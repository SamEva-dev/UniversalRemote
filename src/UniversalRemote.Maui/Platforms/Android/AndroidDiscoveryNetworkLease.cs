using Android.Content;
using Android.Net.Wifi;
using UniversalRemote.Remote.Discovery;

namespace UniversalRemote.Maui;

public sealed class AndroidDiscoveryNetworkLease : IDiscoveryNetworkLease
{
    public ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var wifi = (WifiManager?)global::Android.App.Application.Context.GetSystemService(Context.WifiService)
            ?? throw new InvalidOperationException("Wi-Fi service is unavailable.");
        var multicastLock = wifi.CreateMulticastLock("UniversalRemote.Discovery")
            ?? throw new InvalidOperationException("Multicast lock is unavailable.");
        multicastLock.Acquire();
        return ValueTask.FromResult<IAsyncDisposable>(new Lease(multicastLock));
    }

    private sealed class Lease(WifiManager.MulticastLock multicastLock) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                multicastLock.Release();
                multicastLock.Dispose();
            }
            return ValueTask.CompletedTask;
        }
    }
}
