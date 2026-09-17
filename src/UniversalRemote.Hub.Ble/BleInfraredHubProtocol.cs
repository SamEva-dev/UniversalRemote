using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Hub.Ble;

/// <summary>Versioned UniversalRemote BLE GATT profile for external IR hubs.</summary>
public static class BleInfraredHubProtocol
{
    public const int ApiVersion = 1;
    public const int AdvertisementServiceDataBytes = 17; // version + canonical 128-bit hub id
    public const int MaxInfoBytes = 4 * 1024;
    public const int MaxTransmitPayloadBytes = 32 * 1024;
    public const int MaxLearnPayloadBytes = 32 * 1024;
    public const int DefaultAttMtu = 23;
    public const int PreferredAttMtu = 247;
    public const int GattFrameHeaderBytes = 6;
    public const byte GattFrameVersion = 1;

    public static Guid ServiceUuid { get; } = Guid.Parse("7d9f1000-7c3a-4d55-a1b8-6f7a1b0c0001");
    public static Guid InfoCharacteristicUuid { get; } = Guid.Parse("7d9f1001-7c3a-4d55-a1b8-6f7a1b0c0001");
    public static Guid TransmitCharacteristicUuid { get; } = Guid.Parse("7d9f1002-7c3a-4d55-a1b8-6f7a1b0c0001");
    public static Guid AcknowledgementCharacteristicUuid { get; } = Guid.Parse("7d9f1003-7c3a-4d55-a1b8-6f7a1b0c0001");
    public static Guid LearnCharacteristicUuid { get; } = Guid.Parse("7d9f1004-7c3a-4d55-a1b8-6f7a1b0c0001");

    public static bool TryParseAdvertisementServiceData(
        ReadOnlySpan<byte> data,
        out InfraredHubId hubId,
        out int apiVersion)
    {
        hubId = default;
        apiVersion = 0;
        if (data.Length != AdvertisementServiceDataBytes || data[0] != ApiVersion) return false;

        var hex = Convert.ToHexString(data[1..]).ToLowerInvariant();
        if (!Guid.TryParseExact(hex, "N", out var guid) || guid == Guid.Empty) return false;
        hubId = new InfraredHubId(guid.ToString("N"));
        apiVersion = data[0];
        return true;
    }

    public static byte[] BuildAdvertisementServiceData(Guid hubId)
    {
        if (hubId == Guid.Empty) throw new ArgumentException("BLE hub id must not be empty.", nameof(hubId));
        var bytes = new byte[AdvertisementServiceDataBytes];
        bytes[0] = ApiVersion;
        Convert.FromHexString(hubId.ToString("N")).CopyTo(bytes, 1);
        return bytes;
    }

