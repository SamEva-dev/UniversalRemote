using Microsoft.Maui.ApplicationModel.DataTransfer;
using UniversalRemote.Remote.Presentation;

namespace UniversalRemote.Maui.Privacy;

public sealed class PrivacyPage : ContentPage
{
    private readonly PrivacyViewModel viewModel;
    private readonly Label status = new() { FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label count = new() { FontSize = 12 };
    private readonly Switch telemetrySwitch = new();
    private readonly Button exportButton = new() { Text = RemoteLabels.Text("Exporter les diagnostics", "Export diagnostics"), MinimumHeightRequest = 48 };
    private readonly Button clearButton = new() { Text = RemoteLabels.Text("Effacer les diagnostics", "Clear diagnostics"), MinimumHeightRequest = 48 };

    public PrivacyPage(PrivacyViewModel viewModel)
    {
        this.viewModel = viewModel;
        Title = RemoteLabels.Text("Confidentialité", "Privacy");
        BindingContext = viewModel;

        telemetrySwitch.SetBinding(Switch.IsToggledProperty, nameof(PrivacyViewModel.TelemetryEnabled), mode: BindingMode.TwoWay);
        count.SetBinding(Label.TextProperty, nameof(PrivacyViewModel.EventCountText));
        status.SetBinding(Label.TextProperty, nameof(PrivacyViewModel.Status));

        exportButton.Clicked += ExportClicked;
        clearButton.Clicked += ClearClicked;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 16,
                Children =
                {
                    new Label { Text = RemoteLabels.Text("Diagnostics respectueux de la confidentialité", "Privacy-preserving diagnostics"), FontSize = 22, FontAttributes = FontAttributes.Bold },
                    new Label { Text = RemoteLabels.Text("Désactivés par défaut. Les données restent sur cet appareil et ne contiennent ni IP, endpoint, token, payload, identifiant d'appareil ni message d'exception.", "Off by default. Data stays on this device and contains no IP, endpoint, token, payload, device identifier or exception message."), FontSize = 14 },
                    new HorizontalStackLayout
                    {
                        Spacing = 12,
                        Children = { new Label { Text = RemoteLabels.Text("Activer les diagnostics locaux", "Enable local diagnostics"), VerticalTextAlignment = TextAlignment.Center }, telemetrySwitch }
                    },
                    count,
                    new Label { Text = RemoteLabels.Text("Conservation : au plus 250 événements pendant 7 jours. Durées regroupées par catégories, jamais de chronométrage précis.", "Retention: at most 250 events for 7 days. Durations are bucketed; exact timing is not stored."), FontSize = 12 },
                    exportButton,
                    clearButton,
                    status
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await viewModel.RefreshAsync(); }
        catch (Exception) { await ShowStorageErrorAsync(); }
    }

    private async void ExportClicked(object? sender, EventArgs e)
    {
        try
        {
            exportButton.IsEnabled = false;
            var path = await viewModel.ExportAsync();
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = RemoteLabels.Text("Partager les diagnostics UniversalRemote", "Share UniversalRemote diagnostics"),
                File = new ShareFile(path)
            });
        }
        catch (Exception)
        {
            await DisplayAlert(RemoteLabels.Text("Export impossible", "Export failed"), RemoteLabels.Text("Le fichier de diagnostics n'a pas pu être créé ou partagé.", "The diagnostic file could not be created or shared."), "OK");
        }
        finally { exportButton.IsEnabled = true; }
    }

    private async void ClearClicked(object? sender, EventArgs e)
    {
        clearButton.IsEnabled = false;
        try
        {
            var confirmed = await DisplayAlertAsync(RemoteLabels.Text("Effacer ?", "Clear?"), RemoteLabels.Text("Supprimer tous les diagnostics locaux ?", "Delete all local diagnostics?"), RemoteLabels.Text("Effacer", "Clear"), RemoteLabels.Text("Annuler", "Cancel"));
            if (confirmed) await viewModel.ClearAsync();
        }
        catch (Exception) { await ShowStorageErrorAsync(); }
        finally { clearButton.IsEnabled = true; }
    }

    private Task ShowStorageErrorAsync() => DisplayAlertAsync(
        RemoteLabels.Text("Diagnostics indisponibles", "Diagnostics unavailable"),
        RemoteLabels.Text("Impossible d’accéder aux diagnostics locaux. Réessayez.", "Unable to access local diagnostics. Please try again."), "OK");
}
