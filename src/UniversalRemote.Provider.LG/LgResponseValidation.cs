using System.Text.Json;
namespace UniversalRemote.Provider.LG;

internal static class LgResponseValidation
{
    public static bool MatchesSuccessfulResponse(string json, string requestId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != requestId) return false;
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "response")
            throw new InvalidDataException("LG request failed.");
        if (root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("returnValue", out var success) && success.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("LG request was rejected.");
        return true;
    }
    public static Uri PointerUri(string path, string expectedHost)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.Scheme != "wss" ||
            !string.Equals(uri.IdnHost, new UriBuilder("wss", expectedHost).Uri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("LG pointer endpoint is not trusted.");
        return uri;
    }
    public static bool ReadMute(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("mute", out var mute) || mute.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("LG mute state unavailable.");
        return mute.GetBoolean();
    }
}
