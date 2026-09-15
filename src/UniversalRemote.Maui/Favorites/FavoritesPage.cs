using UniversalRemote.Application;
using UniversalRemote.Presentation;

namespace UniversalRemote.Maui.Favorites;

public sealed class FavoritesPage : ContentPage
{
    private readonly FavoritesViewModel viewModel;
    private readonly Picker devicePicker = new() { Title = RemoteLabels.Text("Appareil", "Device"), MinimumHeightRequest = 48 };
    private readonly VerticalStackLayout controlsHost = new() { Spacing = 12 };
    private readonly Label status = new() { FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Button refreshButton = new() { Text = RemoteLabels.Text("Actualiser", "Refresh"), MinimumHeightRequest = 48 };
    private bool selecting;

    public FavoritesPage(FavoritesViewModel viewModel)
    {
        this.viewModel = viewModel;
        Title = RemoteLabels.Text("Favoris", "Favorites");
        BindingContext = viewModel;

        devicePicker.ItemDisplayBinding = new Binding(nameof(DeviceSummary.DisplayName));
        devicePicker.SelectedIndexChanged += async (_, _) =>
        {
            if (selecting || devicePicker.SelectedItem is not DeviceSummary selected) return;
            await SelectAsync(selected.Id).ConfigureAwait(true);
        };
        refreshButton.Clicked += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        status.SetBinding(Label.TextProperty, nameof(FavoritesViewModel.Status));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new Label { Text = RemoteLabels.Text("Favoris", "Favorites"), FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label
                    {
                        Text = RemoteLabels.Text(
                            "Épinglez les commandes que vous utilisez le plus. Elles apparaissent en tête des sept interfaces sans modifier les capacités de l’appareil.",
                            "Pin the commands you use most. They appear at the top of all seven styles without changing device capabilities."),
                        FontSize = 13
                    },
                    devicePicker,
                    refreshButton,
                    status,
                    controlsHost
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
        SetEnabled(false);
        try
        {
            var selectedId = (devicePicker.SelectedItem as DeviceSummary)?.Id;
            await viewModel.RefreshDevicesAsync().ConfigureAwait(true);
            selecting = true;
            devicePicker.ItemsSource = (System.Collections.IList)viewModel.Devices;
            var index = selectedId is null ? -1 : viewModel.Devices.ToList().FindIndex(x => x.Id == selectedId.Value);
            devicePicker.SelectedIndex = index >= 0 ? index : viewModel.Devices.Count > 0 ? 0 : -1;
            selecting = false;
            if (devicePicker.SelectedItem is DeviceSummary selected)
                await viewModel.SelectDeviceAsync(selected.Id).ConfigureAwait(true);
            RenderControls();
        }
        finally
        {
            selecting = false;
            SetEnabled(true);
        }
    }

    private async Task SelectAsync(Guid deviceId)
    {
        SetEnabled(false);
        try
        {
            await viewModel.SelectDeviceAsync(deviceId).ConfigureAwait(true);
            RenderControls();
        }
        finally { SetEnabled(true); }
    }

    private void RenderControls()
    {
        controlsHost.Children.Clear();
        var model = viewModel.Model;
        if (model is null) return;

        foreach (var section in model.Sections)
        {
            controlsHost.Children.Add(new Label
            {
                Text = RemoteLabels.Section(section.Id),
                FontAttributes = FontAttributes.Bold,
                FontSize = 16
            });

            foreach (var control in section.Controls)
            {
                var favorite = viewModel.IsFavorite(control);
                var toggle = new Button
                {
                    Text = favorite ? RemoteLabels.Text("★ Retirer", "★ Remove") : RemoteLabels.Text("☆ Ajouter", "☆ Add"),
                    MinimumHeightRequest = 44,
                    AutomationId = $"favorite-toggle-{control.Action.Id}"
                };
                toggle.Clicked += async (_, _) => await ToggleAsync(control, toggle).ConfigureAwait(true);

                var label = new Label
                {
                    Text = RemoteLabels.Action(control),
                    VerticalOptions = LayoutOptions.Center,
                    LineBreakMode = LineBreakMode.TailTruncation
                };
                var row = new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    ColumnSpacing = 12,
                    Children = { label, toggle }
                };
                Grid.SetColumn(toggle, 1);
                controlsHost.Children.Add(new Border { Padding = new Thickness(12, 6), StrokeThickness = 1, Content = row });
            }
        }
    }

    private async Task ToggleAsync(RemoteUiControl control, Button button)
    {
        button.IsEnabled = false;
        try
        {
            await viewModel.ToggleAsync(control).ConfigureAwait(true);
            RenderControls();
        }
        finally { button.IsEnabled = true; }
    }

    private void SetEnabled(bool enabled)
    {
        devicePicker.IsEnabled = enabled;
        refreshButton.IsEnabled = enabled;
        controlsHost.IsEnabled = enabled;
    }
}
