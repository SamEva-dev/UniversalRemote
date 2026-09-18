namespace UniversalRemote.Remote.Abstractions;

/// <summary>A carrier-frequency range advertised by a physical infrared emitter.</summary>
public readonly record struct InfraredFrequencyRange
{
    public int MinHz { get; }
    public int MaxHz { get; }

    public InfraredFrequencyRange(int minHz, int maxHz)
    {
        if (minHz <= 0) throw new ArgumentOutOfRangeException(nameof(minHz));
        if (maxHz < minHz) throw new ArgumentOutOfRangeException(nameof(maxHz));
        MinHz = minHz;
        MaxHz = maxHz;
    }

    public bool Contains(int frequencyHz) => frequencyHz >= MinHz && frequencyHz <= MaxHz;
}

/// <summary>Sanitized diagnostic information about the platform infrared emitter.</summary>
public sealed class InfraredEmitterInfo
{
    public bool HasEmitter { get; }
    public IReadOnlyList<InfraredFrequencyRange> CarrierFrequencies { get; }
    public string DiagnosticCode { get; }

    public InfraredEmitterInfo(bool hasEmitter, IEnumerable<InfraredFrequencyRange>? carrierFrequencies, string diagnosticCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);
        HasEmitter = hasEmitter;
        CarrierFrequencies = Array.AsReadOnly((carrierFrequencies ?? []).ToArray());
        DiagnosticCode = diagnosticCode;
    }

    public bool SupportsFrequency(int frequencyHz)
        => HasEmitter && CarrierFrequencies.Any(range => range.Contains(frequencyHz));
}

public enum InfraredTransmitOutcome
{
    Accepted,
    EmitterUnavailable,
    FrequencyUnsupported,
    InvalidSignal,
    FailedBeforeSend,
    Unknown
}

/// <summary>
/// Accepted means the platform API returned after transmission; it is not proof that the appliance reacted.
/// Unknown means the caller must not retry automatically because emission may have started.
/// </summary>
public readonly record struct InfraredTransmitResult(InfraredTransmitOutcome Outcome)
{
    public static InfraredTransmitResult Accepted() => new(InfraredTransmitOutcome.Accepted);
    public static InfraredTransmitResult EmitterUnavailable() => new(InfraredTransmitOutcome.EmitterUnavailable);
    public static InfraredTransmitResult FrequencyUnsupported() => new(InfraredTransmitOutcome.FrequencyUnsupported);
    public static InfraredTransmitResult InvalidSignal() => new(InfraredTransmitOutcome.InvalidSignal);
    public static InfraredTransmitResult FailedBeforeSend() => new(InfraredTransmitOutcome.FailedBeforeSend);
    public static InfraredTransmitResult Unknown() => new(InfraredTransmitOutcome.Unknown);
}

/// <summary>Platform abstraction. Android implements this with ConsumerIrManager.</summary>
public interface IInfraredTransmitter
{
    ValueTask<InfraredEmitterInfo> GetInfoAsync(CancellationToken cancellationToken = default);
    Task<InfraredTransmitResult> TransmitAsync(
        int carrierFrequencyHz,
        IReadOnlyList<int> patternMicroseconds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker for an emitter physically embedded in the current device. It allows the product to choose a hub before
/// sending when the phone has no suitable native emitter, without ever performing a post-send fallback.
/// </summary>
public interface IOnDeviceInfraredTransmitter : IInfraredTransmitter { }

/// <summary>Shared safety limits used before invoking a platform IR API.</summary>
public static class InfraredSignalLimits
{
    public const int MaxPatternValues = 4096;
    public const int MaxTotalDurationMicroseconds = 2_000_000;
    public const int MinPlausibleCarrierFrequencyHz = 10_000;
    public const int MaxPlausibleCarrierFrequencyHz = 500_000;

    public static bool IsCarrierFrequencyPlausible(int frequencyHz)
        => frequencyHz is >= MinPlausibleCarrierFrequencyHz and <= MaxPlausibleCarrierFrequencyHz;

    public static bool IsPatternValid(IReadOnlyList<int>? patternMicroseconds)
    {
        if (patternMicroseconds is null || patternMicroseconds.Count == 0 || patternMicroseconds.Count > MaxPatternValues)
            return false;

        long total = 0;
        foreach (var duration in patternMicroseconds)
        {
            if (duration <= 0) return false;
            total += duration;
            if (total >= MaxTotalDurationMicroseconds) return false;
        }

        return true;
    }
}
