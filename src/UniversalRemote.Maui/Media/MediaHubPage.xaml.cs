using UniversalRemote.Abstractions;
using UniversalRemote.Media.Abstractions;
using RemoteDevice = UniversalRemote.Abstractions.Device;

namespace UniversalRemote.Maui.Media;

public sealed partial class MediaHubPage : ContentPage
{
    private readonly IMediaSourceRepository sources;
    private readonly IMediaLibraryService library;
    private readonly MediaNavigationState navigation;
    private readonly IMediaProfileService profiles;
    private readonly IDeviceRepository devices;
    private readonly DeviceSelectionState deviceSelection;
    private bool loading;

    public MediaHubPage(
        IMediaSourceRepository sources,
        IMediaLibraryService library,
        MediaNavigationState navigation,
        IMediaProfileService profiles,
        IDeviceRepository devices,
        DeviceSelectionState deviceSelection)
    {
        InitializeComponent();
        this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
        this.library = library ?? throw new ArgumentNullException(nameof(library));
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        this.devices = devices ?? throw new ArgumentNullException(nameof(devices));
        this.deviceSelection = deviceSelection ?? throw new ArgumentNullException(nameof(deviceSelection));

        controlButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//remote");
        refreshButton.Clicked += async (_, _) => await RefreshAsync();
        profilesButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//media-profiles");
        liveButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//media-live");
        moviesButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//media-movies");
        seriesButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//media-series");
        guideButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//tv-guide");
        searchButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//media-search");
        libraryButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//media-library");
        continueList.SelectionChanged += OnContinueSelected;
        favoriteList.SelectionChanged += OnFavoriteSelected;
        deviceList.SelectionChanged += OnDeviceSelected;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (loading) return;
        loading = true;
        refreshButton.IsEnabled = false;
        try
        {
            var activeProfile = await profiles.GetActiveAsync();
            activeProfileLabel.Text = $"Profil : {activeProfile.DisplayName}";
            var configured = (await sources.ListAsync()).Where(x => x.IsEnabled).ToArray();
            var continueItems = await library.GetContinueWatchingAsync();
            var favorites = await library.GetFavoritesAsync();
            var knownDevices = await devices.ListAsync();

            deviceList.ItemsSource = knownDevices.Select(x => new DeviceCard(x)).ToArray();
            deviceCountLabel.Text = knownDevices.Count.ToString();
            continueList.ItemsSource = continueItems.Select(x => new ContinueCard(x)).ToArray();
            favoriteList.ItemsSource = favorites.Select(x => new FavoriteCard(x)).ToArray();
            continueCountLabel.Text = continueItems.Count.ToString();
            favoriteCountLabel.Text = favorites.Count.ToString();
            statusLabel.Text = configured.Length == 0
                ? "Aucune source Media active. Le catalogue restera vide tant qu’une source n’est pas configurée."
                : $"{configured.Length} source(s) active(s) • favoris et historique stockés localement";
        }
        catch (Exception)
        {
            statusLabel.Text = "Impossible de charger l’accueil Media. Réessayez.";
        }
        finally
        {
            refreshButton.IsEnabled = true;
            loading = false;
        }
    }


    private async void OnDeviceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not DeviceCard card) return;
        deviceList.SelectedItem = null;
        deviceSelection.ActiveDeviceId = card.Device.Id;
        await Shell.Current.GoToAsync("//remote");
    }

    private async void OnContinueSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ContinueCard card) return;
        continueList.SelectedItem = null;
        await OpenReferenceAsync(card.Entry.Reference);
    }

    private async void OnFavoriteSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not FavoriteCard card) return;
        favoriteList.SelectedItem = null;
        await OpenReferenceAsync(card.Item.Reference);
    }

    private async Task OpenReferenceAsync(MediaReference reference)
    {
        var entry = await library.ResolveAsync(reference);
        if (entry is null)
        {
            await DisplayAlertAsync("Média", "Ce contenu n’est plus disponible dans la source actuelle.", "OK");
            return;
        }

        navigation.Select(entry);
        await Shell.Current.GoToAsync("//media-details");
    }

    private sealed class ContinueCard
    {
        public MediaHistoryEntry Entry { get; }
        public string Title => Entry.Title;
        public string KindText => MediaText.Kind(Entry.Reference.Kind);
        public double Progress => Entry.Progress;
        public string ProgressText => MediaText.Progress(Entry.Position, Entry.Duration);
        public ContinueCard(MediaHistoryEntry entry) => Entry = entry;
    }

    private sealed class FavoriteCard
    {
        public MediaFavorite Item { get; }
        public string Title => Item.Title;
        public string Subtitle => string.IsNullOrWhiteSpace(Item.Category) ? MediaText.Kind(Item.Reference.Kind) : Item.Category!;
        public FavoriteCard(MediaFavorite item) => Item = item;
    }
    private sealed class DeviceCard
    {
        public RemoteDevice Device { get; }
        public string Name => Device.DisplayName;
        public string Icon => "📺";
        public string ProviderText => string.Join(" • ", Device.Routes.Select(x => x.ProviderId).Distinct(StringComparer.Ordinal).Take(2));
        public string CapabilityText => $"{Device.Capabilities.Count} commande(s)";

        public DeviceCard(RemoteDevice device) => Device = device;
    }

}
