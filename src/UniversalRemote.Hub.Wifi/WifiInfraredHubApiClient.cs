using System.Net;
using System.Text.Json;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Hub.Wifi;

internal enum WifiInfraredHubProbeOutcome
{
    Success,
    Unreachable,
    IdentityMismatch,
    UnsupportedApiVersion,
    InvalidResponse
}

internal sealed record WifiInfraredHubProbeResult(
    WifiInfraredHubProbeOutcome Outcome,
    InfraredHubInfo? Info = null)
{
    public static WifiInfraredHubProbeResult Success(InfraredHubInfo info) => new(WifiInfraredHubProbeOutcome.Success, info);
}

/// <summary>Small, bounded, reflection-free client for the versioned hub info endpoint.</summary>
internal sealed class WifiInfraredHubApiClient(HttpClient httpClient)
{
    public async Task<WifiInfraredHubProbeResult> ProbeAsync(
        WifiInfraredHubConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var request = new HttpRequestMessage(HttpMethod.Get, WifiInfraredHubEndpoint.Info(connection));
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new(WifiInfraredHubProbeOutcome.Unreachable);
        }
        catch (HttpRequestException)
        {
            return new(WifiInfraredHubProbeOutcome.Unreachable);
        }
        catch (IOException)
        {
            return new(WifiInfraredHubProbeOutcome.Unreachable);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                return new(WifiInfraredHubProbeOutcome.InvalidResponse);
            if (!response.IsSuccessStatusCode)
                return new(WifiInfraredHubProbeOutcome.Unreachable);
            if (response.Content.Headers.ContentLength is > WifiInfraredHubProtocol.MaxInfoPayloadBytes)
                return new(WifiInfraredHubProbeOutcome.InvalidResponse);

            byte[] payload;
            try
            {
                payload = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return new(WifiInfraredHubProbeOutcome.Unreachable);
            }
            catch (InvalidDataException)
            {
                return new(WifiInfraredHubProbeOutcome.InvalidResponse);
            }
            catch (IOException)
            {
                return new(WifiInfraredHubProbeOutcome.Unreachable);
            }

            return Parse(payload, connection);
        }
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
            if (destination.Length + read > WifiInfraredHubProtocol.MaxInfoPayloadBytes)
                throw new InvalidDataException("Hub info payload is too large.");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }

    private static WifiInfraredHubProbeResult Parse(byte[] payload, WifiInfraredHubConnection connection)
    {
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(WifiInfraredHubProbeOutcome.InvalidResponse);

            if (!TryInt(root, "schemaVersion", out var schemaVersion)) return new(WifiInfraredHubProbeOutcome.InvalidResponse);
            if (schemaVersion != WifiInfraredHubProtocol.ApiVersion || connection.ApiVersion != WifiInfraredHubProtocol.ApiVersion)
                return new(WifiInfraredHubProbeOutcome.UnsupportedApiVersion);

            if (!TryString(root, "hubId", out var hubIdRaw) || !TryString(root, "displayName", out var displayName))
                return new(WifiInfraredHubProbeOutcome.InvalidResponse);

            InfraredHubId returnedId;
            try { returnedId = new InfraredHubId(hubIdRaw); }
            catch (ArgumentException) { return new(WifiInfraredHubProbeOutcome.InvalidResponse); }

            if (!string.Equals(returnedId.Value, connection.HubId.Value, StringComparison.Ordinal))
                return new(WifiInfraredHubProbeOutcome.IdentityMismatch);

            var state = InfraredHubConnectionState.Ready;
            if (TryString(root, "state", out var stateRaw))
            {
                state = stateRaw.ToLowerInvariant() switch
                {
                    "ready" => InfraredHubConnectionState.Ready,
                    "busy" => InfraredHubConnectionState.Busy,
                    "offline" => InfraredHubConnectionState.Offline,
                    "faulted" => InfraredHubConnectionState.Faulted,
                    _ => throw new InvalidDataException("Unknown hub state.")
                };
            }

            if (!root.TryGetProperty("capabilities", out var capabilitiesElement) || capabilitiesElement.ValueKind != JsonValueKind.Object)
                return new(WifiInfraredHubProbeOutcome.InvalidResponse);

            var canTransmit = TryBool(capabilitiesElement, "canTransmit", out var transmit) && transmit;
            var canLearn = TryBool(capabilitiesElement, "canLearn", out var learn) && learn;
            var ranges = ParseRanges(capabilitiesElement);
            var maxPatternValues = TryInt(capabilitiesElement, "maxPatternValues", out var maxPattern)
                ? maxPattern : InfraredSignalLimits.MaxPatternValues;
            var maxDuration = TryInt(capabilitiesElement, "maxTotalDurationMicroseconds", out var duration)
                ? duration : InfraredSignalLimits.MaxTotalDurationMicroseconds;

            var capabilities = new InfraredHubCapabilities(canTransmit, canLearn, ranges, maxPatternValues, maxDuration);
            var firmware = TryString(root, "firmwareVersion", out var firmwareRaw) ? firmwareRaw : null;
            var diagnostic = state switch
            {
                InfraredHubConnectionState.Ready => "hub.wifi.ready",
                InfraredHubConnectionState.Busy => "hub.wifi.busy",
                InfraredHubConnectionState.Offline => "hub.wifi.offline",
                _ => "hub.wifi.faulted"
            };

            return WifiInfraredHubProbeResult.Success(new InfraredHubInfo(
                returnedId,
                displayName,
                InfraredHubTransportKind.Wifi,
                state,
                capabilities,
                diagnostic,
                firmware));
        }
        catch (JsonException)
        {
            return new(WifiInfraredHubProbeOutcome.InvalidResponse);
        }
        catch (InvalidDataException)
        {
            return new(WifiInfraredHubProbeOutcome.InvalidResponse);
        }
        catch (ArgumentException)
        {
            return new(WifiInfraredHubProbeOutcome.InvalidResponse);
        }
        catch (OverflowException)
        {
            return new(WifiInfraredHubProbeOutcome.InvalidResponse);
        }
    }

    private static IReadOnlyList<InfraredFrequencyRange> ParseRanges(JsonElement capabilities)
    {
        if (!capabilities.TryGetProperty("carrierFrequencies", out var value) || value.ValueKind != JsonValueKind.Array)
            return [];
        var ranges = new List<InfraredFrequencyRange>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !TryInt(element, "minHz", out var min)
                || !TryInt(element, "maxHz", out var max))
                throw new InvalidDataException("Invalid carrier-frequency range.");
            ranges.Add(new InfraredFrequencyRange(min, max));
        }
        return ranges;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length is > 0 and <= 256;
    }

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = default;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (property.ValueKind == JsonValueKind.False) { value = false; return true; }
        return false;
    }
}
