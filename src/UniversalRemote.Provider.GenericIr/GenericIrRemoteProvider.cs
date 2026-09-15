using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.GenericIr;

/// <summary>Routes normalized actions to an IR profile and a platform transmitter.</summary>
public sealed class GenericIrRemoteProvider(IInfraredTransmitter transmitter, IIrProfileCatalog catalog) : IRemoteProvider
{
    public const string ProviderId = "generic-ir";
    public string Id => ProviderId;
    public SupportLevel SupportLevel => SupportLevel.Experimental;

    public async Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(route.ProviderId, Id, StringComparison.Ordinal))
            return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);
        if (!route.Capabilities.Contains(action))
            return RemoteResult.Failed(RemoteErrorCode.UnsupportedAction);
        if (!catalog.TryGet(route.DeviceKey, out var profile))
            return RemoteResult.Failed(RemoteErrorCode.PairingRequired);
        if (!profile.TryGetCommand(action, out var command))
            return RemoteResult.Failed(RemoteErrorCode.UnsupportedAction);

        InfraredEmitterInfo emitter;
        try
        {
            emitter = await transmitter.GetInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return RemoteResult.Failed(RemoteErrorCode.ProviderFailure);
        }

        if (!emitter.HasEmitter || !emitter.SupportsFrequency(profile.CarrierFrequencyHz))
            return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);

        try
        {
            var result = await transmitter.TransmitAsync(
                profile.CarrierFrequencyHz,
                command.PatternMicroseconds,
                cancellationToken).ConfigureAwait(false);

            return result.Outcome switch
            {
                InfraredTransmitOutcome.Accepted => RemoteResult.Accepted(),
                InfraredTransmitOutcome.EmitterUnavailable => RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable),
                InfraredTransmitOutcome.FrequencyUnsupported => RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable),
                InfraredTransmitOutcome.InvalidSignal => RemoteResult.Failed(RemoteErrorCode.ProviderFailure),
                InfraredTransmitOutcome.FailedBeforeSend => RemoteResult.Failed(RemoteErrorCode.TransportFailure),
                InfraredTransmitOutcome.Unknown => RemoteResult.Failed(RemoteErrorCode.TransportFailure, DeliveryState.Unknown),
                _ => RemoteResult.Failed(RemoteErrorCode.ProviderFailure, DeliveryState.Unknown)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Once the platform transmission call has been entered, a failure can be ambiguous. Never retry here.
            return RemoteResult.Failed(RemoteErrorCode.TransportFailure, DeliveryState.Unknown);
        }
    }
}
