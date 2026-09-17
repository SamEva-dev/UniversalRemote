using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

public partial class MediaCatalogPage : ContentPage
{
    private readonly IMediaBrowseService browse;
    private readonly MediaNavigationState navigation;
    private readonly MediaItemKind kind;
    private bool loading;

    protected MediaCatalogPage(
        IMediaBrowseService browse,
        MediaNavigationState navigation,
        MediaItemKind kind,
        string title,
        string subtitle)
    {
        InitializeComponent();
        this.browse = browse ?? throw new ArgumentNullException(nameof(browse));
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        this.kind = kind;
        Title = title;
        titleLabel.Text = title;
        subtitleLabel.Text = subtitle;
        emptyTitleLabel.Text = $"Aucun contenu « {title} »";
        refreshButton.Clicked += async (_, _) => await RefreshAsync();
        catalogList.SelectionChanged += OnSelectionChanged;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateResponsiveLayout();
        await RefreshAsync();
    }

    private void UpdateResponsiveLayout()
    {
        if (kind == MediaItemKind.LiveChannel)
        {
            catalogLayout.Span = Width >= 920 ? 2 : 1;
            return;
        }

        catalogLayout.Span = Width switch
        {
            >= 1080 => 4,
            >= 760 => 3,
            _ => 2
        };
    }

    private async Task RefreshAsync()
    {
        if (loading) return;
        loading = true;
        refreshButton.IsEnabled = false;
        statusLabel.Text = "Chargement du catalogue…";
        try
        {
            var result = await browse.BrowseAsync([kind]);
            catalogList.ItemsSource = result.Items.Select(x => new CatalogCard(x)).ToArray();
            statusLabel.Text = result.FailedSourceCount == 0
                ? $"{result.Items.Count} élément(s) • {result.LoadedSourceCount} source(s)"
                : $"{result.Items.Count} élément(s) • {result.FailedSourceCount} source(s) indisponible(s)";
        }
        catch (Exception)
        {
            catalogList.ItemsSource = null;
            statusLabel.Text = "Le catalogue n’a pas pu être chargé. Réessayez.";
        }
        finally
        {
            refreshButton.IsEnabled = true;
            loading = false;
        }
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not CatalogCard card) return;
        catalogList.SelectedItem = null;
        navigation.Select(card.Entry);
        await Shell.Current.GoToAsync("//media-details");
    }

    private sealed class CatalogCard
    {
        public MediaCatalogEntry Entry { get; }
        public string Title => Entry.Item.Title;
        public string Initial => string.IsNullOrWhiteSpace(Title) ? "•" : Title[..1].ToUpperInvariant();
        public ImageSource? Artwork => Entry.Item.ArtworkUri is { } uri ? ImageSource.FromUri(uri) : null;
        public bool HasArtwork => Entry.Item.ArtworkUri is not null;
        public string KindText => MediaText.Kind(Entry.Item.Kind);
        public string Subtitle => string.Join(" • ", new[] { Entry.Item.Category, Entry.Source.DisplayName, MediaText.Duration(Entry.Item.Duration) }.Where(x => !string.IsNullOrWhiteSpace(x)));
        public CatalogCard(MediaCatalogEntry entry) => Entry = entry;
    }
}

public sealed class LiveTvPage : MediaCatalogPage
{
    public LiveTvPage(IMediaBrowseService browse, MediaNavigationState navigation)
        : base(browse, navigation, MediaItemKind.LiveChannel, "TV en direct", "Chaînes disponibles dans vos sources") { }
}

public sealed class MoviesPage : MediaCatalogPage
{
    public MoviesPage(IMediaBrowseService browse, MediaNavigationState navigation)
        : base(browse, navigation, MediaItemKind.Movie, "Films", "Catalogue VOD normalisé") { }
}

public sealed class SeriesPage : MediaCatalogPage
{
    public SeriesPage(IMediaBrowseService browse, MediaNavigationState navigation)
        : base(browse, navigation, MediaItemKind.Series, "Séries", "Séries disponibles dans vos sources") { }
}
