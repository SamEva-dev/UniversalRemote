using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

public sealed partial class MediaSearchPage : ContentPage
{
    private readonly IMediaSearchService search;
    private readonly MediaNavigationState navigation;
    private bool loading;

    public MediaSearchPage(IMediaSearchService search, MediaNavigationState navigation)
    {
        InitializeComponent();
        this.search = search ?? throw new ArgumentNullException(nameof(search));
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        searchButton.Clicked += async (_, _) => await SearchAsync();
        searchBar.SearchButtonPressed += async (_, _) => await SearchAsync();
        resultsList.SelectionChanged += OnSelectionChanged;
    }

    private async Task SearchAsync()
    {
        var query = searchBar.Text?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            resultsList.ItemsSource = null;
            emptyLabel.Text = "Saisissez au moins 2 caractères.";
            return;
        }
        if (loading) return;

        loading = true;
        searchButton.IsEnabled = false;
        emptyLabel.Text = "Recherche en cours…";
        try
        {
            var result = await search.SearchAsync(query, maxResults: 100);
            resultsList.ItemsSource = result.Items.Select(x => new SearchCard(x)).ToArray();
            emptyLabel.Text = result.Items.Count == 0 ? "Aucun résultat." : string.Empty;
        }
        catch (Exception)
        {
            resultsList.ItemsSource = null;
            emptyLabel.Text = "La recherche n’a pas pu être effectuée.";
        }
        finally
        {
            searchButton.IsEnabled = true;
            loading = false;
        }
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchCard card) return;
        resultsList.SelectedItem = null;
        navigation.Select(card.Entry);
        await Shell.Current.GoToAsync("//media-details");
    }

    private sealed class SearchCard
    {
        public MediaCatalogEntry Entry { get; }
        public string Title => Entry.Item.Title;
        public string Subtitle => $"{MediaText.Kind(Entry.Item.Kind)} • {Entry.Item.Category ?? "Sans catégorie"} • {Entry.Source.DisplayName}";
        public SearchCard(MediaCatalogEntry entry) => Entry = entry;
    }
}
