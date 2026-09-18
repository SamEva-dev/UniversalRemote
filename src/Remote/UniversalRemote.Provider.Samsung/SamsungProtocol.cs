using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace UniversalRemote.Remote.Provider.Samsung;

internal static class SamsungProtocol
{
    public const int SecurePort = 8002;
    public static readonly string ClientName = Convert.ToBase64String(Encoding.UTF8.GetBytes("UniversalRemote"));

    public static Uri BuildUri(string host, string? token = null)
    {
        var query = $"name={Uri.EscapeDataString(ClientName)}";
        if (!string.IsNullOrWhiteSpace(token)) query += $"&token={Uri.EscapeDataString(token)}";
        var builder = new UriBuilder("wss", host, SecurePort, "/api/v2/channels/samsung.remote.control") { Query = query };
        return builder.Uri;
    }

    public static string KeyMessage(string key)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("method", "ms.remote.control");
            writer.WriteStartObject("params");
            writer.WriteString("Cmd", "Click");
            writer.WriteString("DataOfCmd", key);
            writer.WriteString("Option", "false");
            writer.WriteString("TypeOfRemote", "SendRemoteKey");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static async Task<string> ReceiveTextAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var result = await socket.ReceiveAsync(chunk, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("Samsung TV closed the session.");
            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage) break;
            if (buffer.Length > 128 * 1024) throw new InvalidDataException("Samsung response is too large.");
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string? TryGetToken(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("event", out var evt) || evt.GetString() != "ms.channel.connect") return null;
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("token", out var token)) return null;
        return token.GetString();
    }

    public static string Fingerprint(X509Certificate? certificate)
        => certificate is null ? string.Empty : Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
}
