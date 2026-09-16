using System.Buffers;
using System.Net;
using System.Text.Json;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Hub.Wifi;

/// <summary>One-attempt learning request. Captured signals are bounded and revalidated before leaving the transport.</summary>
internal sealed class WifiInfraredHubLearnClient(HttpClient httpClient)
{
    public async Task<InfraredHubLearnResult> LearnAsync(
        WifiInfraredHubConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        var payload = EncodeRequest(connection.HubId, requestId);
        if (payload.Length > WifiInfraredHubProtocol.MaxLearnRequestBytes) return InfraredHubLearnResult.Failed();

        using var request = new HttpRequestMessage(HttpMethod.Post, WifiInfraredHubEndpoint.Learn(connection));
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return InfraredHubLearnResult.Timeout(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException) { return InfraredHubLearnResult.Failed(); }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                return InfraredHubLearnResult.Failed();
            if (response.Content.Headers.ContentLength is > WifiInfraredHubProtocol.MaxLearnResponseBytes)
                return InfraredHubLearnResult.InvalidCapture();

            byte[] body;
            try { body = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { return InfraredHubLearnResult.Timeout(); }
            catch (InvalidDataException) { return InfraredHubLearnResult.InvalidCapture(); }
            catch (IOException) { return InfraredHubLearnResult.Failed(); }

            return ParseResponse(body, response.IsSuccessStatusCode, connection.HubId, requestId);
        }
    }

    private static byte[] EncodeRequest(InfraredHubId hubId, string requestId)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", WifiInfraredHubProtocol.ApiVersion);
        writer.WriteString("requestId", requestId);
        writer.WriteString("hubId", hubId.Value);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory.ToArray();
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > WifiInfraredHubProtocol.MaxLearnResponseBytes)
                throw new InvalidDataException("Hub learning response is too large.");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }

    private static InfraredHubLearnResult ParseResponse(
        byte[] payload,
        bool isSuccessStatusCode,
        InfraredHubId expectedHubId,
        string expectedRequestId)
    {
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 12
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return InfraredHubLearnResult.Failed();
            if (!TryInt(root, "schemaVersion", out var version) || version != WifiInfraredHubProtocol.ApiVersion)
                return InfraredHubLearnResult.Failed();
            if (!TryString(root, "requestId", 64, out var requestId) || !string.Equals(requestId, expectedRequestId, StringComparison.Ordinal))
                return InfraredHubLearnResult.Failed();
            if (!TryString(root, "hubId", 128, out var hubId) || !string.Equals(hubId, expectedHubId.Value, StringComparison.Ordinal))
                return InfraredHubLearnResult.Failed();
            if (!TryString(root, "outcome", 48, out var outcome)) return InfraredHubLearnResult.Failed();

            switch (outcome.ToLowerInvariant())
            {
                case "captured" when isSuccessStatusCode:
                    if (!TryInt(root, "carrierFrequencyHz", out var frequency)
                        || !root.TryGetProperty("patternMicroseconds", out var patternElement)
                        || patternElement.ValueKind != JsonValueKind.Array)
                        return InfraredHubLearnResult.InvalidCapture();
                    var pattern = new List<int>();
                    foreach (var item in patternElement.EnumerateArray())
                    {
                        if (!item.TryGetInt32(out var duration)) return InfraredHubLearnResult.InvalidCapture();
                        pattern.Add(duration);
                        if (pattern.Count > InfraredSignalLimits.MaxPatternValues) return InfraredHubLearnResult.InvalidCapture();
                    }
                    try { return InfraredHubLearnResult.Captured(new InfraredSignal(frequency, pattern)); }
                    catch (ArgumentException) { return InfraredHubLearnResult.InvalidCapture(); }
                case "timeout": return InfraredHubLearnResult.Timeout();
                case "unsupported": return InfraredHubLearnResult.Unsupported();
                case "invalid_capture": return InfraredHubLearnResult.InvalidCapture();
                default: return InfraredHubLearnResult.Failed();
            }
        }
        catch (JsonException) { return InfraredHubLearnResult.Failed(); }
    }

    private static bool TryString(JsonElement element, string name, int maxLength, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length is > 0 && value.Length <= maxLength;
    }

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = default;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }
}
