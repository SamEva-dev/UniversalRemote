using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Hub.Wifi;

/// <summary>
/// Reflection-free client for the versioned Wi-Fi transmit endpoint.
/// There is exactly one POST attempt per call. Once SendAsync has been entered, a transport failure is considered
/// delivery-ambiguous and is therefore returned as Unknown rather than retried.
/// </summary>
internal sealed class WifiInfraredHubTransmitClient(HttpClient httpClient)
{
    public async Task<InfraredHubTransmitResult> TransmitAsync(
        WifiInfraredHubConnection connection,
        InfraredSignal signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();

        if (connection.ApiVersion != WifiInfraredHubProtocol.ApiVersion)
            return InfraredHubTransmitResult.FailedBeforeSend();

        var requestId = Guid.NewGuid().ToString("N");
        byte[] payload;
        try
        {
            payload = EncodeRequest(connection.HubId, requestId, signal);
        }
        catch (ArgumentException)
        {
            return InfraredHubTransmitResult.InvalidSignal();
        }
        catch (OverflowException)
        {
            return InfraredHubTransmitResult.InvalidSignal();
        }

        if (payload.Length > WifiInfraredHubProtocol.MaxTransmitRequestBytes)
            return InfraredHubTransmitResult.InvalidSignal();

        using var request = new HttpRequestMessage(HttpMethod.Post, WifiInfraredHubEndpoint.Transmit(connection));
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        HttpResponseMessage response;
        try
        {
            // From this point onward, delivery can be ambiguous: the request may have reached the hub even if
            // SendAsync later throws. Do not retry here or in any caller.
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return InfraredHubTransmitResult.Unknown();
        }
        catch (HttpRequestException)
        {
            return InfraredHubTransmitResult.Unknown();
        }
        catch (IOException)
        {
            return InfraredHubTransmitResult.Unknown();
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Moved
                or HttpStatusCode.Redirect
                or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect)
                return InfraredHubTransmitResult.Unknown();

            if (response.Content.Headers.ContentLength is > WifiInfraredHubProtocol.MaxTransmitResponseBytes)
                return InfraredHubTransmitResult.Unknown();

            byte[] responsePayload;
            try
            {
                responsePayload = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return InfraredHubTransmitResult.Unknown();
            }
            catch (IOException)
            {
                return InfraredHubTransmitResult.Unknown();
            }
            catch (InvalidDataException)
            {
                return InfraredHubTransmitResult.Unknown();
            }

            return ParseResponse(responsePayload, response.IsSuccessStatusCode, connection.HubId, requestId);
        }
    }

    private static byte[] EncodeRequest(InfraredHubId hubId, string requestId, InfraredSignal signal)
    {
        var buffer = new ArrayBufferWriter<byte>(Math.Min(WifiInfraredHubProtocol.MaxTransmitRequestBytes, 32 * 1024));
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", WifiInfraredHubProtocol.ApiVersion);
            writer.WriteString("requestId", requestId);
            writer.WriteString("hubId", hubId.Value);
            writer.WriteNumber("carrierFrequencyHz", signal.CarrierFrequencyHz);
            writer.WritePropertyName("patternMicroseconds");
            writer.WriteStartArray();
            foreach (var duration in signal.PatternMicroseconds)
                writer.WriteNumberValue(duration);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.WrittenMemory.ToArray();
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[2048];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > WifiInfraredHubProtocol.MaxTransmitResponseBytes)
                throw new InvalidDataException("Hub transmit response is too large.");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }

    private static InfraredHubTransmitResult ParseResponse(
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
                MaxDepth = 8
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return InfraredHubTransmitResult.Unknown();
            if (!TryInt(root, "schemaVersion", out var schemaVersion) || schemaVersion != WifiInfraredHubProtocol.ApiVersion)
                return InfraredHubTransmitResult.Unknown();
            if (!TryString(root, "requestId", 64, out var requestId)
                || !string.Equals(requestId, expectedRequestId, StringComparison.Ordinal))
                return InfraredHubTransmitResult.Unknown();
            if (!TryString(root, "hubId", 128, out var hubId)
                || !string.Equals(hubId, expectedHubId.Value, StringComparison.Ordinal))
                return InfraredHubTransmitResult.Unknown();
            if (!TryString(root, "outcome", 64, out var outcome))
                return InfraredHubTransmitResult.Unknown();

            return outcome.ToLowerInvariant() switch
            {
                "accepted" when isSuccessStatusCode => InfraredHubTransmitResult.Accepted(),
                "hub_unavailable" => InfraredHubTransmitResult.HubUnavailable(),
                "frequency_unsupported" => InfraredHubTransmitResult.FrequencyUnsupported(),
                "invalid_signal" => InfraredHubTransmitResult.InvalidSignal(),
                "failed_before_send" => InfraredHubTransmitResult.FailedBeforeSend(),
                _ => InfraredHubTransmitResult.Unknown()
            };
        }
        catch (JsonException)
        {
            return InfraredHubTransmitResult.Unknown();
        }
    }

    private static bool TryString(JsonElement element, string name, int maxLength, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
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
