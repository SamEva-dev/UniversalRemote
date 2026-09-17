using UniversalRemote.Remote.Presentation;

namespace UniversalRemote.Maui.Hub;

public sealed class HubPage : ContentPage
{
    private readonly HubViewModel viewModel;
    private readonly VerticalStackLayout hubsHost = new() { Spacing = 10 };
    private readonly VerticalStackLayout profilesHost = new() { Spacing = 8 };
    private readonly Label status = new() { FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Button refresh = new() { Text = RemoteLabels.Text("Rechercher les Hubs", "Discover hubs"), MinimumHeightRequest = 48 };
    private readonly Entry profileId = new() { Placeholder = "learned.tv", Text = "learned.tv" };
    private readonly Entry displayName = new() { Placeholder = RemoteLabels.Text("TV du salon", "Living room TV"), Text = "TV IR" };
    private readonly Picker actionPicker = new() { Title = RemoteLabels.Text("Action", "Action") };
    private readonly Switch overwrite = new();
    private readonly Button learn = new() { Text = RemoteLabels.Text("Apprendre la commande", "Learn command"), MinimumHeightRequest = 48 };
    private readonly Editor importJson = new() { Placeholder = RemoteLabels.Text("Coller ici un profil IR JSON v1…", "Paste an IR profile JSON v1 here…"), AutoSize = EditorAutoSizeOption.TextChanges, HeightRequest = 160 };
    private readonly Button import = new() { Text = RemoteLabels.Text("Importer le profil", "Import profile"), MinimumHeightRequest = 48 };

    public HubPage(HubViewModel viewModel)
    {
        this.viewModel = viewModel;
        Title = RemoteLabels.Text("Hub IR", "IR Hub");
        BindingContext = viewModel;
        status.SetBinding(Label.TextProperty, nameof(HubViewModel.Status));
        actionPicker.ItemsSource = new[]
        {
            "power.toggle", "volume.up", "volume.down", "audio.mute.toggle",
            "navigation.up", "navigation.down", "navigation.left", "navigation.right", "navigation.ok",
            "navigation.back", "navigation.home", "navigation.menu", "channel.up", "channel.down", "media.playpause"
        };
        actionPicker.SelectedIndex = 0;
        refresh.Clicked += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        learn.Clicked += async (_, _) => await LearnAsync().ConfigureAwait(true);
        import.Clicked += async (_, _) => await ImportAsync().ConfigureAwait(true);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 14,
                Children =
                {
                    new Label { Text = RemoteLabels.Text("Hub infrarouge externe", "External infrared hub"), FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label { Text = RemoteLabels.Text("Le Hub est utilisé automatiquement par Generic IR lorsqu’aucun émetteur natif adapté n’est disponible. Aucun basculement n’est effectué après le début d’une émission.", "Generic IR automatically uses the selected hub when no suitable native emitter is available. No failover occurs after transmission starts."), FontSize = 13 },
                    refresh,
                    status,
                    hubsHost,
                    new Label { Text = RemoteLabels.Text("Apprentissage", "Learning"), FontSize = 20, FontAttributes = FontAttributes.Bold },
                    profileId,
                    displayName,
                    actionPicker,
                    new HorizontalStackLayout { Spacing = 10, Children = { new Label { Text = RemoteLabels.Text("Remplacer l’action existante", "Overwrite existing action"), VerticalTextAlignment = TextAlignment.Center }, overwrite } },
                    learn,
                    new Label { Text = RemoteLabels.Text("Import JSON", "JSON import"), FontSize = 20, FontAttributes = FontAttributes.Bold },
                    importJson,
                    import,
                    new Label { Text = RemoteLabels.Text("Profils IR locaux", "Local IR profiles"), FontSize = 20, FontAttributes = FontAttributes.Bold },
                    profilesHost
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
        try { await viewModel.RefreshAsync().ConfigureAwait(true); Render(); }
        finally { SetEnabled(true); }
    }

    private async Task LearnAsync()
    {
        var actionId = actionPicker.SelectedItem as string ?? string.Empty;
        SetEnabled(false);
        try
        {
            await viewModel.LearnAsync(profileId.Text ?? string.Empty, displayName.Text ?? string.Empty, actionId, overwrite.IsToggled).ConfigureAwait(true);
            RenderProfiles();
        }
        finally { SetEnabled(true); }
    }

    private async Task ImportAsync()
    {
        SetEnabled(false);
        try
        {
            await viewModel.ImportAsync(importJson.Text ?? string.Empty).ConfigureAwait(true);
            RenderProfiles();
        }
        finally { SetEnabled(true); }
    }

    private void Render()
    {
        hubsHost.Children.Clear();
        foreach (var candidate in viewModel.Candidates)
        {
            var button = new Button
            {
                Text = candidate.IsSelected
                    ? RemoteLabels.Text("Sélectionné", "Selected")
                    : RemoteLabels.Text("Connecter et sélectionner", "Connect and select"),
                IsEnabled = !candidate.IsSelected,
                MinimumHeightRequest = 48
            };
            button.Clicked += async (_, _) =>
            {
                SetEnabled(false);
                try { await viewModel.SelectAsync(candidate).ConfigureAwait(true); Render(); }
                finally { SetEnabled(true); }
            };
            hubsHost.Children.Add(new Border
            {
                Padding = new Thickness(12),
                StrokeThickness = 1,
                Content = new VerticalStackLayout
                {
                    Spacing = 5,
                    Children =
                    {
                        new Label { Text = candidate.DisplayName, FontAttributes = FontAttributes.Bold, FontSize = 16 },
                        new Label { Text = $"{candidate.TransportKind} • {candidate.FirmwareVersion ?? "firmware ?"}", FontSize = 12 },
                        new Label { Text = candidate.DiagnosticCode, FontSize = 11 },
                        button
                    }
                }
            });
        }
        RenderProfiles();
    }

    private void RenderProfiles()
    {
        profilesHost.Children.Clear();
        foreach (var profile in viewModel.Profiles)
        {
            profilesHost.Children.Add(new Border
            {
                Padding = new Thickness(10),
                StrokeThickness = 1,
                Content = new VerticalStackLayout
                {
                    Spacing = 3,
                    Children =
                    {
                        new Label { Text = $"{profile.DisplayName} — {profile.Id}", FontAttributes = FontAttributes.Bold },
                        new Label { Text = $"{profile.CarrierFrequencyHz / 1000d:0.#} kHz • {profile.Commands.Count} action(s) • {(profile.Verified ? "verified" : "unverified")}", FontSize = 11 }
                    }
                }
            });
        }
    }

    private void SetEnabled(bool enabled)
    {
        refresh.IsEnabled = enabled;
        learn.IsEnabled = enabled;
        import.IsEnabled = enabled;
        hubsHost.IsEnabled = enabled;
        profileId.IsEnabled = enabled;
        displayName.IsEnabled = enabled;
        actionPicker.IsEnabled = enabled;
        overwrite.IsEnabled = enabled;
        importJson.IsEnabled = enabled;
    }
}
