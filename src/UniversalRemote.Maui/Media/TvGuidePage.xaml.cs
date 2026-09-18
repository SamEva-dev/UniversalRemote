using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

/// <summary>Provider-independent TV guide. The page resolves the first configured Media+EPG pair when opened directly.</summary>
public sealed partial class TvGuidePage : ContentPage
{
    private readonly IEpgGuideService guideService;
    private readonly IMediaSourceRepository sources;
    private readonly IEpgSourceRepository epgSources;
    private MediaSource? mediaSource;
    private EpgSource? epgSource;
    private bool loading;

    public TvGuidePage(
        IEpgGuideService guideService,
        IMediaSourceRepository sources,
        IEpgSourceRepository epgSources)
    {
        InitializeComponent();
        this.guideService = guideService ?? throw new ArgumentNullException(nameof(guideService));
        this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
        this.epgSources = epgSources ?? throw new ArgumentNullException(nameof(epgSources));
        refreshButton.Clicked += async (_, _) => await ResolveAndRefreshAsync(forceRefresh: true);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ResolveAndRefreshAsync(forceRefresh: false);
    }

    public async Task ShowAsync(MediaSource source, EpgSource guideSource, CancellationToken cancellationToken = default)
    {
        mediaSource = source ?? throw new ArgumentNullException(nameof(source));
        epgSource = guideSource ?? throw new ArgumentNullException(nameof(guideSource));
        await RefreshAsync(forceRefresh: false, cancellationToken);
    }

    private async Task ResolveAndRefreshAsync(bool forceRefresh)
    {
        if (loading) return;
        mediaSource = null;
        epgSource = null;

        var configured = await sources.ListAsync();
        foreach (var source in configured.Where(x => x.IsEnabled))
        {
            var guides = await epgSources.ListForMediaSourceAsync(source.Id);
            var guide = guides.FirstOrDefault(x => x.IsEnabled);
            if (guide is null) continue;
            mediaSource = source;
            epgSource = guide;
            break;
        }

        await RefreshAsync(forceRefresh);
    }

    private async Task RefreshAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        if (loading) return;
        if (mediaSource is null || epgSource is null)
        {
            statusLabel.Text = "Aucun couple Media + XMLTV configuré. Ouvrez Média > Sources.";
            guideList.ItemsSource = null;
            return;
        }

        loading = true;
        refreshButton.IsEnabled = false;
        statusLabel.Text = "Chargement du guide…";
        try
        {
            var now = DateTimeOffset.Now;
            var request = new EpgGuideRequest(now.AddHours(-1), now.AddHours(12), forceRefresh);
            var guide = await guideService.GetGuideAsync(mediaSource, epgSource, request, cancellationToken);
            windowLabel.Text = $"{guide.WindowStart:ddd HH:mm} → {guide.WindowEnd:ddd HH:mm}";
            var rows = guide.Channels.Select(row => new GuideRowViewModel(
                row.Channel.Title,
                row.MatchedGuideId is null ? "EPG non associé" : "EPG associé",
                row.Programs.Select(program => new ProgramViewModel(
                    program.Title,
                    $"{program.StartsAt.ToLocalTime():HH:mm}–{program.EndsAt.ToLocalTime():HH:mm}",
                    program.Category ?? string.Empty)).ToArray())).ToArray();
            guideList.ItemsSource = rows;
            var matched = guide.Channels.Count(x => x.MatchedGuideId is not null);
            statusLabel.Text = $"{mediaSource.DisplayName} • {matched}/{guide.Channels.Count} chaînes associées • cache local actif";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Chargement annulé.";
        }
        catch (Exception)
        {
            statusLabel.Text = "Le guide n’a pas pu être chargé. Vérifiez la source EPG et réessayez.";
        }
        finally
        {
            refreshButton.IsEnabled = true;
            loading = false;
        }
    }

    private sealed record GuideRowViewModel(string ChannelTitle, string MatchText, IReadOnlyList<ProgramViewModel> Programs);
    private sealed record ProgramViewModel(string Title, string TimeRange, string Category);
}
