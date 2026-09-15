using UniversalRemote.Abstractions;

namespace UniversalRemote.Telemetry;

public sealed record TelemetryOptions(int MaxEvents = 250, TimeSpan? MaxAge = null)
{
    public TimeSpan EffectiveMaxAge => MaxAge ?? TimeSpan.FromDays(7);

    public void Validate()
    {
        if (MaxEvents is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(MaxEvents));
        if (EffectiveMaxAge <= TimeSpan.Zero || EffectiveMaxAge > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(MaxAge));
    }
}

public interface ITelemetryConsentStore
{
    bool IsEnabled { get; set; }
}

public interface ITelemetryEventStore
{
    ValueTask AppendAsync(TelemetryRecord record, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<TelemetryRecord>> ReadAsync(CancellationToken cancellationToken = default);
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

public interface ITelemetryExporter
{
    ValueTask ExportAsync(string destinationPath, string productVersion, CancellationToken cancellationToken = default);
}

public sealed class DisabledTelemetryConsentStore : ITelemetryConsentStore
{
    public bool IsEnabled { get => false; set { } }
}

public sealed class InMemoryTelemetryConsentStore(bool enabled = false) : ITelemetryConsentStore
{
    public bool IsEnabled { get; set; } = enabled;
}
