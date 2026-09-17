using UniversalRemote.Remote.Application;

namespace UniversalRemote.Maui.Rooms;

public sealed class RoomsPage : ContentPage
{
    private readonly RoomsViewModel viewModel;
    private readonly VerticalStackLayout roomCards = new() { Spacing = 12 };
    private readonly Label status = new() { FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Button addButton = new() { Text = "Ajouter une pièce", MinimumHeightRequest = 48 };
    private readonly Button refreshButton = new() { Text = "Actualiser", MinimumHeightRequest = 48 };

    public RoomsPage(RoomsViewModel viewModel)
    {
        this.viewModel = viewModel;
        Title = "Pièces";
        BindingContext = viewModel;
        status.SetBinding(Label.TextProperty, nameof(RoomsViewModel.Status));
        addButton.Clicked += async (_, _) => await CreateRoomAsync().ConfigureAwait(true);
        refreshButton.Clicked += async (_, _) => await RefreshAsync().ConfigureAwait(true);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new Label { Text = "Pièces", FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label
                    {
                        Text = "Regroupe tes appareils par emplacement. Un appareil appartient à une seule pièce ; le réaffecter le déplace automatiquement.",
                        FontSize = 13
                    },
                    new HorizontalStackLayout { Spacing = 10, Children = { addButton, refreshButton } },
                    status,
                    roomCards
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        SetButtons(false);
        try
        {
            await viewModel.RefreshAsync().ConfigureAwait(true);
            RenderRooms();
        }
        finally { SetButtons(true); }
    }

    private async Task CreateRoomAsync()
    {
        var name = await DisplayPromptAsync("Nouvelle pièce", "Nom de la pièce", "Créer", "Annuler", maxLength: 60).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            SetButtons(false);
            await viewModel.CreateAsync(name).ConfigureAwait(true);
            RenderRooms();
        }
        catch (Exception) { viewModel.ReportError(); }
        finally { SetButtons(true); }
    }

    private View RoomCard(RoomSummary room)
    {
        var content = new VerticalStackLayout { Spacing = 10 };
        content.Children.Add(new Label { Text = room.Name, FontSize = 18, FontAttributes = FontAttributes.Bold });
        content.Children.Add(new Label
        {
            Text = room.Devices.Count == 0 ? "Aucun appareil affecté" : $"{room.Devices.Count} appareil(s)",
            FontSize = 12
        });

        foreach (var device in room.Devices)
        {
            var remove = new Button { Text = "Retirer", MinimumHeightRequest = 44 };
            remove.Clicked += async (_, _) => await UnassignAsync(room.Id, device.Id, remove).ConfigureAwait(true);
            content.Children.Add(new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                Children =
                {
                    new Label { Text = device.DisplayName, VerticalOptions = LayoutOptions.Center },
                    remove
                }
            });
            Grid.SetColumn(remove, 1);
        }

        if (viewModel.Devices.Count > 0)
        {
            var picker = new Picker { Title = "Choisir un appareil", MinimumHeightRequest = 48 };
            picker.ItemsSource = (System.Collections.IList)viewModel.Devices;
            picker.ItemDisplayBinding = new Binding(nameof(DeviceSummary.DisplayName));
            var assign = new Button { Text = "Affecter / déplacer", MinimumHeightRequest = 48 };
            assign.Clicked += async (_, _) =>
            {
                if (picker.SelectedItem is DeviceSummary selected)
                    await AssignAsync(room.Id, selected.Id, assign).ConfigureAwait(true);
            };
            content.Children.Add(picker);
            content.Children.Add(assign);
        }

        var rename = new Button { Text = "Renommer", MinimumHeightRequest = 44 };
        rename.Clicked += async (_, _) => await RenameAsync(room, rename).ConfigureAwait(true);
        var delete = new Button { Text = "Supprimer", MinimumHeightRequest = 44 };
        delete.Clicked += async (_, _) => await DeleteAsync(room, delete).ConfigureAwait(true);
        content.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { rename, delete } });

        return new Border { Padding = new Thickness(14), StrokeThickness = 1, Content = content };
    }

    private void RenderRooms()
    {
        roomCards.Children.Clear();
        foreach (var room in viewModel.Rooms) roomCards.Children.Add(RoomCard(room));
    }

    private async Task RenameAsync(RoomSummary room, Button button)
    {
        var name = await DisplayPromptAsync("Renommer la pièce", "Nouveau nom", "Enregistrer", "Annuler", initialValue: room.Name, maxLength: 60).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;
        button.IsEnabled = false;
        try
        {
            await viewModel.RenameAsync(room.Id, name).ConfigureAwait(true);
            RenderRooms();
        }
        catch (Exception) { viewModel.ReportError(); }
        finally { button.IsEnabled = true; }
    }

    private async Task DeleteAsync(RoomSummary room, Button button)
    {
        var confirmed = await DisplayAlertAsync("Supprimer la pièce", $"Supprimer « {room.Name} » ? Les appareils resteront enregistrés.", "Supprimer", "Annuler").ConfigureAwait(true);
        if (!confirmed) return;
        button.IsEnabled = false;
        try
        {
            await viewModel.DeleteAsync(room.Id).ConfigureAwait(true);
            RenderRooms();
        }
        catch (Exception) { viewModel.ReportError(); }
        finally { button.IsEnabled = true; }
    }

    private async Task AssignAsync(Guid roomId, Guid deviceId, Button button)
    {
        button.IsEnabled = false;
        try
        {
            await viewModel.AssignAsync(roomId, deviceId).ConfigureAwait(true);
            RenderRooms();
        }
        catch (Exception) { viewModel.ReportError(); }
        finally { button.IsEnabled = true; }
    }

    private async Task UnassignAsync(Guid roomId, Guid deviceId, Button button)
    {
        button.IsEnabled = false;
        try
        {
            await viewModel.UnassignAsync(roomId, deviceId).ConfigureAwait(true);
            RenderRooms();
        }
        catch (Exception) { viewModel.ReportError(); }
        finally { button.IsEnabled = true; }
    }

    private void SetButtons(bool enabled)
    {
        addButton.IsEnabled = enabled;
        refreshButton.IsEnabled = enabled;
    }
}
