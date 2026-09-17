using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

public sealed partial class MediaLibraryPage : ContentPage
{
    private readonly IMediaLibraryService library;
    private readonly MediaNavigationState navigation;
    private bool loading;

    public MediaLibraryPage(IMediaLibraryService library, MediaNavigationState navigation)
    {
        InitializeComponent();
        this.library = library ?? throw new ArgumentNullException(nameof(library));
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        refreshButton.Clicked += async (_, _) => await RefreshAsync();
        favoritesList.SelectionChanged += OnFavoriteSelected;
        historyList.SelectionChanged += OnHistorySelected;
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
            var favorites = await library.GetFavoritesAsync();
            var history = await library.GetHistoryAsync(100);
            favoritesList.ItemsSource = favorites.Select(x => new FavoriteRow(x)).ToArray();
            historyList.ItemsSource = history.Select(x => new HistoryRow(x)).ToArray();
        }
        catch (Exception)
        {
            await DisplayAlertAsync("Bibliothèque Media", "Impossible de charger les favoris ou l’historique.", "OK");
        }
        finally
        {
            refreshButton.IsEnabled = true;
            loading = false;
        }
    }

    private async void OnFavoriteSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not FavoriteRow row) return;
        favoritesList.SelectedItem = null;
        await OpenAsync(row.Item.Reference);
    }

    private async void OnHistorySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not HistoryRow row) return;
        historyList.SelectedItem = null;
        await OpenAsync(row.Item.Reference);
    }

    private async Task OpenAsync(MediaReference reference)
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

    private sealed class FavoriteRow
    {
        public MediaFavorite Item { get; }
        public string Title => Item.Title;
        public string Subtitle => $"{MediaText.Kind(Item.Reference.Kind)} • {Item.Category ?? "Sans catégorie"}";
        public FavoriteRow(MediaFavorite item) => Item = item;
    }

    private sealed class HistoryRow
    {
        public MediaHistoryEntry Item { get; }
        public string Title => Item.Title;
        public string Subtitle => Item.Completed ? $"{MediaText.Kind(Item.Reference.Kind)} • terminé" : $"{MediaText.Kind(Item.Reference.Kind)} • {MediaText.Progress(Item.Position, Item.Duration)}";
        public string When => Item.LastWatchedAt.ToLocalTime().ToString("dd/MM HH:mm");
        public HistoryRow(MediaHistoryEntry item) => Item = item;
    }
}
