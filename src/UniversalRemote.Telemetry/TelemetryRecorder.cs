using UniversalRemote.Abstractions;

namespace UniversalRemote.Telemetry;

/// <summary>
/// Opt-in local recorder. Diagnostics are never uploaded by this component and recorder failures are deliberately swallowed.
/// </summary>
public sealed class TelemetryRecorder(ITelemetryConsentStore consent, ITelemetryEventStore store) : ITelemetryRecorder
{
    public async ValueTask RecordAsync(TelemetryRecord record, CancellationToken cancellationToken = default)
    {
        if (!consent.IsEnabled) return;
        try
        {
            await store.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
    }
}
