using DomainRelay.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UniversalRemote.Application;
using UniversalRemote.Presentation;

namespace UniversalRemote.Maui.Favorites;

public sealed class FavoritesViewModel(IMediator mediator) : INotifyPropertyChanged
{
    private IReadOnlyList<DeviceSummary> devices = Array.Empty<DeviceSummary>();
    private RemoteUiModel? model;
    private string status = RemoteLabels.Text("Choisissez un appareil et ses raccourcis.", "Choose a device and its shortcuts.");
    private bool isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<DeviceSummary> Devices { get => devices; private set => Set(ref devices, value); }
    public RemoteUiModel? Model { get => model; private set => Set(ref model, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }

    public async Task RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            Devices = await mediator.Send(new ListDevices(), cancellationToken);
            Status = Devices.Count == 0
                ? RemoteLabels.Text("Aucun appareil enregistré.", "No registered devices.")
                : RemoteLabels.Text("Sélectionnez un appareil.", "Select a device.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { Status = RemoteLabels.Text("Impossible de charger les appareils.", "Unable to load devices."); }
        finally { IsBusy = false; }
    }

    public async Task SelectDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            Model = await mediator.Send(new GetRemoteUiModel(deviceId), cancellationToken);
            Status = Model is null
                ? RemoteLabels.Text("Appareil indisponible.", "Device unavailable.")
                : Model.Controls.Count == 0
                    ? RemoteLabels.Text("Cet appareil n’expose aucune commande.", "This device exposes no commands.")
                    : RemoteLabels.Text($"{Model.Favorites.Count} favori(s) sur {Model.Controls.Count} commande(s).", $"{Model.Favorites.Count} favorite(s) out of {Model.Controls.Count} command(s).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { Status = RemoteLabels.Text("Impossible de charger les favoris.", "Unable to load favorites."); }
        finally { IsBusy = false; }
    }

    public bool IsFavorite(RemoteUiControl control)
        => Model?.Favorites.Any(x => string.Equals(x.Action.Id, control.Action.Id, StringComparison.Ordinal)) == true;

    public async Task ToggleAsync(RemoteUiControl control, CancellationToken cancellationToken = default)
    {
        var snapshot = Model ?? throw new InvalidOperationException("No device selected.");
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            if (IsFavorite(control))
                await mediator.Send(new RemoveDeviceFavorite(snapshot.DeviceId, control.Action.Id), cancellationToken);
            else
                await mediator.Send(new AddDeviceFavorite(snapshot.DeviceId, control.Action.Id), cancellationToken);

            Model = await mediator.Send(new GetRemoteUiModel(snapshot.DeviceId), cancellationToken);
            Status = Model is null
                ? RemoteLabels.Text("Appareil indisponible.", "Device unavailable.")
                : RemoteLabels.Text($"{Model.Favorites.Count} favori(s).", $"{Model.Favorites.Count} favorite(s).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { Status = RemoteLabels.Text("Impossible de modifier ce favori.", "Unable to update this favorite."); }
        finally { IsBusy = false; }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
