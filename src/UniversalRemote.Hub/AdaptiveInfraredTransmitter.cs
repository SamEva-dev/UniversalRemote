using UniversalRemote.Abstractions;

namespace UniversalRemote.Hub;

/// <summary>
/// Chooses the emitter before sending. Native IR is preferred when it supports the requested frequency; otherwise
/// the selected external hub may be used. There is never an automatic fallback after a transmit attempt starts.
/// </summary>
public sealed class AdaptiveInfraredTransmitter(
    IEnumerable<IOnDeviceInfraredTransmitter> onDeviceTransmitters,
    IInfraredHubSelectionAccessor selectedHubAccessor) : IInfraredTransmitter
{
    private readonly IReadOnlyList<IOnDeviceInfraredTransmitter> _onDevice = onDeviceTransmitters?.ToArray()
        ?? throw new ArgumentNullException(nameof(onDeviceTransmitters));

    public async ValueTask<InfraredEmitterInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var ranges = new HashSet<InfraredFrequencyRange>();
        var nativeReady = false;
        foreach (var transmitter in _onDevice)
        {
            try
            {
                var info = await transmitter.GetInfoAsync(cancellationToken).ConfigureAwait(false);
                if (!info.HasEmitter) continue;
                nativeReady = true;
                foreach (var range in info.CarrierFrequencies) ranges.Add(range);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { }
        }

        var hubReady = false;
        try
        {
            var hub = await selectedHubAccessor.GetSelectedHubAsync(cancellationToken).ConfigureAwait(false);
            if (hub is not null)
            {
                var info = await hub.GetInfoAsync(cancellationToken).ConfigureAwait(false);
                if (info.State == InfraredHubConnectionState.Ready && info.Capabilities.CanTransmit)
                {
                    hubReady = true;
                    foreach (var range in info.Capabilities.CarrierFrequencies) ranges.Add(range);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { }

        var diagnostic = (nativeReady, hubReady) switch
        {
            (true, true) => "ir.adaptive.native_and_hub_ready",
            (true, false) => "ir.adaptive.native_ready",
            (false, true) => "ir.adaptive.hub_ready",
            _ => "ir.adaptive.unavailable"
        };
        return new InfraredEmitterInfo(nativeReady || hubReady, ranges.OrderBy(x => x.MinHz).ThenBy(x => x.MaxHz), diagnostic);
    }

    public async Task<InfraredTransmitResult> TransmitAsync(
        int carrierFrequencyHz,
        IReadOnlyList<int> patternMicroseconds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!InfraredSignalLimits.IsCarrierFrequencyPlausible(carrierFrequencyHz)
            || !InfraredSignalLimits.IsPatternValid(patternMicroseconds))
            return InfraredTransmitResult.InvalidSignal();

        var sawEmitter = false;
        foreach (var transmitter in _onDevice)
        {
            InfraredEmitterInfo info;
            try { info = await transmitter.GetInfoAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { continue; }

            if (!info.HasEmitter) continue;
            sawEmitter = true;
            if (!info.SupportsFrequency(carrierFrequencyHz)) continue;

            try
            {
                // Once native emission starts, its result is final for this command. Never fail over afterwards.
                return await transmitter.TransmitAsync(carrierFrequencyHz, patternMicroseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return InfraredTransmitResult.Unknown(); }
        }

        IInfraredHub? hub;
        try { hub = await selectedHubAccessor.GetSelectedHubAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return sawEmitter ? InfraredTransmitResult.FrequencyUnsupported() : InfraredTransmitResult.EmitterUnavailable(); }

        if (hub is null)
            return sawEmitter ? InfraredTransmitResult.FrequencyUnsupported() : InfraredTransmitResult.EmitterUnavailable();

        InfraredHubInfo hubInfo;
        try { hubInfo = await hub.GetInfoAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return sawEmitter ? InfraredTransmitResult.FrequencyUnsupported() : InfraredTransmitResult.EmitterUnavailable(); }

        if (hubInfo.State != InfraredHubConnectionState.Ready || !hubInfo.Capabilities.CanTransmit)
            return sawEmitter ? InfraredTransmitResult.FrequencyUnsupported() : InfraredTransmitResult.EmitterUnavailable();
        if (!hubInfo.Capabilities.SupportsFrequency(carrierFrequencyHz))
            return InfraredTransmitResult.FrequencyUnsupported();

        InfraredSignal signal;
        try { signal = new InfraredSignal(carrierFrequencyHz, patternMicroseconds); }
        catch (ArgumentException) { return InfraredTransmitResult.InvalidSignal(); }
        if (!hubInfo.Capabilities.Accepts(signal)) return InfraredTransmitResult.InvalidSignal();

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
                _ => InfraredTransmitResult.Unknown()
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return InfraredTransmitResult.Unknown(); }
    }
}
