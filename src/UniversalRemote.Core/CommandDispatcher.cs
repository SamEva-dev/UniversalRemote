using UniversalRemote.Abstractions;

namespace UniversalRemote.Core;

/// <summary>One provider attempt per invocation. No retry/fallback after an ambiguous transport outcome.</summary>
public sealed class CommandDispatcher : IRemoteControl
{
    private readonly IDeviceRepository devices;
    private readonly ProviderResolver resolver;
    private readonly TimeSpan timeout;
    public CommandDispatcher(IDeviceRepository devices, ProviderResolver resolver, CommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(resolver);
        this.devices = devices;
        this.resolver = resolver;
        ArgumentNullException.ThrowIfNull(options);
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(options));
        timeout = options.Timeout;
    }
    public async Task<RemoteResult> ExecuteAsync(Guid deviceId, RemoteAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        var device = await devices.FindAsync(deviceId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (device is null) return RemoteResult.Failed(RemoteErrorCode.DeviceNotFound);
        if (!device.Capabilities.Contains(action)) return RemoteResult.Failed(RemoteErrorCode.UnsupportedAction);
        var selected = resolver.Resolve(device, action);
        if (selected is null) return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Cooperative timeout: do not abandon a provider task which might still send later.
            var result = await selected.Value.Provider.ExecuteAsync(selected.Value.Route, action, deadline.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested)
                return RemoteResult.Failed(RemoteErrorCode.Timeout, DeliveryState.Unknown);
            return result ?? RemoteResult.Failed(RemoteErrorCode.ProviderFailure, DeliveryState.Unknown);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return RemoteResult.Failed(RemoteErrorCode.Timeout, DeliveryState.Unknown);
        }
        catch (TimeoutException)
        {
            return RemoteResult.Failed(RemoteErrorCode.Timeout, DeliveryState.Unknown);
        }
        catch (HttpRequestException)
        {
            return RemoteResult.Failed(RemoteErrorCode.TransportFailure, DeliveryState.Unknown);
        }
        catch (Exception)
        {
            return RemoteResult.Failed(RemoteErrorCode.ProviderFailure, DeliveryState.Unknown);
        }
    }
}

public sealed class CommandOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
