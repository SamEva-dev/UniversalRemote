using DomainRelay.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UniversalRemote.Application;

namespace UniversalRemote.Maui.Rooms;

public sealed class RoomsViewModel(IMediator mediator) : INotifyPropertyChanged
{
    private IReadOnlyList<RoomSummary> rooms = Array.Empty<RoomSummary>();
    private IReadOnlyList<DeviceSummary> devices = Array.Empty<DeviceSummary>();
    private string status = "Organise tes appareils par pièce.";
    private bool isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<RoomSummary> Rooms { get => rooms; private set => Set(ref rooms, value); }
    public IReadOnlyList<DeviceSummary> Devices { get => devices; private set => Set(ref devices, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var roomItems = await mediator.Send(new ListRooms(), cancellationToken);
            var deviceItems = await mediator.Send(new ListDevices(), cancellationToken);
            Rooms = roomItems;
            Devices = deviceItems;
            Status = Rooms.Count == 0
                ? "Aucune pièce. Crée par exemple Salon, Chambre ou Cuisine."
                : $"{Rooms.Count} pièce(s), {Devices.Count} appareil(s) disponible(s).";
        }
        catch (OperationCanceledException) { Status = "Actualisation annulée."; }
        catch (Exception) { Status = "Impossible de charger les pièces. Réessaie."; }
        finally { IsBusy = false; }
    }

    public async Task CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new CreateRoom(name), cancellationToken);
        await RefreshAfterMutationAsync("Pièce créée.", cancellationToken);
    }

    public async Task RenameAsync(Guid roomId, string name, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new RenameRoom(roomId, name), cancellationToken);
        await RefreshAfterMutationAsync("Pièce renommée.", cancellationToken);
    }

    public async Task DeleteAsync(Guid roomId, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new DeleteRoom(roomId), cancellationToken);
        await RefreshAfterMutationAsync("Pièce supprimée. Les appareils restent enregistrés.", cancellationToken);
    }

    public async Task AssignAsync(Guid roomId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new AssignDeviceToRoom(roomId, deviceId), cancellationToken);
        await RefreshAfterMutationAsync("Appareil affecté à la pièce.", cancellationToken);
    }

    public async Task UnassignAsync(Guid roomId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new UnassignDeviceFromRoom(roomId, deviceId), cancellationToken);
        await RefreshAfterMutationAsync("Appareil retiré de la pièce.", cancellationToken);
    }

    public void ReportError() => Status = "Opération impossible. Vérifie les informations et réessaie.";

    private async Task RefreshAfterMutationAsync(string successStatus, CancellationToken cancellationToken)
    {
        var roomItems = await mediator.Send(new ListRooms(), cancellationToken);
        var deviceItems = await mediator.Send(new ListDevices(), cancellationToken);
        Rooms = roomItems;
        Devices = deviceItems;
        Status = successStatus;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
