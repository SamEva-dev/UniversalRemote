using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace UniversalRemote.Provider.LG;

internal static class LgProtocol
{
    public const int SecurePort = 3001;
    public static Uri UriFor(string host) => new UriBuilder("wss", host, SecurePort).Uri;
    public static string Fingerprint(X509Certificate? certificate) => certificate is null ? string.Empty : Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

    // Explicit JSON writing keeps the wire contract independent of reflection/trimming.
    public static string Register(string? clientKey = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", "register_0");
            writer.WriteString("type", "register");
            writer.WriteStartObject("payload");
            writer.WriteBoolean("forcePairing", false);
            writer.WriteString("pairingType", "PROMPT");
            writer.WriteStartObject("manifest");
            writer.WriteNumber("manifestVersion", 1);
            writer.WriteString("appVersion", "0.7.0");
            writer.WriteStartObject("signed");
            writer.WriteString("created", "20260914");
            writer.WriteString("appId", "com.itech.universalremote");
            writer.WriteString("vendorId", "com.itech");
            writer.WriteStartObject("localizedAppNames");
            writer.WriteString("", "UniversalRemote");
            writer.WriteEndObject();
            writer.WriteStartObject("localizedVendorNames");
            writer.WriteString("", "Itech");
            writer.WriteEndObject();
            WritePermissions(writer, ["LAUNCH", "LAUNCH_WEBAPP", "APP_TO_APP", "CLOSE", "TEST_OPEN", "TEST_PROTECTED", "CONTROL_AUDIO", "CONTROL_POWER", "READ_INSTALLED_APPS", "CONTROL_DISPLAY", "CONTROL_INPUT_JOYSTICK", "CONTROL_INPUT_MEDIA_PLAYBACK", "CONTROL_INPUT_TV", "READ_INPUT_DEVICE_LIST", "READ_NETWORK_STATE", "READ_TV_CHANNEL_LIST", "WRITE_NOTIFICATION_TOAST", "READ_POWER_STATE"]);
            writer.WriteString("serial", "UR-1");
            writer.WriteEndObject();
            WritePermissions(writer, ["LAUNCH", "CONTROL_AUDIO", "CONTROL_POWER", "CONTROL_INPUT_JOYSTICK", "CONTROL_INPUT_MEDIA_PLAYBACK"]);
            writer.WriteEndObject();
            writer.WriteString("client-key", clientKey);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePermissions(Utf8JsonWriter writer, string[] permissions)
    {
        writer.WriteStartArray("permissions");
        foreach (var permission in permissions) writer.WriteStringValue(permission);
        writer.WriteEndArray();
    }

    public static string Request(string id, string uri, bool? mute = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("type", "request");
            writer.WriteString("uri", uri);
            writer.WriteStartObject("payload");
            if (mute is { } value) writer.WriteBoolean("mute", value);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static async Task<string> ReceiveTextAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var ms = new MemoryStream(); var buffer = new byte[4096];
        while (true)
        {
            var r = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (r.MessageType == WebSocketMessageType.Close) throw new WebSocketException("LG TV closed the session.");
            ms.Write(buffer,0,r.Count); if (r.EndOfMessage) break; if (ms.Length > 128*1024) throw new InvalidDataException("LG response is too large.");
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static string? TryClientKey(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "registered") return null;
        if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("client-key", out var key)) return null;
        return key.GetString();
    }

    public static string? TrySocketPath(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("socketPath", out var path)) return null;
        return path.GetString();
    }
}
