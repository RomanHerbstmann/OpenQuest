using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.AutoReview;
using OpenQuest.Api.Config;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;
using OpenQuest.Core.Review;

namespace OpenQuest.Api.Tests;

/// <summary>Phase 6: the automatic check of photo submissions (scripted here; the HTTP checker has its own tests below).</summary>
[Collection(ApiCollection.Name)]
public class AutoReviewTests(ApiFactory api)
{
    private const double Lon = 9.0;
    private static int _seed = 5000;

    private async Task<(Guid SubmissionId, Guid QuestId, HttpClient Player)> SubmitPhotoAsync(double lat, int reward = 10, string taskType = "photo", string? genus = null)
    {
        var admin = await api.AdminAsync();
        var tree = await api.AddTreeAsync(lat, Lon, genus);
        var questId = await api.CreateQuestAsync(admin, tree, taskType, rewardPoints: reward);
        var (player, _, _) = await api.RegisterAsync("shooter");
        var claim = (await (await player.PostAsync($"/quests/{questId}/claim", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var form = new MultipartFormDataContent
        {
            { new StringContent(lat.ToString(CultureInfo.InvariantCulture)), "lat" },
            { new StringContent(Lon.ToString(CultureInfo.InvariantCulture)), "lon" },
            { new StringContent("{}"), "payload" },
        };
        var file = new ByteArrayContent(PhotoProcessorTests.MakeJpeg(320, 240, seed: Interlocked.Increment(ref _seed)));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "photo", "photo.jpg");
        var res = await player.PostAsync($"/claims/{claim}/submit", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return ((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submissionId").GetGuid(), questId, player);
    }

    private async Task<Submission> WaitForAutoReviewAsync(Guid submissionId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var s = await api.WithDb(db => db.Submissions.AsNoTracking().SingleAsync(x => x.Id == submissionId));
            if (s.AutoReview is not null) return s;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("The automatic check did not run within 20 s.");
    }

    private async Task<Submission> WaitForStatusAsync(Guid submissionId, SubmissionStatus status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var s = await api.WithDb(db => db.Submissions.AsNoTracking().SingleAsync(x => x.Id == submissionId));
            if (s.Status == status) return s;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException($"The submission did not become {status} within 20 s.");
    }

    // ---- the decision -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_sure_check_approves_the_submission_without_a_moderator_and_everything_after_an_approval_follows()
    {
        var lat = 41.0;
        api.AutoReviewer.On(lat, AutoReviewVerdict.Approve, "tree_visible", "genus_matches");
        var (submissionId, _, player) = await SubmitPhotoAsync(lat, reward: 30, genus: "Tilia");

        var approved = await WaitForStatusAsync(submissionId, SubmissionStatus.Approved);
        Assert.Null(approved.ReviewedBy);               // nobody: the automatic check did it
        Assert.NotNull(approved.ReviewedAt);
        var auto = JsonDocument.Parse(approved.AutoReview!).RootElement;
        Assert.Equal("approve", auto.GetProperty("verdict").GetString());
        Assert.Equal(["tree_visible", "genus_matches"], auto.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToArray());
        Assert.True(auto.GetProperty("details").GetProperty("scripted").GetBoolean());

        // the same consequences as an approval by a moderator: change accepted and published, points, card
        await Feed.WaitForChangesAsync(api, submissionId);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        JsonElement me = default;
        while (DateTime.UtcNow < deadline)
        {
            me = await player.GetFromJsonAsync<JsonElement>("/me");
            if (me.GetProperty("totalPoints").GetInt32() == 30) break;
            await Task.Delay(50);
        }
        Assert.Equal(30, me.GetProperty("totalPoints").GetInt32());
        var cards = await player.GetFromJsonAsync<JsonElement>("/me/cards");
        Assert.Equal("Tilia", cards.EnumerateArray().Single().GetProperty("genus").GetString());

        // the moderator sees who decided
        var admin = await api.AdminAsync();
        var listed = (await admin.GetFromJsonAsync<JsonElement>("/admin/submissions?status=approved&limit=200")).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == submissionId);
        Assert.Equal("approve", listed.GetProperty("autoReview").GetProperty("verdict").GetString());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("reviewedBy").ValueKind);
    }

    [Theory]
    [InlineData(AutoReviewVerdict.Review, 41.1)]
    [InlineData(AutoReviewVerdict.Reject, 41.2)]
    public async Task Anything_but_a_sure_approval_leaves_the_submission_with_the_moderator_and_never_rejects(AutoReviewVerdict verdict, double lat)
    {
        api.AutoReviewer.On(lat, verdict, "no_tree_visible");
        var (submissionId, _, player) = await SubmitPhotoAsync(lat);

        var checkedSubmission = await WaitForAutoReviewAsync(submissionId);
        Assert.Equal(SubmissionStatus.Pending, checkedSubmission.Status);
        Assert.Equal(verdict.ToString().ToLowerInvariant(), JsonDocument.Parse(checkedSubmission.AutoReview!).RootElement.GetProperty("verdict").GetString());
        Assert.Null(checkedSubmission.RejectionReason);
        await Task.Delay(300);   // nothing follows
        Assert.Equal(SubmissionStatus.Pending, (await api.WithDb(db => db.Submissions.AsNoTracking().SingleAsync(x => x.Id == submissionId))).Status);
        Assert.Equal(0, (await player.GetFromJsonAsync<JsonElement>("/me")).GetProperty("totalPoints").GetInt32());

        // the moderator can still decide, and sees the verdict next to the photo
        var admin = await api.AdminAsync();
        var queued = (await admin.GetFromJsonAsync<JsonElement>("/admin/submissions?limit=200")).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == submissionId);
        Assert.Equal(verdict.ToString().ToLowerInvariant(), queued.GetProperty("autoReview").GetProperty("verdict").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
    }

    [Fact]
    public async Task The_check_gets_the_photo_the_position_of_the_player_and_what_is_known_about_the_tree()
    {
        var lat = 41.3;
        api.AutoReviewer.On(lat, AutoReviewVerdict.Review);
        var (submissionId, _, _) = await SubmitPhotoAsync(lat, genus: "Quercus");
        await WaitForAutoReviewAsync(submissionId);

        var call = api.AutoReviewer.Calls.Single(c => c.Expected is { } e && Math.Abs(e.Lat - lat) < 1e-6);
        Assert.Equal("image/jpeg", call.ContentType);
        Assert.True(call.Image.Length > 100 && call.Image[0] == 0xFF && call.Image[1] == 0xD8);   // the stored JPEG, not the original upload
        Assert.Equal(lat, call.Player!.Value.Lat, 6);
        Assert.Equal(Lon, call.Player!.Value.Lon, 6);
        Assert.Equal("Quercus", call.ExpectedGenus);
    }

    [Fact]
    public async Task Only_photo_submissions_of_the_configured_task_types_are_checked()
    {
        var lat = 41.4;
        var admin = await api.AdminAsync();
        var tree = await api.AddTreeAsync(lat, Lon);
        var questId = await api.CreateQuestAsync(admin, tree);   // verify_attribute: no photo at all
        var (player, _, _) = await api.RegisterAsync("typist");
        var claim = (await (await player.PostAsync($"/quests/{questId}/claim", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var form = new MultipartFormDataContent
        {
            { new StringContent(lat.ToString(CultureInfo.InvariantCulture)), "lat" }, { new StringContent(Lon.ToString(CultureInfo.InvariantCulture)), "lon" },
            { new StringContent("{\"value\":\"Tilia\"}"), "payload" },
        };
        api.AutoReviewer.On(lat, AutoReviewVerdict.Approve);
        var submissionId = (await (await player.PostAsync($"/claims/{claim}/submit", form)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submissionId").GetGuid();
        // the event of the submission is delivered, but the handler leaves it alone
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && !await api.WithDb(db => db.OutboxMessages.AnyAsync(m => m.Type == nameof(SubmissionSubmitted) && m.Status == OutboxStatus.Processed
                   && EF.Functions.JsonContains(m.Payload, "{\"submissionId\":\"" + submissionId + "\"}")))) await Task.Delay(50);
        var stored = await api.WithDb(db => db.Submissions.AsNoTracking().SingleAsync(x => x.Id == submissionId));
        Assert.Equal((SubmissionStatus.Pending, null), (stored.Status, stored.AutoReview));
        Assert.Equal(0, api.AutoReviewer.AttemptsFor(lat));
    }

    [Fact]
    public async Task A_failing_check_is_retried_and_then_decides()
    {
        var lat = 41.5;
        api.AutoReviewer.On(lat, attempt => attempt < 3
            ? throw new HttpRequestException("busy")
            : new AutoReviewDecision(AutoReviewVerdict.Approve, ["tree_visible"]));
        var (submissionId, _, _) = await SubmitPhotoAsync(lat);

        var approved = await WaitForStatusAsync(submissionId, SubmissionStatus.Approved);
        Assert.NotNull(approved.AutoReview);
        Assert.Equal(3, api.AutoReviewer.AttemptsFor(lat));
    }

    [Fact]
    public async Task A_moderator_who_was_faster_keeps_the_last_word()
    {
        var lat = 41.6;
        var release = new TaskCompletionSource();
        api.AutoReviewer.On(lat, _ =>
        {
            release.Task.GetAwaiter().GetResult();   // the check is slow
            return new AutoReviewDecision(AutoReviewVerdict.Approve, ["tree_visible"]);
        });
        var (submissionId, _, _) = await SubmitPhotoAsync(lat);
        var admin = await api.AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = false, reason = "unscharf" })).StatusCode);
        release.SetResult();
        await Task.Delay(500);

        var s = await api.WithDb(db => db.Submissions.AsNoTracking().SingleAsync(x => x.Id == submissionId));
        Assert.Equal((SubmissionStatus.Rejected, "unscharf"), (s.Status, s.RejectionReason));
    }

    // ---- the HTTP checker (talks to POST /api/verify of the web app) --------------------------------------------------------------

    private sealed class FakeVerify(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public List<(string Name, string Value)> Fields { get; } = [];
        public string? Url { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.ToString();
            var form = (MultipartFormDataContent)request.Content!;
            foreach (var part in form)
                Fields.Add((part.Headers.ContentDisposition!.Name!.Trim('"'), part.Headers.ContentDisposition.FileName is null ? await part.ReadAsStringAsync(ct) : $"file:{(await part.ReadAsByteArrayAsync(ct)).Length}"));
            return await answer(request);
        }
    }

    private static TreeVerificationReviewer Checker(FakeVerify handler)
        => new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new AutoReviewOptions { Enabled = true, VerifyUrl = "https://openquest.example/api/verify" }));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static readonly AutoReviewRequest Request = new(
        [1, 2, 3], "image/jpeg", new GeoPoint(51.9601, 7.6201), 12.5, new GeoPoint(51.96, 7.62), "Tilia", new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task The_checker_sends_the_photo_and_the_expected_tree_to_the_verification_and_takes_over_its_verdict()
    {
        var fake = new FakeVerify(_ => Task.FromResult(Json(HttpStatusCode.OK,
            """{"expected":null,"result":{"verdict":"approve","reasons":[{"code":"tree_visible","hard":false},{"code":"genus_matches"}],"model":"x"}}""")));
        var decision = await Checker(fake).ReviewAsync(Request, CancellationToken.None);

        Assert.Equal(AutoReviewVerdict.Approve, decision.Verdict);
        Assert.Equal(["tree_visible", "genus_matches"], decision.Reasons);
        Assert.Contains("\"model\":\"x\"", decision.DetailsJson);
        Assert.Equal("https://openquest.example/api/verify", fake.Url);
        var fields = fake.Fields.ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("file:3", fields["image"]);
        Assert.Equal(("51.9601", "7.6201", "12.5"), (fields["lat"], fields["lon"], fields["accuracy"]));
        Assert.Equal(("51.96", "7.62", "Tilia"), (fields["expectedLat"], fields["expectedLon"], fields["expectedGenus"]));
        Assert.StartsWith("2026-09-26T10:00:00", fields["capturedAt"]);
    }

    [Theory]
    [InlineData("review", AutoReviewVerdict.Review)]
    [InlineData("reject", AutoReviewVerdict.Reject)]
    [InlineData("something new", AutoReviewVerdict.Review)]   // unknown verdicts are a moderator's job
    public async Task The_other_verdicts_are_passed_on_and_unknown_ones_are_left_to_a_person(string verdict, AutoReviewVerdict expected)
    {
        var fake = new FakeVerify(_ => Task.FromResult(Json(HttpStatusCode.OK, $$$"""{"result":{"verdict":"{{{verdict}}}","reasons":[{"code":"no_tree_visible"}]}}""")));
        Assert.Equal(expected, (await Checker(fake).ReviewAsync(Request, CancellationToken.None)).Verdict);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":"verification_unavailable"}""", "verification_unavailable")]
    [InlineData(HttpStatusCode.UnsupportedMediaType, """{"error":"unsupported_image_type"}""", "unsupported_image_type")]
    [InlineData(HttpStatusCode.OK, """{"unexpected":true}""", "unexpected_answer")]
    public async Task What_the_service_cannot_check_goes_to_a_moderator(HttpStatusCode status, string body, string reason)
    {
        var decision = await Checker(new FakeVerify(_ => Task.FromResult(Json(status, body)))).ReviewAsync(Request, CancellationToken.None);
        Assert.Equal(AutoReviewVerdict.Review, decision.Verdict);
        Assert.Equal([reason], decision.Reasons);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Overload_and_server_errors_are_thrown_so_that_the_outbox_tries_again(HttpStatusCode status)
    {
        var fake = new FakeVerify(_ => Task.FromResult(Json(status, """{"error":"rate_limited"}""")));
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => Checker(fake).ReviewAsync(Request, CancellationToken.None));
        Assert.Equal(status, e.StatusCode);
    }

    [Fact]
    public async Task A_network_error_is_thrown_too()
    {
        var fake = new FakeVerify(_ => throw new HttpRequestException("connection refused"));
        await Assert.ThrowsAsync<HttpRequestException>(() => Checker(fake).ReviewAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task A_tree_that_is_not_in_the_data_is_sent_without_an_expected_position()
    {
        var fake = new FakeVerify(_ => Task.FromResult(Json(HttpStatusCode.OK, """{"result":{"verdict":"review","reasons":[]}}""")));
        await Checker(fake).ReviewAsync(Request with { Expected = null, ExpectedGenus = null, CapturedAt = null, PlayerAccuracyM = null }, CancellationToken.None);
        var names = fake.Fields.Select(f => f.Name).ToArray();
        Assert.DoesNotContain("expectedLat", names);
        Assert.DoesNotContain("expectedGenus", names);
        Assert.DoesNotContain("capturedAt", names);
        Assert.Contains("lat", names);
    }
}
