using DomainRelay.Abstractions;
using UniversalRemote.Maui.Remote;
using UniversalRemote.Remote.Application;

namespace UniversalRemote.Maui.Discovery;

public sealed partial class DeviceSetupPage : ContentPage
{
    private readonly IMediator mediator;
    private readonly DeviceSetupState state;
    private readonly DeviceSelectionState selection;
    private IReadOnlyList<RoomSummary> rooms = Array.Empty<RoomSummary>();
    private bool saving;

    public DeviceSetupPage(IMediator mediator, DeviceSetupState state, DeviceSelectionState selection)
    {
        InitializeComponent();
        this.mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.selection = selection ?? throw new ArgumentNullException(nameof(selection));

        roomPicker.SelectedIndexChanged += (_, _) =>
            newRoomEntry.IsVisible = roomPicker.SelectedIndex == rooms.Count + 1;
        saveButton.Clicked += async (_, _) => await SaveAsync(assignRoom: true);
        skipButton.Clicked += async (_, _) => await SaveAsync(assignRoom: false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!state.HasPendingDevice)
        {
            statusLabel.Text = "Aucun appareil à finaliser.";
            saveButton.IsEnabled = false;
            skipButton.IsEnabled = false;
            return;
        }

        nameEntry.Text = state.SuggestedName;
        try
        {
            rooms = await mediator.Send(new ListRooms());
            var labels = new List<string> { "Aucune pièce" };
            labels.AddRange(rooms.Select(x => x.Name));
            labels.Add("+ Créer une nouvelle pièce");
            roomPicker.ItemsSource = labels;
            roomPicker.SelectedIndex = 0;
            nameEntry.Focus();
        }
        catch (Exception)
        {
            statusLabel.Text = "Les pièces n’ont pas pu être chargées. Vous pouvez quand même nommer l’appareil.";
        }
    }

    private async Task SaveAsync(bool assignRoom)
    {
        if (saving || !state.HasPendingDevice) return;
        var name = (nameEntry.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlertAsync("Appareil", "Donnez un nom à l’appareil.", "OK");
            return;
        }

        saving = true;
        saveButton.IsEnabled = skipButton.IsEnabled = false;
        statusLabel.Text = "Enregistrement…";
        try
        {
            await mediator.Send(new RenameDevice(state.DeviceId, name));

            if (assignRoom && roomPicker.SelectedIndex > 0)
            {
                Guid roomId;
                if (roomPicker.SelectedIndex == rooms.Count + 1)
                {
                    var roomName = (newRoomEntry.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(roomName))
                    {
                        await DisplayAlertAsync("Pièce", "Indiquez le nom de la nouvelle pièce.", "OK");
                        return;
                    }
                    roomId = (await mediator.Send(new CreateRoom(roomName))).Id;
                }
                else
                {
                    roomId = rooms[roomPicker.SelectedIndex - 1].Id;
                }
                await mediator.Send(new AssignDeviceToRoom(roomId, state.DeviceId));
            }

            selection.ActiveDeviceId = state.DeviceId;
            state.Clear();
            await Shell.Current.GoToAsync("//remote");
        }
        catch (Exception)
        {
            statusLabel.Text = "Impossible d’enregistrer les informations. Réessayez.";
        }
        finally
        {
            saving = false;
            saveButton.IsEnabled = skipButton.IsEnabled = state.HasPendingDevice;
        }
    }
}
