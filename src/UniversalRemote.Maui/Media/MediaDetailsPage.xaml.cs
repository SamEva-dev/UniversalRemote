using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

public sealed partial class MediaDetailsPage : ContentPage
{
    private readonly MediaNavigationState navigation;
    private readonly IMediaLibraryService library;
    private readonly IMediaSeriesService series;
    private readonly IMediaAccessPolicy access;
    private MediaCatalogEntry? current;
    private bool isFavorite;

    public MediaDetailsPage(
        MediaNavigationState navigation,
        IMediaLibraryService library,
        IMediaSeriesService series,
        IMediaAccessPolicy access)
    {
        InitializeComponent();
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        this.library = library ?? throw new ArgumentNullException(nameof(library));
        this.series = series ?? throw new ArgumentNullException(nameof(series));
        this.access = access ?? throw new ArgumentNullException(nameof(access));

        playButton.Clicked += async (_, _) => await PlayAsync();
        favoriteButton.Clicked += async (_, _) => await ToggleFavoriteAsync();
        guideButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//tv-guide");
        scenarioButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("//media-activities");
        episodeList.SelectionChanged += OnEpisodeSelected;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        current = navigation.Current;
        await RenderAsync();
    }

    private async Task RenderAsync()
    {
        var entry = current;
        if (entry is null)
        {
            titleLabel.Text = "Aucun média sélectionné";
            metadataLabel.Text = "Revenez au catalogue et sélectionnez un contenu.";
            playButton.IsEnabled = false;
            favoriteButton.IsEnabled = false;
            scenarioButton.IsEnabled = false;
            resumeCard.IsVisible = false;
            return;
        }

        var decision = await access.EvaluateAsync(entry.Item);
        if (!decision.IsAllowed)
        {
            titleLabel.Text = "Contenu restreint";
            metadataLabel.Text = decision.UserMessage ?? "Ce contenu est bloqué pour le profil actif.";
            sourceLabel.Text = string.Empty;
            resumeLabel.Text = string.Empty;
            resumeCard.IsVisible = false;
            playButton.IsEnabled = false;
            favoriteButton.IsEnabled = false;
            scenarioButton.IsEnabled = false;
            guideButton.IsVisible = false;
            seriesSection.IsVisible = false;
            return;
        }

        scenarioButton.IsEnabled = true;
        var details = await library.GetDetailsAsync(entry.Source, entry.Item);
        isFavorite = details.IsFavorite;
        titleLabel.Text = details.Item.Title;
        heroInitial.Text = details.Item.Title[..1].ToUpperInvariant();
        metadataLabel.Text = string.Join(" • ", new[]
        {
            MediaText.Kind(details.Item.Kind),
            details.Item.Category,
            MediaText.Duration(details.Item.Duration)
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        sourceLabel.Text = $"Source : {details.Source.DisplayName}";
        resumeLabel.Text = details.History is { Completed: false } history && history.Position > TimeSpan.Zero
            ? $"Reprise : {MediaText.Progress(history.Position, history.Duration)}"
            : "Lecture disponible depuis le début";
        resumeCard.IsVisible = details.Item.Kind != MediaItemKind.Series;
        favoriteButton.Text = isFavorite ? "Retirer des favoris" : "Ajouter aux favoris";
        guideButton.IsVisible = details.Item.Kind == MediaItemKind.LiveChannel;
        seriesSection.IsVisible = details.Item.Kind == MediaItemKind.Series;
        playButton.IsVisible = details.Item.Kind != MediaItemKind.Series;
        playButton.IsEnabled = details.Item.Kind != MediaItemKind.Series;

        if (details.Item.ArtworkUri is { } artwork)
        {
            artworkImage.Source = ImageSource.FromUri(artwork);
            heroInitial.IsVisible = false;
        }
        else
        {
            artworkImage.Source = null;
            heroInitial.IsVisible = true;
        }

        if (details.Item.Kind == MediaItemKind.Series)
            await LoadSeriesAsync(details.Source, details.Item);
        else
            episodeList.ItemsSource = null;
    }

    private async Task LoadSeriesAsync(MediaSource source, MediaItem item)
    {
        seriesLoading.IsVisible = true;
        seriesLoading.IsRunning = true;
        seriesStatusLabel.Text = "Chargement des saisons…";
        try
        {
            var details = await series.GetSeriesAsync(source, item);
            if (details is null)
            {
                episodeList.ItemsSource = null;
                seriesStatusLabel.Text = "Cette source ne fournit pas le détail des épisodes.";
                return;
            }

            var rows = details.Seasons
                .SelectMany(season => season.Episodes.Select(episode => new EpisodeRow(
                    new MediaCatalogEntry(source, new MediaItem(
                        source.Id,
                        episode.ExternalId,
                        MediaItemKind.Episode,
                        episode.Title,
                        $"Saison {episode.SeasonNumber}",
                        episode.Duration,
                        episode.ArtworkUri)),
                    episode.SeasonNumber,
                    episode.EpisodeNumber)))
                .ToArray();
            episodeList.ItemsSource = rows;
            seriesStatusLabel.Text = $"{details.Seasons.Count} saison(s) • {rows.Length} épisode(s)";
        }
        catch (Exception)
        {
            episodeList.ItemsSource = null;
            seriesStatusLabel.Text = "Les épisodes n’ont pas pu être chargés.";
        }
        finally
        {
            seriesLoading.IsRunning = false;
            seriesLoading.IsVisible = false;
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        if (current is null) return;
        try
        {
            isFavorite = await library.ToggleFavoriteAsync(current.Item);
            favoriteButton.Text = isFavorite ? "Retirer des favoris" : "Ajouter aux favoris";
        }
        catch (Exception)
        {
            await DisplayAlertAsync("Favoris", "Impossible de modifier les favoris Media.", "OK");
        }
    }

    private async Task PlayAsync()
    {
        if (current is null) return;
        await StartAsync(current);
    }

    private async void OnEpisodeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not EpisodeRow row) return;
        episodeList.SelectedItem = null;
        current = row.Entry;
        navigation.Select(row.Entry);
        await StartAsync(row.Entry);
    }

    private async Task StartAsync(MediaCatalogEntry entry)
    {
        navigation.Select(entry);
        await Shell.Current.GoToAsync("//playback-targets");
    }

    private sealed class EpisodeRow
    {
        public MediaCatalogEntry Entry { get; }
        public string Title => Entry.Item.Title;
        public string Subtitle { get; }
        public EpisodeRow(MediaCatalogEntry entry, int seasonNumber, int episodeNumber)
        {
            Entry = entry;
            Subtitle = $"S{seasonNumber:00} E{episodeNumber:00} • {MediaText.Duration(entry.Item.Duration)}";
        }
    }
}
