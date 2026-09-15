using DomainRelay.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UniversalRemote.Abstractions;
using UniversalRemote.Application;
using UniversalRemote.Presentation;

namespace UniversalRemote.Maui.Remote;

public sealed class RemoteViewModel(IMediator mediator, DeviceSelectionState selection) : INotifyPropertyChanged
{
    private RemoteUiModel? model;
    private string layoutId = "classic";
    private string status = RemoteLabels.Text("Sélectionnez un appareil.", "Select a device.");
    private bool isBusy;
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
            Status = Model is null ? RemoteLabels.Text("Appareil indisponible.", "Device unavailable.") : IsDemo
                ? RemoteLabels.Text("Démonstration — aucune commande réelle.", "Demo — no real commands.")
                : RemoteLabels.Text("Prêt à envoyer une commande.", "Ready to send a command.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { Status = RemoteLabels.Text("Impossible de charger cet appareil. Réessayez depuis la liste.", "Unable to load this device. Try again from the device list."); }
        finally { IsBusy = false; }
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
