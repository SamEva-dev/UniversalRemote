using Microsoft.Maui.ApplicationModel.DataTransfer;
using UniversalRemote.Remote.Compatibility;
using UniversalRemote.Remote.Presentation;

namespace UniversalRemote.Maui.Compatibility;

public sealed class CompatibilityPage : ContentPage
{
    private readonly CompatibilityViewModel viewModel;
    private readonly VerticalStackLayout profilesHost = new() { Spacing = 12 };
    private readonly Label status = new() { FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Button refreshButton = new() { Text = RemoteLabels.Text("Actualiser", "Refresh"), MinimumHeightRequest = 48 };
    private readonly Button csvButton = new() { Text = RemoteLabels.Text("Exporter CSV", "Export CSV"), MinimumHeightRequest = 48 };
    private readonly Button jsonButton = new() { Text = RemoteLabels.Text("Exporter JSON", "Export JSON"), MinimumHeightRequest = 48 };

    public CompatibilityPage(CompatibilityViewModel viewModel)
    {
        this.viewModel = viewModel;
        Title = RemoteLabels.Text("Compatibilité", "Compatibility");
        BindingContext = viewModel;

        status.SetBinding(Label.TextProperty, nameof(CompatibilityViewModel.Status));
        refreshButton.Clicked += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        csvButton.Clicked += async (_, _) => await ExportAsync(CompatibilityMatrixFormat.Csv).ConfigureAwait(true);
        jsonButton.Clicked += async (_, _) => await ExportAsync(CompatibilityMatrixFormat.Json).ConfigureAwait(true);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new Label { Text = RemoteLabels.Text("Compatibilité opérateurs", "Operator compatibility"), FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label
                    {
                        Text = RemoteLabels.Text(
                            "Cette vue expose la décision produit, le niveau de support, les preuves revues et la qualification runtime. Elle ne transforme jamais une détection en promesse de compatibilité.",
                            "This view exposes product decisions, support level, reviewed evidence and runtime qualification. Detection is never turned into a compatibility promise."),
                        FontSize = 13
                    },
                    new HorizontalStackLayout { Spacing = 8, Children = { refreshButton, csvButton, jsonButton } },
                    status,
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
        try
        {
            await viewModel.RefreshAsync().ConfigureAwait(true);
            RenderProfiles();
        }
        finally { SetEnabled(true); }
    }

    private void RenderProfiles()
    {
        profilesHost.Children.Clear();
        foreach (var profile in viewModel.Profiles)
        {
            var observed = profile.ProviderObserved || profile.Confidence is not null;
            var detected = profile.Confidence is null
                ? RemoteLabels.Text(observed ? "Provider observé ; modèle non qualifié" : "Non détecté pendant ce scan", observed ? "Provider observed; model not qualified" : "Not detected during this scan")
                : string.Join(" • ", new[] { profile.DetectedFamily, profile.DetectedModel, profile.Firmware }.Where(static x => !string.IsNullOrWhiteSpace(x)));

            profilesHost.Children.Add(new Border
            {
                Padding = new Thickness(14),
                StrokeThickness = 1,
                Content = new VerticalStackLayout
                {
                    Spacing = 5,
                    Children =
                    {
                        new Label { Text = $"{profile.Operator} — {profile.ProductFamily}", FontSize = 17, FontAttributes = FontAttributes.Bold },
                        new Label { Text = $"{RemoteLabels.Text("Statut", "Status")} : {profile.SupportStatus} • {profile.IntegrationMode}", FontSize = 12 },
                        new Label { Text = $"Provider : {profile.ProviderId ?? "—"} • {profile.Transport}", FontSize = 12 },
                        new Label { Text = $"{RemoteLabels.Text("Détection", "Detection")} : {detected}", FontSize = 12 },
                        new Label { Text = $"{RemoteLabels.Text("Recette", "Recipe")} : {viewModel.RecipeLabel(profile.RecipeState)}", FontSize = 12 },
                        new Label { Text = $"{RemoteLabels.Text("Preuves revues", "Reviewed evidence")} : {profile.Evidence.Count} • {profile.ReviewedOn:yyyy-MM-dd}", FontSize = 11 },
                        new Label { Text = profile.Decision, FontSize = 11 }
                    }
                }
            });
        }
    }

    private async Task ExportAsync(CompatibilityMatrixFormat format)
    {
        SetEnabled(false);
        try
        {
            var path = await viewModel.ExportAsync(format).ConfigureAwait(true);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = RemoteLabels.Text("Exporter la matrice de compatibilité", "Export compatibility matrix"),
                File = new ShareFile(path)
            }).ConfigureAwait(true);
        }
        catch (Exception)
        {
            await DisplayAlertAsync(
                RemoteLabels.Text("Export", "Export"),
                RemoteLabels.Text("Impossible d’exporter la matrice sur cet appareil.", "Unable to export the matrix on this device."),
                "OK").ConfigureAwait(true);
        }
        finally { SetEnabled(true); }
    }

    private void SetEnabled(bool enabled)
    {
        refreshButton.IsEnabled = enabled;
        csvButton.IsEnabled = enabled;
        jsonButton.IsEnabled = enabled;
        profilesHost.IsEnabled = enabled;
    }
}
