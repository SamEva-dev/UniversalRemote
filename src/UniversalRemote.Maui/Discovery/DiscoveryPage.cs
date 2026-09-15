using UniversalRemote.Application;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Maui.Discovery;

public sealed class DiscoveryPage : ContentPage
{
    private readonly DiscoveryViewModel viewModel;
    private readonly VerticalStackLayout savedDevices = new() { Spacing = 8 };
    private bool pairing;
    private readonly VerticalStackLayout results = new() { Spacing = 12 };
    private readonly Button scanButton = new() { Text = "Rechercher sur le réseau", MinimumHeightRequest = 48 };
    private readonly Label status = new() { HorizontalTextAlignment = TextAlignment.Center, FontSize = 12 };
    private readonly Entry manualAddress = new() { Placeholder = "Adresse IP locale, ex. 192.168.1.30", Keyboard = Keyboard.Text };
    private readonly Button manualButton = new() { Text = "Ajouter par adresse locale", MinimumHeightRequest = 48 };
    private readonly VerticalStackLayout manualResults = new() { Spacing = 8 };

    public DiscoveryPage(DiscoveryViewModel viewModel)
    {
        this.viewModel = viewModel;
        Title = "Appareils";
        BindingContext = viewModel;
        scanButton.Clicked += async (_, _) =>
        {
            scanButton.IsEnabled = false;
            try { await viewModel.ScanAsync().ConfigureAwait(true); RenderResults(); }
            finally { scanButton.IsEnabled = true; }
        };
        manualButton.Clicked += async (_, _) =>
        {
            manualButton.IsEnabled = false;
            manualResults.Children.Clear();
            try
            {
                var candidates = await viewModel.FindManualPairingCandidatesAsync(manualAddress.Text ?? string.Empty).ConfigureAwait(true);
                foreach (var candidate in candidates)
                {
                    var button = new Button { Text = $"Associer {candidate.DisplayName}", MinimumHeightRequest = 48 };
                    button.Clicked += async (_, _) => await PairAsync(candidate, button).ConfigureAwait(true);
                    manualResults.Children.Add(button);
                }
            }
            finally { manualButton.IsEnabled = true; }
        };
        status.SetBinding(Label.TextProperty, nameof(DiscoveryViewModel.Status));
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20), Spacing = 16,
                Children =
                {
                    new Label { Text = "Mes appareils", FontSize = 24, FontAttributes = FontAttributes.Bold },
                    savedDevices,
                    new Label { Text = "Découverte réseau", FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Le scan recherche les services réseau puis propose les associations supportées. Détecté ne signifie pas encore pilotable.", FontSize = 13 },
                    scanButton,
                    new Border
                    {
                        Padding = new Thickness(14), StrokeThickness = 1,
                        Content = new VerticalStackLayout
                        {
                            Spacing = 8,
                            Children =
                            {
                                new Label { Text = "Ajout manuel", FontAttributes = FontAttributes.Bold, FontSize = 16 },
                                new Label { Text = "Pour les équipements qui n’annoncent pas leur service, saisis uniquement une adresse IP locale. Le provider décide s’il peut l’associer en sécurité.", FontSize = 12 },
                                manualAddress, manualButton, manualResults
                            }
                        }
                    },
                    status, results
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.RefreshSavedDevicesAsync();
        RenderSavedDevices();
    }

    private void RenderSavedDevices()
    {
        savedDevices.Children.Clear();
        if (viewModel.SavedDevices.Count == 0)
            savedDevices.Children.Add(new Label { Text = "Aucun appareil enregistré. Associe un appareil ci-dessous." });
        foreach (var device in viewModel.SavedDevices)
        {
            var open = new Button { Text = device.DisplayName, MinimumHeightRequest = 48 };
            open.Clicked += async (_, _) =>
            {
                if (pairing || viewModel.IsBusy) return;
                open.IsEnabled = false;
                try
                {
                    viewModel.SelectSavedDevice(device);
                    await Shell.Current.GoToAsync("//remote");
                }
                catch (Exception) { await DisplayAlertAsync("Télécommande", "Impossible d’ouvrir la télécommande. Réessaie.", "OK"); }
                finally { open.IsEnabled = true; }
            };
            savedDevices.Children.Add(open);
        }
    }

    private void RenderResults()
    {
        results.Children.Clear();
        foreach (var item in viewModel.Devices) results.Children.Add(Card(item));
    }

    private View Card(DiscoveredDeviceItem item)
    {
        var device = item.Device;
        var sources = device.Sources.Count == 0 ? "inconnue" : string.Join(" + ", device.Sources);
        var address = device.Addresses.FirstOrDefault() ?? device.HostName ?? "adresse non résolue";
        var content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = device.DisplayName, FontAttributes = FontAttributes.Bold, FontSize = 16 },
                new Label { Text = address, FontSize = 12 },
                new Label { Text = $"Source : {sources}", FontSize = 12 },
                new Label { Text = device.Services.Count == 0 ? "service non identifié" : string.Join("\n", device.Services.Take(4)), FontSize = 11 }
            }
        };
        foreach (var candidate in item.PairingCandidates)
        {
            var button = new Button { Text = "Associer", MinimumHeightRequest = 48 };
            button.Clicked += async (_, _) => await PairAsync(candidate, button).ConfigureAwait(true);
            content.Children.Add(button);
        }
        return new Border { Padding = new Thickness(14), StrokeThickness = 1, Content = content };
    }

    private async Task PairAsync(PairingCandidate candidate, Button button)
    {
        if (pairing || viewModel.IsBusy) return;
        pairing = true;
        button.IsEnabled = scanButton.IsEnabled = manualButton.IsEnabled = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var challenge = await viewModel.StartPairingAsync(candidate, deadline.Token).ConfigureAwait(true);
            string code;
            if (challenge.CodeLength <= 0)
            {
                var confirmed = await DisplayAlertAsync("Association", challenge.Prompt, "Continuer", "Annuler").ConfigureAwait(true);
                if (!confirmed) { viewModel.ReportPairingCancelled(); return; }
                code = string.Empty;
            }
            else
            {
                code = await DisplayPromptAsync("Association", challenge.Prompt, "Associer", "Annuler", maxLength: challenge.CodeLength, keyboard: Keyboard.Text).ConfigureAwait(true) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(code)) { viewModel.ReportPairingCancelled(); return; }
            }
            await viewModel.CompletePairingAsync(candidate, challenge, code, deadline.Token).ConfigureAwait(true);
            RenderSavedDevices();
        }
        catch (Exception) { viewModel.ReportPairingError(); }
        finally { pairing = false; button.IsEnabled = scanButton.IsEnabled = manualButton.IsEnabled = true; }
    }
}
