namespace UniversalRemote.Remote.Abstractions;

/// <summary>Coarse outcome used by privacy-preserving product diagnostics.</summary>
public enum TelemetryOutcome
{
    Success,
    Failed,
    Cancelled,
    Unknown
}

/// <summary>Coarse duration bucket. Exact timings are intentionally not persisted.</summary>
public enum TelemetryDurationBucket
{
    Under50Milliseconds,
    Under250Milliseconds,
    Under1Second,
    Under5Seconds,
    FiveSecondsOrMore
}

/// <summary>
/// Sanitized telemetry event. It deliberately has no arbitrary tag bag, user/device identifier,
/// endpoint, payload or exception-message field.
/// </summary>
public sealed record TelemetryRecord(
    DateTimeOffset TimestampUtc,
    string EventName,
    string Component,
    TelemetryOutcome Outcome,
    TelemetryDurationBucket DurationBucket)
{
    public static TelemetryRecord Create(
        string eventName,
        string component,
        TelemetryOutcome outcome,
        TimeSpan elapsed,
        DateTimeOffset? timestampUtc = null)
    {
        ValidateToken(eventName, nameof(eventName));
        ValidateToken(component, nameof(component));
        return new TelemetryRecord(
            timestampUtc ?? DateTimeOffset.UtcNow,
            eventName,
            component,
            outcome,
            ToBucket(elapsed));
    }

    private static void ValidateToken(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96)
            throw new ArgumentException("Telemetry tokens must contain 1 to 96 safe characters.", paramName);

        foreach (var ch in value)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-'))
                throw new ArgumentException("Telemetry tokens may contain only ASCII letters, digits, '.', '_' or '-'.", paramName);
        }
    }

    private static TelemetryDurationBucket ToBucket(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed < TimeSpan.FromMilliseconds(50)) return TelemetryDurationBucket.Under50Milliseconds;
        if (elapsed < TimeSpan.FromMilliseconds(250)) return TelemetryDurationBucket.Under250Milliseconds;
        if (elapsed < TimeSpan.FromSeconds(1)) return TelemetryDurationBucket.Under1Second;
        if (elapsed < TimeSpan.FromSeconds(5)) return TelemetryDurationBucket.Under5Seconds;
        return TelemetryDurationBucket.FiveSecondsOrMore;
    }
}

/// <summary>Receives only sanitized events. Implementations must not affect command execution.</summary>
public interface ITelemetryRecorder
{
    ValueTask RecordAsync(TelemetryRecord record, CancellationToken cancellationToken = default);
}

/// <summary>Default recorder used when telemetry is not configured.</summary>
public sealed class NullTelemetryRecorder : ITelemetryRecorder
{
    public static NullTelemetryRecorder Instance { get; } = new();
    private NullTelemetryRecorder() { }
    public ValueTask RecordAsync(TelemetryRecord record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
