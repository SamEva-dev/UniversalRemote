using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Provider.M3U;
using UniversalRemote.Media.Provider.Xtream;

namespace UniversalRemote.Maui.Media;

public sealed partial class MediaSourcesPage : ContentPage
{
    private readonly IMediaSourceRepository sources;
    private readonly IMediaCredentialStore credentials;
    private readonly IMediaCatalog catalog;
    private bool busy;

    public MediaSourcesPage(IMediaSourceRepository sources, IMediaCredentialStore credentials, IMediaCatalog catalog)
    {
        InitializeComponent();
        this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
        this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        typePicker.ItemsSource = new[] { "Xtream — DNS + utilisateur + mot de passe", "M3U / M3U8 — URL de playlist" };
        typePicker.SelectedIndex = 0;
        typePicker.SelectedIndexChanged += (_, _) => UpdateFields();
        sourceList.SelectionChanged += (_, e) => deleteButton.IsEnabled = e.CurrentSelection.FirstOrDefault() is SourceCard;
        saveButton.Clicked += async (_, _) => await SaveAsync();
        deleteButton.Clicked += async (_, _) => await DeleteSelectedAsync();
        refreshButton.Clicked += async (_, _) => await RefreshAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateFields();
        await RefreshAsync();
    }

    private void UpdateFields()
    {
        var xtream = typePicker.SelectedIndex != 1;
        xtreamFields.IsVisible = xtream;
        m3uFields.IsVisible = !xtream;
    }

    private async Task SaveAsync()
    {
        if (busy) return;
        var displayName = (nameEntry.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            await DisplayAlertAsync("Source Media", "Indiquez un nom pour cette source.", "OK");
            return;
        }

        busy = true;
        SetBusy(true);
        var sourceId = Guid.NewGuid();
        var credentialReference = $"source.{sourceId:N}";
        try
        {
            MediaSource source;
            if (typePicker.SelectedIndex == 1)
            {
                var raw = (playlistEntry.Text ?? string.Empty).Trim();
                if (!TryHttpUri(raw, out _))
                    throw new InvalidOperationException("L’URL M3U/M3U8 doit être une adresse HTTP ou HTTPS valide.");

                await credentials.SetAsync(credentialReference, new MediaSecret(raw));
                source = new MediaSource(sourceId, displayName, M3uMediaProvider.ProviderId, credentialReference);
            }
            else
            {
                var server = NormalizeServer((serverEntry.Text ?? string.Empty).Trim());
                var username = (usernameEntry.Text ?? string.Empty).Trim();
                var password = passwordEntry.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException("Le nom d’utilisateur et le mot de passe sont obligatoires.");

                var xtreamCredentials = new XtreamCredentials(server, username, password);
                await credentials.SetAsync(credentialReference, XtreamCredentialCodec.Encode(xtreamCredentials));
                source = new MediaSource(sourceId, displayName, XtreamMediaProvider.ProviderId, credentialReference);
            }

            statusLabel.Text = "Test de connexion…";
            // An empty kind set makes Xtream validate authentication without downloading the full catalogue.
            // M3U still downloads/parses the playlist, which validates the supplied URL.
            await catalog.GetAsync(source, new MediaCatalogRequest(Array.Empty<MediaItemKind>()));
            await sources.SaveAsync(source);

            statusLabel.Text = "✓ Source connectée et enregistrée.";
            ClearForm();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await credentials.DeleteAsync(credentialReference);
            statusLabel.Text = FriendlyError(ex);
        }
        finally
        {
            busy = false;
            SetBusy(false);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (busy || sourceList.SelectedItem is not SourceCard card) return;
        var confirmed = await DisplayAlertAsync("Supprimer la source", $"Supprimer « {card.Name} » ?", "Supprimer", "Annuler");
        if (!confirmed) return;

        busy = true;
        SetBusy(true);
        try
        {
            if (!string.IsNullOrWhiteSpace(card.Source.CredentialReference))
                await credentials.DeleteAsync(card.Source.CredentialReference);
            await sources.DeleteAsync(card.Source.Id);
            sourceList.SelectedItem = null;
            statusLabel.Text = "Source supprimée.";
            await RefreshAsync();
        }
        catch (Exception)
        {
            statusLabel.Text = "Impossible de supprimer cette source.";
        }
        finally
        {
            busy = false;
            SetBusy(false);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var items = await sources.ListAsync();
            sourceList.ItemsSource = items.Select(x => new SourceCard(x)).ToArray();
        }
        catch (Exception)
        {
            statusLabel.Text = "Impossible de charger les sources Media.";
        }
    }

    private void SetBusy(bool value)
    {
        saveButton.IsEnabled = !value;
        refreshButton.IsEnabled = !value;
        deleteButton.IsEnabled = !value && sourceList.SelectedItem is SourceCard;
    }

    private void ClearForm()
    {
        nameEntry.Text = string.Empty;
        serverEntry.Text = string.Empty;
        usernameEntry.Text = string.Empty;
        passwordEntry.Text = string.Empty;
        playlistEntry.Text = string.Empty;
    }

    private static Uri NormalizeServer(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Indiquez le DNS ou l’URL du serveur.");
        var candidate = value.Contains("://", StringComparison.Ordinal) ? value : "http://" + value;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !IsHttp(uri) || string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidOperationException("Le DNS / URL du serveur est invalide.");
        return uri;
    }

    private static bool TryHttpUri(string value, out Uri? uri)
    {
        var ok = Uri.TryCreate(value, UriKind.Absolute, out uri) && uri is not null && IsHttp(uri) && !string.IsNullOrWhiteSpace(uri.Host);
        return ok;
    }

    private static bool IsHttp(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string FriendlyError(Exception exception) => exception switch
    {
        XtreamAuthenticationException => "Connexion refusée : vérifiez le DNS, le nom d’utilisateur et le mot de passe.",
        XtreamApiException => "Le serveur IPTV a répondu, mais son API n’est pas compatible ou est indisponible.",
        M3uPlaylistLoadException => "Impossible de charger la playlist M3U/M3U8.",
        InvalidOperationException invalid => invalid.Message,
        ArgumentException argument => argument.Message,
        _ => "Impossible de connecter cette source. Vérifiez les informations et le réseau."
    };

    private sealed class SourceCard
    {
        public MediaSource Source { get; }
        public string Name => Source.DisplayName;
        public string TypeLabel => Source.ProviderId.Equals(XtreamMediaProvider.ProviderId, StringComparison.OrdinalIgnoreCase)
            ? "Xtream • DNS + identifiants"
            : Source.ProviderId.Equals(M3uMediaProvider.ProviderId, StringComparison.OrdinalIgnoreCase)
                ? "M3U / M3U8"
                : Source.ProviderId;

        public SourceCard(MediaSource source) => Source = source;
    }
}
