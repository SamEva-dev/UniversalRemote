#pragma warning disable CS0618 // Compatibility path for API 21-32 characteristic values.
using Android.App;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Java.Util;
using UniversalRemote.Hub.Ble;

namespace UniversalRemote.Platform.Android;

/// <summary>Android BLE scanner/GATT adapter. Raw device addresses never cross the IBleInfraredHubRadio boundary.</summary>
public sealed class AndroidBleInfraredHubRadio : IBleInfraredHubRadio
{
    private readonly Context _context = Application.Context;

    public async Task<BleInfraredHubRadioScanResult> ScanAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!HasScanPermission()) return new(BleInfraredHubRadioScanOutcome.PermissionDenied, [], "hub.ble.permission_scan_required");
        var adapter = GetAdapter();
        if (adapter is null || !adapter.IsEnabled) return new(BleInfraredHubRadioScanOutcome.BluetoothOff, [], "hub.ble.bluetooth_off");
        var scanner = adapter.BluetoothLeScanner;
        if (scanner is null) return new(BleInfraredHubRadioScanOutcome.Unsupported, [], "hub.ble.scanner_unavailable");

        var callback = new HubScanCallback();
        try
        {
            scanner.StartScan(callback);
            try { await Task.Delay(timeout, cancellationToken).ConfigureAwait(false); }
            finally { try { scanner.StopScan(callback); } catch (Java.Lang.SecurityException) { } }
            if (callback.Failure is not null)
                return new(BleInfraredHubRadioScanOutcome.Failed, [], $"hub.ble.scan_failed.{(int)callback.Failure.Value}");
            return new(BleInfraredHubRadioScanOutcome.Success, callback.Results, "hub.ble.scan_complete");
        }
        catch (Java.Lang.SecurityException)
        {
            return new(BleInfraredHubRadioScanOutcome.PermissionDenied, [], "hub.ble.permission_scan_required");
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new(BleInfraredHubRadioScanOutcome.Failed, [], "hub.ble.scan_failed");
        }
    }

    public async Task<BleInfraredHubRadioProbeResult> ProbeAsync(BleInfraredHubConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!HasConnectPermission()) return new(BleInfraredHubRadioProbeOutcome.PermissionDenied, default, "hub.ble.permission_connect_required");
        var adapter = GetAdapter();
        if (adapter is null || !adapter.IsEnabled) return new(BleInfraredHubRadioProbeOutcome.BluetoothOff, default, "hub.ble.bluetooth_off");
        try
        {
            var device = adapter.GetRemoteDevice(connection.DeviceKey);
            await using var session = await AndroidBleGattSession.ConnectAsync(_context, device, cancellationToken).ConfigureAwait(false);
            if (session is null) return new(BleInfraredHubRadioProbeOutcome.Unreachable, default, "hub.ble.unreachable");
            var payload = await session.ReadAsync(BleInfraredHubProtocol.InfoCharacteristicUuid, cancellationToken).ConfigureAwait(false);
            if (payload is null || payload.Length is <= 0 or > BleInfraredHubProtocol.MaxInfoBytes)
                return new(BleInfraredHubRadioProbeOutcome.InvalidResponse, default, "hub.ble.info_invalid");
            return new(BleInfraredHubRadioProbeOutcome.Success, payload, "hub.ble.probe_ok");
        }
        catch (Java.Lang.SecurityException)
        {
            return new(BleInfraredHubRadioProbeOutcome.PermissionDenied, default, "hub.ble.permission_connect_required");
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new(BleInfraredHubRadioProbeOutcome.Unreachable, default, "hub.ble.unreachable");
        }
    }

    public async Task<BleInfraredHubRadioTransmitResult> TransmitAsync(BleInfraredHubConnection connection, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (request.IsEmpty || request.Length > BleInfraredHubProtocol.MaxTransmitPayloadBytes)
            return new(BleInfraredHubRadioTransmitOutcome.FailedBeforeSend);
        if (!HasConnectPermission()) return new(BleInfraredHubRadioTransmitOutcome.HubUnavailable);
        var adapter = GetAdapter();
        if (adapter is null || !adapter.IsEnabled) return new(BleInfraredHubRadioTransmitOutcome.HubUnavailable);
        var started = false;
        try
        {
            var device = adapter.GetRemoteDevice(connection.DeviceKey);
            await using var session = await AndroidBleGattSession.ConnectAsync(_context, device, cancellationToken).ConfigureAwait(false);
            if (session is null) return new(BleInfraredHubRadioTransmitOutcome.HubUnavailable);
            var mtu = await session.TryNegotiateMtuAsync(BleInfraredHubProtocol.PreferredAttMtu, cancellationToken).ConfigureAwait(false);
            var frames = BleInfraredHubProtocol.BuildGattFrames(request, mtu);
            foreach (var frame in frames)
            {
                var write = await session.WriteAsync(BleInfraredHubProtocol.TransmitCharacteristicUuid, frame, cancellationToken).ConfigureAwait(false);
                if (!write.Initiated)
                    return new(started ? BleInfraredHubRadioTransmitOutcome.Unknown : BleInfraredHubRadioTransmitOutcome.FailedBeforeSend);
                started = true;
                if (!write.Succeeded) return new(BleInfraredHubRadioTransmitOutcome.Unknown);
            }

            var acknowledgement = await session.ReadAsync(BleInfraredHubProtocol.AcknowledgementCharacteristicUuid, cancellationToken).ConfigureAwait(false);
            return acknowledgement is { Length: > 0 }
                ? new(BleInfraredHubRadioTransmitOutcome.Acknowledged, acknowledgement)
                : new(BleInfraredHubRadioTransmitOutcome.Unknown);
        }
        catch (Java.Lang.SecurityException)
        {
            return new(started ? BleInfraredHubRadioTransmitOutcome.Unknown : BleInfraredHubRadioTransmitOutcome.HubUnavailable);
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested && started)
        {
            // Once a characteristic write has started, cancellation cannot prove non-delivery.
            return new(BleInfraredHubRadioTransmitOutcome.Unknown);
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Before the first write we know no IR request was submitted; afterwards delivery is ambiguous.
            return new(started ? BleInfraredHubRadioTransmitOutcome.Unknown : BleInfraredHubRadioTransmitOutcome.FailedBeforeSend);
        }
    }

    public async Task<BleInfraredHubRadioLearnResult> LearnAsync(
        BleInfraredHubConnection connection,
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (request.IsEmpty || request.Length > 256)
            return new(BleInfraredHubRadioLearnOutcome.Failed);
        if (!HasConnectPermission()) return new(BleInfraredHubRadioLearnOutcome.HubUnavailable);
        var adapter = GetAdapter();
        if (adapter is null || !adapter.IsEnabled) return new(BleInfraredHubRadioLearnOutcome.HubUnavailable);

        try
        {
            var device = adapter.GetRemoteDevice(connection.DeviceKey);
            await using var session = await AndroidBleGattSession.ConnectAsync(_context, device, cancellationToken).ConfigureAwait(false);
            if (session is null) return new(BleInfraredHubRadioLearnOutcome.HubUnavailable);
            var mtu = await session.TryNegotiateMtuAsync(BleInfraredHubProtocol.PreferredAttMtu, cancellationToken).ConfigureAwait(false);
            var frames = BleInfraredHubProtocol.BuildGattFrames(request, mtu);
            foreach (var frame in frames)
            {
                var write = await session.WriteAsync(BleInfraredHubProtocol.LearnCharacteristicUuid, frame, cancellationToken).ConfigureAwait(false);
                if (!write.Initiated || !write.Succeeded) return new(BleInfraredHubRadioLearnOutcome.Failed);
            }

            var response = await session.ReadAsync(
                BleInfraredHubProtocol.LearnCharacteristicUuid,
                cancellationToken,
                TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            if (response is null || response.Length is <= 0 or > BleInfraredHubProtocol.MaxLearnPayloadBytes)
                return new(BleInfraredHubRadioLearnOutcome.Failed);
            return new(BleInfraredHubRadioLearnOutcome.Result, response);
        }
        catch (Java.Lang.SecurityException) { return new(BleInfraredHubRadioLearnOutcome.HubUnavailable); }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(BleInfraredHubRadioLearnOutcome.Failed); }
    }

    private BluetoothAdapter? GetAdapter()
        => (_context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;

    private bool HasScanPermission()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            return _context.CheckSelfPermission("android.permission.BLUETOOTH_SCAN") == Permission.Granted;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            return _context.CheckSelfPermission("android.permission.ACCESS_FINE_LOCATION") == Permission.Granted;
        return true;
    }

    private bool HasConnectPermission()
        => Build.VERSION.SdkInt < BuildVersionCodes.S
           || _context.CheckSelfPermission("android.permission.BLUETOOTH_CONNECT") == Permission.Granted;

    private sealed class HubScanCallback : ScanCallback
    {
        private readonly Dictionary<string, BleInfraredHubRadioAdvertisement> _results = new(StringComparer.Ordinal);
        private static readonly ParcelUuid ServiceParcelUuid = new(UUID.FromString(BleInfraredHubProtocol.ServiceUuid.ToString()));
        public IReadOnlyList<BleInfraredHubRadioAdvertisement> Results => _results.Values.ToArray();
        public ScanFailure? Failure { get; private set; }

        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            var record = result?.ScanRecord;
            var deviceKey = result?.Device?.Address;
            if (record is null || string.IsNullOrWhiteSpace(deviceKey)) return;
            var data = record.GetServiceData(ServiceParcelUuid);
            if (data is null || !BleInfraredHubProtocol.TryParseAdvertisementServiceData(data, out var hubId, out var apiVersion)) return;
            var name = string.IsNullOrWhiteSpace(record.DeviceName) ? "Hub IR UniversalRemote" : record.DeviceName!;
            _results[deviceKey] = new BleInfraredHubRadioAdvertisement(hubId, deviceKey, name, apiVersion);
        }

        public override void OnScanFailed(ScanFailure errorCode) => Failure = errorCode;
    }

    private sealed class AndroidBleGattSession : IAsyncDisposable
    {
        private readonly BluetoothGatt _gatt;
        private readonly SessionCallback _callback;
        private readonly SemaphoreSlim _operationLock = new(1, 1);

        private AndroidBleGattSession(BluetoothGatt gatt, SessionCallback callback)
        {
            _gatt = gatt;
            _callback = callback;
        }

        public static async Task<AndroidBleGattSession?> ConnectAsync(Context context, BluetoothDevice device, CancellationToken cancellationToken)
        {
            var callback = new SessionCallback();
            var gatt = device.ConnectGatt(context, false, callback, BluetoothTransports.Le);
            if (gatt is null) return null;
            callback.Attach(gatt);
            try
            {
                var connected = await callback.WaitConnectedAsync(cancellationToken).ConfigureAwait(false);
                if (!connected || !gatt.DiscoverServices()) { gatt.Close(); return null; }
                var services = await callback.WaitServicesAsync(cancellationToken).ConfigureAwait(false);
                if (!services) { gatt.Close(); return null; }
                var service = gatt.GetService(UUID.FromString(BleInfraredHubProtocol.ServiceUuid.ToString()));
                if (service is null) { gatt.Close(); return null; }
                return new AndroidBleGattSession(gatt, callback);
            }
            catch
            {
                try { gatt.Disconnect(); } catch { }
                gatt.Close();
                throw;
            }
        }

        public async Task<int> TryNegotiateMtuAsync(int requested, CancellationToken cancellationToken)
        {
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var tcs = _callback.PrepareMtu();
                if (!_gatt.RequestMtu(requested)) return BleInfraredHubProtocol.DefaultAttMtu;
                try { return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); }
                catch (TimeoutException) { return BleInfraredHubProtocol.DefaultAttMtu; }
            }
            finally { _operationLock.Release(); }
        }

        public Task<byte[]?> ReadAsync(Guid characteristicUuid, CancellationToken cancellationToken)
            => ReadAsync(characteristicUuid, cancellationToken, TimeSpan.FromSeconds(4));

        public async Task<byte[]?> ReadAsync(Guid characteristicUuid, CancellationToken cancellationToken, TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var characteristic = FindCharacteristic(characteristicUuid);
                if (characteristic is null) return null;
                var tcs = _callback.PrepareRead(characteristicUuid);
                if (!_gatt.ReadCharacteristic(characteristic)) return null;
                try { return await tcs.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false); }
                catch (TimeoutException) { return null; }
            }
            finally { _operationLock.Release(); }
        }

        public async Task<(bool Initiated, bool Succeeded)> WriteAsync(Guid characteristicUuid, byte[] value, CancellationToken cancellationToken)
        {
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var characteristic = FindCharacteristic(characteristicUuid);
                if (characteristic is null || !characteristic.SetValue(value)) return (false, false);
                characteristic.WriteType = GattWriteType.Default;
                var tcs = _callback.PrepareWrite(characteristicUuid);
                if (!_gatt.WriteCharacteristic(characteristic)) return (false, false);
                try { return (true, await tcs.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false)); }
                catch (TimeoutException) { return (true, false); }
                catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The Android stack accepted the write call; delivery is now ambiguous.
                    return (true, false);
                }
            }
            finally { _operationLock.Release(); }
        }

        private BluetoothGattCharacteristic? FindCharacteristic(Guid characteristicUuid)
        {
            var service = _gatt.GetService(UUID.FromString(BleInfraredHubProtocol.ServiceUuid.ToString()));
            return service?.GetCharacteristic(UUID.FromString(characteristicUuid.ToString()));
        }

        public ValueTask DisposeAsync()
        {
            try { _gatt.Disconnect(); } catch { }
            _gatt.Close();
            _operationLock.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SessionCallback : BluetoothGattCallback
    {
        private readonly TaskCompletionSource<bool> _connected = NewTcs<bool>();
        private readonly TaskCompletionSource<bool> _services = NewTcs<bool>();
        private TaskCompletionSource<int>? _mtu;
        private TaskCompletionSource<byte[]?>? _read;
        private Guid _readUuid;
        private TaskCompletionSource<bool>? _write;
        private Guid _writeUuid;
        private BluetoothGatt? _gatt;

        public void Attach(BluetoothGatt gatt) => _gatt = gatt;
        public Task<bool> WaitConnectedAsync(CancellationToken token) => _connected.Task.WaitAsync(TimeSpan.FromSeconds(8), token);
        public Task<bool> WaitServicesAsync(CancellationToken token) => _services.Task.WaitAsync(TimeSpan.FromSeconds(6), token);
        public TaskCompletionSource<int> PrepareMtu() => _mtu = NewTcs<int>();
        public TaskCompletionSource<byte[]?> PrepareRead(Guid uuid) { _readUuid = uuid; return _read = NewTcs<byte[]?>(); }
        public TaskCompletionSource<bool> PrepareWrite(Guid uuid) { _writeUuid = uuid; return _write = NewTcs<bool>(); }

        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (status == GattStatus.Success && newState == ProfileState.Connected) _connected.TrySetResult(true);
            else if (newState == ProfileState.Disconnected) _connected.TrySetResult(false);
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
            => _services.TrySetResult(status == GattStatus.Success);

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
            => _mtu?.TrySetResult(status == GattStatus.Success ? mtu : BleInfraredHubProtocol.DefaultAttMtu);

        public override void OnCharacteristicRead(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
        {
            if (characteristic?.Uuid?.ToString() != _readUuid.ToString()) return;
            _read?.TrySetResult(status == GattStatus.Success ? characteristic.GetValue() : null);
        }

        public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
        {
            if (characteristic?.Uuid?.ToString() != _writeUuid.ToString()) return;
            _write?.TrySetResult(status == GattStatus.Success);
        }

        private static TaskCompletionSource<T> NewTcs<T>()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
#pragma warning restore CS0618
