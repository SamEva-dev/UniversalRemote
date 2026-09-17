using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Hub;

/// <summary>
/// Adapts an external hub to the existing emitter contract, allowing Generic IR profiles to be reused unchanged.
/// The adapter never retries a transmission whose delivery may be ambiguous.
/// </summary>
public sealed class HubInfraredTransmitter(IInfraredHub hub) : IInfraredTransmitter
{
    public async ValueTask<InfraredEmitterInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hub);
        try
        {
            var info = await hub.GetInfoAsync(cancellationToken).ConfigureAwait(false);
            var ready = info.State == InfraredHubConnectionState.Ready && info.Capabilities.CanTransmit;
            var diagnostic = ready ? "ir.hub.ready" : info.State switch
            {
                InfraredHubConnectionState.Offline => "ir.hub.offline",
                InfraredHubConnectionState.Busy => "ir.hub.busy",
                InfraredHubConnectionState.Faulted => "ir.hub.faulted",
                _ => "ir.hub.unavailable"
            };

            return new InfraredEmitterInfo(
                ready,
                info.Capabilities.CarrierFrequencies,
                diagnostic);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new InfraredEmitterInfo(false, [], "ir.hub.info_failed");
        }
    }

    public async Task<InfraredTransmitResult> TransmitAsync(
        int carrierFrequencyHz,
        IReadOnlyList<int> patternMicroseconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hub);
        if (!InfraredSignalLimits.IsCarrierFrequencyPlausible(carrierFrequencyHz)
            || !InfraredSignalLimits.IsPatternValid(patternMicroseconds))
            return InfraredTransmitResult.InvalidSignal();

        InfraredSignal signal;
        try
        {
            signal = new InfraredSignal(carrierFrequencyHz, patternMicroseconds);
        }
        catch (ArgumentException)
        {
            return InfraredTransmitResult.InvalidSignal();
        }

        InfraredHubInfo info;
        try
        {
            info = await hub.GetInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return InfraredTransmitResult.EmitterUnavailable();
        }

        if (info.State != InfraredHubConnectionState.Ready || !info.Capabilities.CanTransmit)
            return InfraredTransmitResult.EmitterUnavailable();
        if (!info.Capabilities.SupportsFrequency(carrierFrequencyHz))
            return InfraredTransmitResult.FrequencyUnsupported();
        if (!info.Capabilities.Accepts(signal))
            return InfraredTransmitResult.InvalidSignal();

        try
        {
            var result = await hub.TransmitAsync(signal, cancellationToken).ConfigureAwait(false);
            return result.Outcome switch
            {
                InfraredHubTransmitOutcome.Accepted => InfraredTransmitResult.Accepted(),
                InfraredHubTransmitOutcome.HubUnavailable => InfraredTransmitResult.EmitterUnavailable(),
                InfraredHubTransmitOutcome.FrequencyUnsupported => InfraredTransmitResult.FrequencyUnsupported(),
                InfraredHubTransmitOutcome.InvalidSignal => InfraredTransmitResult.InvalidSignal(),
                InfraredHubTransmitOutcome.FailedBeforeSend => InfraredTransmitResult.FailedBeforeSend(),
                InfraredHubTransmitOutcome.Unknown => InfraredTransmitResult.Unknown(),
                _ => InfraredTransmitResult.Unknown()
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Preserve the existing IInfraredTransmitter cancellation contract. Higher layers treat
            // an in-flight cancelled command conservatively because emission may already have started.
            throw;
        }
        catch (Exception)
        {
            // The transport may have delivered the request before failing. Never retry automatically.
            return InfraredTransmitResult.Unknown();
        }
    }
}
