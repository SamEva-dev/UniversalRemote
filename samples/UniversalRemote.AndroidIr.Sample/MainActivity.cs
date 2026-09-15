using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using DomainRelay.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;
using UniversalRemote.Application;
using UniversalRemote.Core;
using UniversalRemote.Platform.Android;
using UniversalRemote.Provider.GenericIr;
using UniversalRemote.Provider.Simulator;

namespace UniversalRemote.AndroidIr.Sample;

[Activity(Label = "UniversalRemote IR", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private static readonly Guid IrDeviceId = Guid.Parse("7cb5a207-7ad9-4c99-8832-d503f17f5001");
    private static readonly Guid SimulatorDeviceId = Guid.Parse("7cb5a207-7ad9-4c99-8832-d503f17f5002");
    private ServiceProvider? services;
    private TextView? status;
    private Button? power;
    private Button? volumeUp;
    private Button? volumeDown;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var bench = BuiltInIrProfiles.BenchSyntheticTv;
        var irDevice = new Device(IrDeviceId, "Banc IR synthétique — non vérifié",
            [new DeviceRoute(GenericIrRemoteProvider.ProviderId, bench.Id, bench.Capabilities)]);
        var simulatorDevice = new Device(SimulatorDeviceId, "Simulation DomainRelay",
            [new DeviceRoute(SimulatorProvider.ProviderId, "ir-sample-simulator", [RemoteActions.VolumeUp])]);
        var repository = new InMemoryDeviceRepository([irDevice, simulatorDevice]);

        var collection = new ServiceCollection();
        collection.AddSingleton(repository);
        collection.AddSingleton<IDeviceRepository>(repository);
        collection.AddSingleton<IDeviceRegistrar>(repository);
        collection.AddUniversalRemoteApplication();
        collection.AddSingleton<IRemoteProvider, SimulatorProvider>();
        collection.AddUniversalRemoteAndroidInfrared();
        collection.AddUniversalRemoteGenericIr();
        services = collection.BuildServiceProvider();

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        root.SetPadding(32, 32, 32, 32);

        root.AddView(new TextView(this)
        {
            Text = "UniversalRemote — diagnostic infrarouge",
            TextSize = 22
        });
        root.AddView(new TextView(this)
        {
            Text = "Le profil fourni est synthétique et NON vérifié sur un téléviseur réel. « Acceptée » signifie seulement que l'API Android a terminé l'émission.",
            TextSize = 14
        });

        status = new TextView(this) { Text = "Diagnostic en cours…", TextSize = 14 };
        root.AddView(status);

        var dryRun = AddButton(root, "Tester DomainRelay — simulation", async () =>
            await ExecuteAsync(SimulatorDeviceId, RemoteActions.VolumeUp));
        dryRun.ContentDescription = "Test du pipeline DomainRelay sans émission infrarouge";

        power = AddButton(root, "Power — profil de banc", async () => await ExecuteAsync(IrDeviceId, RemoteActions.PowerToggle));
        volumeUp = AddButton(root, "Volume + — profil de banc", async () => await ExecuteAsync(IrDeviceId, RemoteActions.VolumeUp));
        volumeDown = AddButton(root, "Volume - — profil de banc", async () => await ExecuteAsync(IrDeviceId, RemoteActions.VolumeDown));

        SetContentView(root);
        _ = RefreshDiagnosticsAsync();
    }

    protected override void OnDestroy()
    {
        services?.Dispose();
        services = null;
        base.OnDestroy();
    }

    private Button AddButton(LinearLayout root, string text, Func<Task> action)
    {
        var button = new Button(this) { Text = text };
        button.SetMinHeight(96);
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try { await action(); }
            catch (Android.Accounts.OperationCanceledException) { SetStatus("Commande annulée."); }
            catch (Exception) { SetStatus("Échec maîtrisé du sample. Aucun détail technique sensible n'est affiché."); }
            finally { button.Enabled = true; }
        };
        root.AddView(button, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return button;
    }

    private async Task RefreshDiagnosticsAsync()
    {
        if (services is null) return;
        var transmitter = services.GetRequiredService<IInfraredTransmitter>();
        var info = await transmitter.GetInfoAsync();
        var frequencies = info.CarrierFrequencies.Count == 0
            ? "fréquences indisponibles"
            : string.Join(", ", info.CarrierFrequencies.Select(range => $"{range.MinHz / 1000d:0.#}-{range.MaxHz / 1000d:0.#} kHz"));
        SetStatus(info.HasEmitter
            ? $"Émetteur IR détecté — {frequencies} — diagnostic {info.DiagnosticCode}."
            : $"Aucun émetteur IR Android disponible — diagnostic {info.DiagnosticCode}.");
        var canTransmit = info.HasEmitter && info.SupportsFrequency(BuiltInIrProfiles.BenchSyntheticTv.CarrierFrequencyHz);
        if (power is not null) power.Enabled = canTransmit;
        if (volumeUp is not null) volumeUp.Enabled = canTransmit;
        if (volumeDown is not null) volumeDown.Enabled = canTransmit;
    }

    private async Task ExecuteAsync(Guid deviceId, Abstractions.RemoteAction action)
    {
        if (services is null) return;
        var mediator = services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new ExecuteRemoteAction(deviceId, action));
        SetStatus(result.IsSuccess
            ? $"{action.Id} : émission acceptée ({result.Delivery}). Réaction physique non confirmée."
            : $"{action.Id} : {result.Error} ({result.Delivery}).");
    }

    private void SetStatus(string value)
    {
        if (status is null) return;
        RunOnUiThread(() => status.Text = value);
    }
}
