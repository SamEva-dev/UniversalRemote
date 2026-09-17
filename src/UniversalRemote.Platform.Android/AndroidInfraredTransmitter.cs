using Android.App;
using Android.Content;
using Android.Hardware;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Platform.Android;

/// <summary>ConsumerIrManager adapter. Emission is serialized process-wide and never retried.</summary>
public sealed class AndroidInfraredTransmitter : IOnDeviceInfraredTransmitter
{
    private static readonly SemaphoreSlim TransmissionGate = new(1, 1);

    public ValueTask<InfraredEmitterInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manager = GetManager();
        if (manager is null || !manager.HasIrEmitter)
            return ValueTask.FromResult(new InfraredEmitterInfo(false, [], "ir.emitter.unavailable"));

        try
        {
            var ranges = manager.GetCarrierFrequencies();
            if (ranges is null || ranges.Length == 0)
                return ValueTask.FromResult(new InfraredEmitterInfo(true, [], "ir.frequency.unavailable"));

            var normalized = ranges
                .Select(range => new InfraredFrequencyRange(range.MinFrequency, range.MaxFrequency))
                .ToArray();
            return ValueTask.FromResult(new InfraredEmitterInfo(true, normalized, "ir.ready"));
        }
        catch (Exception)
        {
            return ValueTask.FromResult(new InfraredEmitterInfo(true, [], "ir.frequency.query_failed"));
        }
    }

    public async Task<InfraredTransmitResult> TransmitAsync(
        int carrierFrequencyHz,
        IReadOnlyList<int> patternMicroseconds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!InfraredSignalLimits.IsCarrierFrequencyPlausible(carrierFrequencyHz) ||
            !InfraredSignalLimits.IsPatternValid(patternMicroseconds))
            return InfraredTransmitResult.InvalidSignal();

        var manager = GetManager();
        if (manager is null || !manager.HasIrEmitter)
            return InfraredTransmitResult.EmitterUnavailable();

        var info = await GetInfoAsync(cancellationToken).ConfigureAwait(false);
        if (!info.SupportsFrequency(carrierFrequencyHz))
            return InfraredTransmitResult.FrequencyUnsupported();

        await TransmissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pattern = patternMicroseconds.ToArray();
            try
            {
                // ConsumerIrManager.Transmit is synchronous. Once started, cancellation cannot retract the emission.
                await Task.Run(() => manager.Transmit(carrierFrequencyHz, pattern), CancellationToken.None).ConfigureAwait(false);
                return InfraredTransmitResult.Accepted();
            }
            catch (Java.Lang.IllegalArgumentException)
            {
                return InfraredTransmitResult.InvalidSignal();
            }
            catch (Java.Lang.SecurityException)
            {
                return InfraredTransmitResult.FailedBeforeSend();
            }
            catch (Exception)
            {
                return InfraredTransmitResult.Unknown();
            }
        }
        finally
        {
            TransmissionGate.Release();
        }
    }

    private static ConsumerIrManager? GetManager()
        => Application.Context.GetSystemService(Context.ConsumerIrService) as ConsumerIrManager;
}
