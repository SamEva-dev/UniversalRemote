using DomainRelay.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UniversalRemote.Abstractions;
using UniversalRemote.Application;
using UniversalRemote.Presentation;

namespace UniversalRemote.Maui.Remote;

public sealed class RemoteViewModel(IMediator mediator, DeviceSelectionState selection, IDeviceRepository devices) : INotifyPropertyChanged
{
    private RemoteUiModel? model;
    private string layoutId = "classic";
    private string status = RemoteLabels.Text("Sélectionnez un appareil.", "Select a device.");
    private bool isBusy;
    private CancellationTokenSource? activityRun;
    public IReadOnlyList<RemoteDeviceTile> Devices { get; private set; } = Array.Empty<RemoteDeviceTile>();
    public IReadOnlyList<RemoteActivityTile> Activities { get; private set; } = Array.Empty<RemoteActivityTile>();
    public bool IsActivityRunning => activityRun is not null;
    public void CancelActivity() => activityRun?.Cancel();
    public event PropertyChangedEventHandler? PropertyChanged;
    public RemoteUiModel? Model { get => model; private set => Set(ref model, value); }
    public string LayoutId { get => layoutId; set => Set(ref layoutId, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }
    public bool IsDemo => Model?.DeviceId == MauiProgram.DemoDeviceId;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        Model = null;
        try
        {
            var deviceId = selection.ActiveDeviceId ?? MauiProgram.DemoDeviceId;
            var snapshot = await mediator.Send(new GetRemoteUiModel(deviceId), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Model = snapshot;
            await LoadDashboardAsync(cancellationToken);
            Status = Model is null ? RemoteLabels.Text("Appareil indisponible.", "Device unavailable.") : IsDemo
                ? RemoteLabels.Text("Démonstration — aucune commande réelle.", "Demo — no real commands.")
                : RemoteLabels.Text("Prêt à envoyer une commande.", "Ready to send a command.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { Status = RemoteLabels.Text("Impossible de charger cet appareil. Réessayez depuis la liste.", "Unable to load this device. Try again from the device list."); }
        finally { IsBusy = false; }
    }

    private async Task LoadDashboardAsync(CancellationToken ct)
    {
        Devices = Array.Empty<RemoteDeviceTile>();
        Activities = Array.Empty<RemoteActivityTile>();
        try
        {
            var all = await devices.ListAsync(ct);
            var rooms = await mediator.Send(new ListRooms(), ct);
            var activities = await mediator.Send(new ListActivities(), ct);
            Devices = all.Select(d => new RemoteDeviceTile(d.Id, d.DisplayName,
                rooms.FirstOrDefault(room => room.Devices.Any(item => item.Id == d.Id))?.Name ?? string.Empty,
                d.Capabilities.Any(a => a.Id.StartsWith("climate.", StringComparison.Ordinal)) ? "climate" :
                d.Capabilities.Any(a => a.Id.StartsWith("light.", StringComparison.Ordinal)) ? "light" :
                d.Capabilities.Any(a => a.Id.StartsWith("navigation.", StringComparison.Ordinal) || a.Id.StartsWith("channel.", StringComparison.Ordinal)) ? "tv" : "audio")).ToArray();
            Activities = activities.Where(a => a.Steps.Count > 0)
                .Select(a => new RemoteActivityTile(a.Id, a.Name, a.Steps.Count)).ToArray();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { /* Optional dashboard data must not disable a loaded remote. */ }
    }

    public async Task SelectDeviceAsync(Guid id, CancellationToken ct)
    {
        if (IsBusy || !Devices.Any(d => d.Id == id)) return;
        selection.ActiveDeviceId = id;
        await InitializeAsync(ct);
    }

    public async Task RunActivityAsync(Guid id, CancellationToken ct)
    {
        if (IsBusy || !Activities.Any(a => a.Id == id)) return;
        IsBusy = true;
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);
        activityRun = run;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActivityRunning)));
        try
        {
            Status = RemoteLabels.Text("Activité en cours…", "Activity running…");
            var report = await mediator.Send(new RunActivity(id), run.Token);
            Status = report.Status switch
            {
                ActivityRunStatus.Completed => RemoteLabels.Text("Activité terminée ; vérifiez les appareils.", "Activity completed; check your devices."),
                ActivityRunStatus.Cancelled => RemoteLabels.Text("Activité interrompue. Les commandes déjà envoyées restent effectives.", "Activity stopped. Commands already sent remain effective."),
                _ => RemoteLabels.Text("Activité arrêtée ou partiellement exécutée ; aucun renvoi automatique.", "Activity stopped or partially executed; no automatic resend.")
            };
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        { Status = RemoteLabels.Text("Activité interrompue ; vérifiez les appareils.", "Activity stopped; check your devices."); }
        catch (Exception)
        { Status = RemoteLabels.Text("Impossible de terminer l’activité. Vérifiez les appareils.", "Unable to complete the activity. Check your devices."); }
        finally
        {
            activityRun = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActivityRunning)));
            IsBusy = false;
        }
    }

    public async Task ExecuteAsync(RemoteUiControl control, CancellationToken cancellationToken = default)
    {
        var snapshot = Model;
        if (snapshot is null || IsBusy || !snapshot.Controls.Any(c => c.Action == control.Action)) return;
        try
        {
            IsBusy = true;
            Status = RemoteLabels.Text("Envoi en cours…", "Sending…");
            var result = await mediator.Send(new ExecuteRemoteAction(snapshot.DeviceId, control.Action), cancellationToken);
            if (result.IsSuccess)
                Status = IsDemo ? RemoteLabels.Text("Commande simulée.", "Command simulated.")
                    : RemoteLabels.Text("Commande acceptée — vérifiez la réaction de l’appareil.", "Command accepted — check the device response.");
            else Status = result.Error switch
            {
                RemoteErrorCode.PairingRequired => RemoteLabels.Text("Associez à nouveau cet appareil.", "Pair this device again."),
                RemoteErrorCode.UnsupportedAction => RemoteLabels.Text("Cette commande n’est pas disponible.", "This command is unavailable."),
                RemoteErrorCode.DeviceNotFound => RemoteLabels.Text("Cet appareil n’est plus disponible.", "This device is no longer available."),
                _ => RemoteLabels.Text("La commande n’a pas pu être confirmée. Aucun renvoi automatique.", "The command could not be confirmed. No automatic resend.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { Status = RemoteLabels.Text("Envoi annulé ; une commande déjà partie peut avoir été reçue.", "Sending cancelled; a command already sent may have arrived."); }
        catch (Exception)
        { Status = RemoteLabels.Text("Impossible de confirmer la commande. Vérifiez l’appareil avant de réessayer.", "Unable to confirm the command. Check the device before retrying."); }
        finally { IsBusy = false; }
    }
    public void PreferenceSaveFailed() => Status = RemoteLabels.Text("Interface appliquée, mais le choix n’a pas pu être enregistré.", "Style applied, but your choice could not be saved.");
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
