using System.Text.Json;

namespace UniversalRemote.Provider.OrangeTv;

internal sealed record OrangeTvStatus(string? FriendlyName, string? MacAddress, string? StandbyState);

internal static class OrangeTvProtocol
{
    private const int MaxStatusBytes = 32 * 1024;

    public static async Task<OrangeTvStatus?> ReadStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentLength is > MaxStatusBytes) return null;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaxStatusBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray());
            if (!document.RootElement.TryGetProperty("result", out var result)) return null;
            if (!result.TryGetProperty("responseCode", out var responseCode)) return null;
            var code = responseCode.ValueKind == JsonValueKind.String ? responseCode.GetString() : responseCode.ToString();
            if (!string.Equals(code, "0", StringComparison.Ordinal)) return null;
            if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;

            return new OrangeTvStatus(
                ReadString(data, "friendlyName"),
                ReadString(data, "macAddress"),
                ReadString(data, "activeStandbyState"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool LooksLikeOrangeTv(OrangeTvStatus status)
    {
        var name = status.FriendlyName ?? string.Empty;
        return name.Contains("orange", StringComparison.OrdinalIgnoreCase)
            || name.Contains("decodeur tv", StringComparison.OrdinalIgnoreCase)
            || name.Contains("décodeur tv", StringComparison.OrdinalIgnoreCase)
            || name.Contains("tv uhd", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}