    internal static bool TryParseInfo(ReadOnlySpan<byte> payload, InfraredHubId expectedId, out InfraredHubInfo? info)
    {
        info = null;
        if (payload.Length is <= 0 or > MaxInfoBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;
            if (!TryInt(root, "schemaVersion", out var schemaVersion) || schemaVersion != ApiVersion) return false;
            if (!TryString(root, "hubId", 128, out var hubIdRaw)) return false;
            var hubId = new InfraredHubId(hubIdRaw);
            if (!string.Equals(hubId.Value, expectedId.Value, StringComparison.Ordinal)) return false;
            if (!TryString(root, "displayName", 128, out var displayName)) return false;
            if (!TryString(root, "state", 24, out var stateRaw)) return false;
            var state = stateRaw.ToLowerInvariant() switch
            {
                "ready" => InfraredHubConnectionState.Ready,
                "busy" => InfraredHubConnectionState.Busy,
                "offline" => InfraredHubConnectionState.Offline,
                "faulted" => InfraredHubConnectionState.Faulted,
                _ => (InfraredHubConnectionState?)null
            };
            if (state is null) return false;
            if (!root.TryGetProperty("canTransmit", out var canTransmitElement)
                || canTransmitElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            if (!root.TryGetProperty("canLearn", out var canLearnElement)
                || canLearnElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            if (!root.TryGetProperty("carrierFrequencies", out var rangesElement)
                || rangesElement.ValueKind != JsonValueKind.Array) return false;

            var ranges = new List<InfraredFrequencyRange>();
            foreach (var rangeElement in rangesElement.EnumerateArray())
            {
                if (!TryInt(rangeElement, "minHz", out var minHz) || !TryInt(rangeElement, "maxHz", out var maxHz)) return false;
                ranges.Add(new InfraredFrequencyRange(minHz, maxHz));
                if (ranges.Count > 16) return false;
            }

            var firmware = TryOptionalString(root, "firmwareVersion", 64);
            var maxPatternValues = TryInt(root, "maxPatternValues", out var mpv) ? mpv : InfraredSignalLimits.MaxPatternValues;
            var maxDuration = TryInt(root, "maxTotalDurationMicroseconds", out var md) ? md : InfraredSignalLimits.MaxTotalDurationMicroseconds;
            var capabilities = new InfraredHubCapabilities(
                canTransmitElement.GetBoolean(),
                canLearnElement.GetBoolean(),
                ranges,
                maxPatternValues,
                maxDuration);
            info = new InfraredHubInfo(
                hubId,
                displayName,
                InfraredHubTransportKind.BluetoothLowEnergy,
                state.Value,
                capabilities,
                "hub.ble.ready",
                firmware);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    internal static byte[] EncodeTransmit(InfraredHubId hubId, Guid requestId, InfraredSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (!Guid.TryParseExact(hubId.Value, "N", out var hubGuid) || hubGuid == Guid.Empty)
            throw new ArgumentException("BLE hub identifiers must be canonical UUID values.", nameof(hubId));
        if (requestId == Guid.Empty) throw new ArgumentException("Request id must not be empty.", nameof(requestId));
        if (signal.PatternMicroseconds.Count > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(signal));

        var length = 1 + 16 + 16 + 4 + 2 + checked(signal.PatternMicroseconds.Count * 4);
        if (length > MaxTransmitPayloadBytes) throw new ArgumentOutOfRangeException(nameof(signal));
        var payload = new byte[length];
        var offset = 0;
        payload[offset++] = ApiVersion;
        WriteCanonicalGuid(requestId, payload.AsSpan(offset, 16)); offset += 16;
        WriteCanonicalGuid(hubGuid, payload.AsSpan(offset, 16)); offset += 16;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, 4), signal.CarrierFrequencyHz); offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset, 2), checked((ushort)signal.PatternMicroseconds.Count)); offset += 2;
        foreach (var duration in signal.PatternMicroseconds)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, 4), duration);
            offset += 4;
        }
        return payload;
    }

    internal static bool TryParseAcknowledgement(
        ReadOnlySpan<byte> payload,
        InfraredHubId expectedHubId,
        Guid expectedRequestId,
        out InfraredHubTransmitResult result)
    {
        result = InfraredHubTransmitResult.Unknown();
        if (payload.Length != 34 || payload[0] != ApiVersion) return false;
        var requestId = ReadCanonicalGuid(payload.Slice(1, 16));
        var hubIdGuid = ReadCanonicalGuid(payload.Slice(17, 16));
        if (requestId != expectedRequestId) return false;
        if (!Guid.TryParseExact(expectedHubId.Value, "N", out var expectedHubGuid) || hubIdGuid != expectedHubGuid) return false;
        result = payload[33] switch
        {
            0 => InfraredHubTransmitResult.Accepted(),
            1 => InfraredHubTransmitResult.HubUnavailable(),
            2 => InfraredHubTransmitResult.FrequencyUnsupported(),
            3 => InfraredHubTransmitResult.InvalidSignal(),
            4 => InfraredHubTransmitResult.FailedBeforeSend(),
            _ => InfraredHubTransmitResult.Unknown()
        };
        return payload[33] <= 4;
    }

    internal static byte[] EncodeLearnRequest(InfraredHubId hubId, Guid requestId)
    {
        if (!Guid.TryParseExact(hubId.Value, "N", out var hubGuid) || hubGuid == Guid.Empty)
            throw new ArgumentException("BLE hub identifiers must be canonical UUID values.", nameof(hubId));
        if (requestId == Guid.Empty) throw new ArgumentException("Request id must not be empty.", nameof(requestId));
        var payload = new byte[33];
        payload[0] = ApiVersion;
        WriteCanonicalGuid(requestId, payload.AsSpan(1, 16));
        WriteCanonicalGuid(hubGuid, payload.AsSpan(17, 16));
        return payload;
    }

    internal static bool TryParseLearnResult(
        ReadOnlySpan<byte> payload,
        InfraredHubId expectedHubId,
        Guid expectedRequestId,
        out InfraredHubLearnResult result)
    {
        result = InfraredHubLearnResult.Failed();
        if (payload.Length < 34 || payload.Length > MaxLearnPayloadBytes || payload[0] != ApiVersion) return false;
        var requestId = ReadCanonicalGuid(payload.Slice(1, 16));
        var hubIdGuid = ReadCanonicalGuid(payload.Slice(17, 16));
        if (requestId != expectedRequestId) return false;
        if (!Guid.TryParseExact(expectedHubId.Value, "N", out var expectedHubGuid) || hubIdGuid != expectedHubGuid) return false;

        var outcome = payload[33];
        if (outcome != 0)
        {
            if (payload.Length != 34) return false;
            result = outcome switch
            {
                1 => InfraredHubLearnResult.Timeout(),
                2 => InfraredHubLearnResult.Unsupported(),
                3 => InfraredHubLearnResult.InvalidCapture(),
                4 => InfraredHubLearnResult.Failed(),
                _ => InfraredHubLearnResult.Failed()
            };
            return outcome is >= 1 and <= 4;
        }

        if (payload.Length < 40) return false;
        var frequency = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(34, 4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(38, 2));
        if (count is 0 || count > InfraredSignalLimits.MaxPatternValues) return false;
        var expectedLength = 40 + checked(count * 4);
        if (payload.Length != expectedLength) return false;
        var pattern = new int[count];
        var offset = 40;
        for (var i = 0; i < count; i++)
        {
            pattern[i] = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, 4));
            offset += 4;
        }
        try
        {
            result = InfraredHubLearnResult.Captured(new InfraredSignal(frequency, pattern));
            return true;
        }
        catch (ArgumentException)
        {
            result = InfraredHubLearnResult.InvalidCapture();
            return true;
        }
    }

    /// <summary>Splits one logical BLE request into write-with-response characteristic values for a negotiated ATT MTU.</summary>
    public static IReadOnlyList<byte[]> BuildGattFrames(ReadOnlyMemory<byte> request, int negotiatedMtu)
    {
        if (request.Length is <= 0 or > MaxTransmitPayloadBytes) throw new ArgumentOutOfRangeException(nameof(request));
        if (negotiatedMtu < DefaultAttMtu) negotiatedMtu = DefaultAttMtu;
        var valueBytes = negotiatedMtu - 3; // ATT opcode/handle overhead
        var chunkBytes = valueBytes - GattFrameHeaderBytes;
        if (chunkBytes <= 0) throw new ArgumentOutOfRangeException(nameof(negotiatedMtu));
        var count = checked((ushort)((request.Length + chunkBytes - 1) / chunkBytes));
        var frames = new byte[count][];
        var source = request.Span;
        for (ushort index = 0; index < count; index++)
        {
            var start = index * chunkBytes;
            var length = Math.Min(chunkBytes, source.Length - start);
            var frame = new byte[GattFrameHeaderBytes + length];
            frame[0] = GattFrameVersion;
            frame[1] = (byte)((index == 0 ? 1 : 0) | (index == count - 1 ? 2 : 0));
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2, 2), index);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), count);
            source.Slice(start, length).CopyTo(frame.AsSpan(GattFrameHeaderBytes));
            frames[index] = frame;
        }
        return frames;
    }

    private static void WriteCanonicalGuid(Guid guid, Span<byte> destination)
        => Convert.FromHexString(guid.ToString("N")).CopyTo(destination);

    private static Guid ReadCanonicalGuid(ReadOnlySpan<byte> bytes)
        => Guid.ParseExact(Convert.ToHexString(bytes), "N");

    private static bool TryString(JsonElement element, string name, int maxLength, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length is > 0 && value.Length <= maxLength;
    }

    private static string? TryOptionalString(JsonElement element, string name, int maxLength)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (property.ValueKind != JsonValueKind.String) return null;
        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) || value.Length > maxLength ? null : value;
    }

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = default;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }
}
