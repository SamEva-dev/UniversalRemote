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
            var usedHttpFallback = false;
            var usedM3uCompatibility = false;

            if (typePicker.SelectedIndex == 1)
            {
                var raw = (playlistEntry.Text ?? string.Empty).Trim();
                if (!TryHttpUri(raw, out _))
                    throw new InvalidOperationException("L’URL M3U/M3U8 doit être une adresse HTTP ou HTTPS valide.");

                await credentials.SetAsync(credentialReference, new MediaSecret(raw));
                source = new MediaSource(sourceId, displayName, M3uMediaProvider.ProviderId, credentialReference);
                statusLabel.Text = "Test de la playlist…";
                await catalog.GetAsync(source, new MediaCatalogRequest(Array.Empty<MediaItemKind>()));
            }
            else
            {
                var server = NormalizeServer((serverEntry.Text ?? string.Empty).Trim());
                var username = (usernameEntry.Text ?? string.Empty).Trim();
                var password = passwordEntry.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException("Le nom d’utilisateur et le mot de passe sont obligatoires.");

                var effectiveCredentials = new XtreamCredentials(server, username, password);
                source = new MediaSource(sourceId, displayName, XtreamMediaProvider.ProviderId, credentialReference);
                await credentials.SetAsync(credentialReference, XtreamCredentialCodec.Encode(effectiveCredentials));

                statusLabel.Text = "Test de l’API Xtream…";
                XtreamApiException? apiError = null;
                try
                {
                    await catalog.GetAsync(source, new MediaCatalogRequest(Array.Empty<MediaItemKind>()));
                }
                catch (XtreamApiException ex)
                {
                    apiError = ex;
                }

                // A 403 means the host is reachable but explicitly refuses player_api.php.
                // Many legitimate Xtream-compatible services still expose the standard get.php M3U export.
                // Offer that documented compatibility path without impersonating another IPTV application.
                if (apiError?.StatusCode is System.Net.HttpStatusCode.Forbidden)
                {
                    var tryM3u = await DisplayAlertAsync(
                        "API Xtream bloquée",
                        "Le serveur refuse player_api.php (HTTP 403). UniversalRemote peut tester l’export M3U standard du même compte. Les chaînes et catégories resteront disponibles, mais les Films/Séries peuvent être moins structurés qu’avec l’API Xtream.",
                        "Tester en M3U",
                        "Annuler");

                    if (tryM3u)
                    {
                        statusLabel.Text = "Test de compatibilité M3U…";
                        var playlistUri = XtreamM3uCompatibility.BuildM3uPlusPlaylistUri(effectiveCredentials);
                        await credentials.SetAsync(credentialReference, new MediaSecret(playlistUri.AbsoluteUri));
                        source = new MediaSource(sourceId, displayName, M3uMediaProvider.ProviderId, credentialReference);
                        await catalog.GetAsync(source, new MediaCatalogRequest([MediaItemKind.LiveChannel]));
                        usedM3uCompatibility = true;
                        apiError = null;
                    }
                }

                if (apiError is not null && effectiveCredentials.UsesTls)
                {
                    var retryHttp = await DisplayAlertAsync(
                        "Compatibilité IPTV",
                        $"La connexion HTTPS a échoué : {FriendlyXtreamApiError(apiError)}\n\nEssayer la même adresse en HTTP ? Attention : HTTP n’est pas chiffré ; utilisez-le uniquement si votre fournisseur vous a donné une adresse HTTP.",
                        "Essayer HTTP",
                        "Annuler");
                    if (!retryHttp) throw apiError;

                    effectiveCredentials = new XtreamCredentials(ToHttp(effectiveCredentials.ServerBaseUri), username, password);
                    await credentials.SetAsync(credentialReference, XtreamCredentialCodec.Encode(effectiveCredentials));
                    source = new MediaSource(sourceId, displayName, XtreamMediaProvider.ProviderId, credentialReference);
                    usedHttpFallback = true;

                    statusLabel.Text = "Nouveau test en HTTP…";
                    try
                    {
                        await catalog.GetAsync(source, new MediaCatalogRequest(Array.Empty<MediaItemKind>()));
                        apiError = null;
                    }
                    catch (XtreamApiException ex)
                    {
                        apiError = ex;
                    }

                    if (apiError?.StatusCode is System.Net.HttpStatusCode.Forbidden)
                    {
                        var tryM3uHttp = await DisplayAlertAsync(
                            "API Xtream bloquée",
                            "Le serveur HTTP refuse aussi player_api.php (403). Tester l’export M3U standard avec les mêmes identifiants ?",
                            "Tester en M3U",
                            "Annuler");
                        if (tryM3uHttp)
                        {
                            statusLabel.Text = "Test M3U en HTTP…";
                            var playlistUri = XtreamM3uCompatibility.BuildM3uPlusPlaylistUri(effectiveCredentials);
                            await credentials.SetAsync(credentialReference, new MediaSecret(playlistUri.AbsoluteUri));
                            source = new MediaSource(sourceId, displayName, M3uMediaProvider.ProviderId, credentialReference);
                            await catalog.GetAsync(source, new MediaCatalogRequest([MediaItemKind.LiveChannel]));
                            usedM3uCompatibility = true;
                            apiError = null;
                        }
                    }
                }

                if (apiError is not null) throw apiError;
            }

            await sources.SaveAsync(source);

            statusLabel.Text = usedM3uCompatibility
                ? usedHttpFallback
                    ? "✓ Source enregistrée en compatibilité M3U via HTTP (connexion non chiffrée)."
                    : "✓ Source enregistrée en compatibilité M3U."
                : usedHttpFallback
                    ? "✓ Source Xtream connectée en HTTP (connexion non chiffrée)."
                    : "✓ Source connectée et enregistrée.";
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

    private bool IsHttpsServerInput()
    {
        try
        {
            var candidate = NormalizeServer((serverEntry.Text ?? string.Empty).Trim());
            return candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static Uri ToHttp(Uri httpsServer)
    {
        var builder = new UriBuilder(httpsServer) { Scheme = Uri.UriSchemeHttp };
        if (httpsServer.IsDefaultPort) builder.Port = -1;
        return builder.Uri;
    }

    private static string FriendlyError(Exception exception) => exception switch
    {
        XtreamAuthenticationException => "Connexion refusée : vérifiez le DNS, le nom d’utilisateur et le mot de passe.",
        XtreamApiException api => FriendlyXtreamApiError(api),
        M3uPlaylistLoadException { StatusCode: System.Net.HttpStatusCode.Forbidden } => "Accès M3U bloqué par le serveur (HTTP 403). Vérifiez surtout le DNS, le port et le protocole exacts fournis par votre service.",
        M3uPlaylistLoadException { StatusCode: System.Net.HttpStatusCode.Unauthorized } => "Accès M3U refusé (HTTP 401). Vérifiez vos identifiants.",
        M3uPlaylistLoadException => "Impossible de charger la playlist M3U/M3U8.",
        InvalidOperationException invalid => invalid.Message,
        ArgumentException argument => argument.Message,
        _ => "Impossible de connecter cette source. Vérifiez les informations et le réseau."
    };

    private static string FriendlyXtreamApiError(XtreamApiException error)
    {
        if (error.FailureKind == XtreamApiFailureKind.Network)
            return "Serveur IPTV injoignable. Vérifiez l’adresse, le port, Internet et le protocole HTTP/HTTPS.";
        if (error.FailureKind == XtreamApiFailureKind.Timeout)
            return "Le serveur IPTV ne répond pas assez vite. Vérifiez l’adresse et réessayez.";
        if (error.FailureKind == XtreamApiFailureKind.Redirect)
            return "Le serveur redirige l’API vers une autre adresse. Utilisez l’URL finale exacte fournie par votre service.";
        if (error.FailureKind == XtreamApiFailureKind.InvalidJson)
            return "Le serveur répond, mais renvoie une page web au lieu de l’API Xtream. Vérifiez surtout http/https, le port et l’URL du portail.";
        if (error.FailureKind == XtreamApiFailureKind.InvalidPayload)
            return "Le serveur répond en JSON, mais pas au format Xtream attendu. Vérifiez que vos identifiants sont bien de type Xtream Codes.";
        if (error.StatusCode is System.Net.HttpStatusCode.NotFound)
            return "API Xtream introuvable (HTTP 404). Vérifiez le DNS/URL et le port exacts fournis par votre service.";
        if (error.StatusCode is System.Net.HttpStatusCode.Unauthorized)
            return "Accès refusé (HTTP 401). Vérifiez le nom d’utilisateur et le mot de passe.";
        if (error.StatusCode is System.Net.HttpStatusCode.Forbidden)
            return "Accès bloqué par le serveur (HTTP 403). Vérifiez l’URL exacte ou une éventuelle protection du portail.";
        if (error.StatusCode is not null)
            return $"Le serveur IPTV a répondu avec HTTP {(int)error.StatusCode.Value}. Vérifiez le DNS/URL, le port et le protocole indiqué par votre service.";
        return "Le serveur IPTV a répondu, mais son API Xtream n’est pas exploitable.";
    }

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
