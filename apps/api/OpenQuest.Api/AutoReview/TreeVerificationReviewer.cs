using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;
using OpenQuest.Core.Review;

namespace OpenQuest.Api.AutoReview;

/// <summary>
/// The automatic check of photo submissions: sends the photo to the photo verification of the web app (<c>POST /api/verify</c>, the TypeScript
/// pipeline of <c>packages/tree-verification</c>, ADR-0002) and takes over its verdict (<c>approve</c>, <c>review</c>, <c>reject</c>).
/// "Cannot decide" (the service is not configured, the request was refused) is answered with <see cref="AutoReviewVerdict.Review"/>, so a moderator
/// looks at it; transient failures (network, overload, rate limit) throw, so the outbox retries later.
/// </summary>
public sealed class TreeVerificationReviewer(HttpClient http, IOptions<AutoReviewOptions> options) : ISubmissionAutoReviewer
{
    public async Task<AutoReviewDecision> ReviewAsync(AutoReviewRequest request, CancellationToken ct)
    {
        var url = options.Value.VerifyUrl ?? throw new InvalidOperationException("AutoReview:VerifyUrl is not set.");
        using var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(request.Image);
        image.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
        form.Add(image, "image", "photo.jpg");
        if (request.Player is { } p)
        {
            form.Add(Text(p.Lat), "lat");
            form.Add(Text(p.Lon), "lon");
            if (request.PlayerAccuracyM is { } accuracy) form.Add(Text(accuracy), "accuracy");
        }
        if (request.Expected is { } e)
        {
            form.Add(Text(e.Lat), "expectedLat");
            form.Add(Text(e.Lon), "expectedLon");
        }
        if (!string.IsNullOrWhiteSpace(request.ExpectedGenus)) form.Add(new StringContent(request.ExpectedGenus), "expectedGenus");
        if (request.CapturedAt is { } at) form.Add(new StringContent(at.ToString("o", CultureInfo.InvariantCulture)), "capturedAt");

        using var response = await http.PostAsync(url, form, ct);   // network errors throw: retried
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.OK) return Parse(body);

        var code = ErrorCode(body);
        // the service works but cannot check this: not configured, or it did not accept the request; nothing to retry
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable || (int)response.StatusCode is 400 or 404 or 413 or 415)
            return new AutoReviewDecision(AutoReviewVerdict.Review, [code ?? $"http_{(int)response.StatusCode}"], body);
        // rate limit, overload, server error: try again later
        throw new HttpRequestException($"Photo verification answered {(int)response.StatusCode} {code}".Trim(), null, response.StatusCode);
    }

    private static StringContent Text(double value) => new(value.ToString("R", CultureInfo.InvariantCulture));

    internal static AutoReviewDecision Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("verdict", out var verdict) || verdict.ValueKind != JsonValueKind.String)
            return new AutoReviewDecision(AutoReviewVerdict.Review, ["unexpected_answer"], body);

        var parsed = verdict.GetString() switch
        {
            "approve" => AutoReviewVerdict.Approve,
            "reject" => AutoReviewVerdict.Reject,
            _ => AutoReviewVerdict.Review,   // "review" and anything unknown: a person decides
        };
        var reasons = result.TryGetProperty("reasons", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(r => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("code", out var c) ? c.GetString() : null)
                .Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).ToList()
            : [];
        return new AutoReviewDecision(parsed, reasons, result.GetRawText());
    }

    private static string? ErrorCode(string body)
    {
        try { return JsonDocument.Parse(body).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null; }
        catch (JsonException) { return null; }
    }
}
