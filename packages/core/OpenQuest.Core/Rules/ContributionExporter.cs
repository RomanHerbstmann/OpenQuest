using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Rules;

/// <summary>Serializes accepted attribute changes to files the city can ingest (GeoJSON / CSV).</summary>
public static class ContributionExporter
{
    public static ExportResult ToGeoJson(IReadOnlyCollection<ApprovedContribution> items, string attribution, DateTimeOffset generatedAt)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("type", "FeatureCollection");
            w.WriteString("attribution", attribution);
            w.WriteString("generated_at", generatedAt.ToString("o", CultureInfo.InvariantCulture));
            w.WriteStartArray("features");
            foreach (var c in items)
            {
                w.WriteStartObject();
                w.WriteString("type", "Feature");
                w.WriteStartObject("geometry");
                w.WriteString("type", "Point");
                w.WriteStartArray("coordinates");
                w.WriteNumberValue(c.AssetPosition.Lon);
                w.WriteNumberValue(c.AssetPosition.Lat);
                w.WriteEndArray();
                w.WriteEndObject();
                w.WriteStartObject("properties");
                w.WriteString("change_id", c.ChangeId);
                w.WriteString("submission_id", c.SubmissionId);
                w.WriteString("source", c.DataSourceKey);
                w.WriteString("asset_type", c.AssetTypeKey);
                w.WriteString("external_id", c.ExternalId);
                w.WriteString("attribute", c.AttributeKey);
                WriteJson(w, "old_value", c.OldValueJson);
                WriteJson(w, "new_value", c.NewValueJson);
                w.WriteString("approved_at", c.ApprovedAt.ToString("o", CultureInfo.InvariantCulture));
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return new ExportResult("application/geo+json", $"contributions-{generatedAt:yyyyMMdd-HHmmss}.geojson", ms.ToArray(), items.Count);
    }

    public static ExportResult ToCsv(IReadOnlyCollection<ApprovedContribution> items, string attribution, DateTimeOffset generatedAt)
    {
        var sb = new StringBuilder();
        sb.Append('﻿'); // BOM so Excel opens it as UTF-8
        sb.Append("# ").Append(attribution.ReplaceLineEndings(" ")).Append('\n');
        sb.Append("change_id,submission_id,source,asset_type,external_id,lat,lon,attribute,old_value,new_value,approved_at\n");
        foreach (var c in items)
        {
            sb.AppendJoin(',',
                c.ChangeId.ToString(), c.SubmissionId.ToString(), Csv(c.DataSourceKey), Csv(c.AssetTypeKey), Csv(c.ExternalId),
                c.AssetPosition.Lat.ToString("R", CultureInfo.InvariantCulture),
                c.AssetPosition.Lon.ToString("R", CultureInfo.InvariantCulture),
                Csv(c.AttributeKey), Csv(Plain(c.OldValueJson)), Csv(Plain(c.NewValueJson)),
                c.ApprovedAt.ToString("o", CultureInfo.InvariantCulture));
            sb.Append('\n');
        }
        return new ExportResult("text/csv; charset=utf-8", $"contributions-{generatedAt:yyyyMMdd-HHmmss}.csv", Encoding.UTF8.GetBytes(sb.ToString()), items.Count);
    }

    private static void WriteJson(Utf8JsonWriter w, string name, string? json)
    {
        w.WritePropertyName(name);
        if (json is null) { w.WriteNullValue(); return; }
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.WriteTo(w);
    }

    /// <summary>JSON string values are written without quotes, everything else as JSON text.</summary>
    private static string Plain(string? json)
    {
        if (json is null) return "";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind switch
        {
            JsonValueKind.String => doc.RootElement.GetString() ?? "",
            JsonValueKind.Null => "",
            _ => doc.RootElement.GetRawText(),
        };
    }

    private static string Csv(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
