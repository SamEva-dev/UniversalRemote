using UniversalRemote.Application;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Maui.Discovery;

public sealed class DiscoveryPage : ContentPage
{
    private readonly DiscoveryViewModel viewModel;
    private readonly VerticalStackLayout results = new() { Spacing = 12 };
    private readonly Button scanButton = new() { Text = "Rechercher sur le réseau", MinimumHeightRequest = 48 };
    private readonly Label status = new() { HorizontalTextAlignment = TextAlignment.Center, FontSize = 12 };

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
        status.SetBinding(Label.TextProperty, nameof(DiscoveryViewModel.Status));
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20), Spacing = 16,
                Children =
                {
                    new Label { Text = "Découverte réseau", FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Le scan recherche les services réseau puis propose les associations supportées. Détecté ne signifie pas encore pilotable.", FontSize = 13 },
                    scanButton, status, results
                }
            }
        };
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
        button.IsEnabled = false;
        try
        {
            var challenge = await viewModel.StartPairingAsync(candidate).ConfigureAwait(true);
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
            await viewModel.CompletePairingAsync(candidate, challenge, code).ConfigureAwait(true);
        }
        catch (Exception) { viewModel.ReportPairingError(); }
        finally { button.IsEnabled = true; }
    }
}
