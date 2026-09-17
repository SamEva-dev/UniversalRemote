using System.Globalization;
using System.Text.Json;

namespace UniversalRemote.Media.Provider.Xtream;

internal static class XtreamJson
{
    public static string? String(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => NullIfWhiteSpace(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            _ => null
        };
    }

    public static int? Int32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric)) return numeric;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric)) return numeric;
        return null;
    }

    public static bool IsTruthy(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.TryGetInt32(out var numeric) && numeric != 0,
            JsonValueKind.String => ParseTruthy(value.GetString()),
            _ => false
        };
    }

    public static Uri? AbsoluteHttpUri(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return null;
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }

    public static TimeSpan? DurationSeconds(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var seconds = Int32(element, propertyName);
            if (seconds.HasValue && seconds.Value >= 0) return TimeSpan.FromSeconds(seconds.Value);
        }
        return null;
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ParseTruthy(string? value)
        => value is not null && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                                 || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                                 || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
