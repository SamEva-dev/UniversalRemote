using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

/// <summary>
/// UI shell for MEDIA 4. Source selection is intentionally left to MEDIA 5 catalogue UX; this page already
/// renders a provider-independent EpgGuideSnapshot and can be fed by that flow without knowing M3U/Xtream/XMLTV.
/// </summary>
public sealed partial class TvGuidePage : ContentPage
{
    private readonly IEpgGuideService guideService;
    private MediaSource? mediaSource;
    private EpgSource? epgSource;

    public TvGuidePage(IEpgGuideService guideService)
    {
        InitializeComponent();
        this.guideService = guideService ?? throw new ArgumentNullException(nameof(guideService));
        refreshButton.Clicked += async (_, _) => await RefreshAsync(forceRefresh: true);
    }

    public async Task ShowAsync(MediaSource source, EpgSource guideSource, CancellationToken cancellationToken = default)
    {
        mediaSource = source ?? throw new ArgumentNullException(nameof(source));
        epgSource = guideSource ?? throw new ArgumentNullException(nameof(guideSource));
        await RefreshAsync(forceRefresh: false, cancellationToken);
    }

    private async Task RefreshAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        if (mediaSource is null || epgSource is null)
        {
            statusLabel.Text = "Configurez une source Media puis une source XMLTV pour afficher le guide.";
            guideList.ItemsSource = null;
            return;
        }

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
            statusLabel.Text = $"{matched}/{guide.Channels.Count} chaînes associées • cache local actif";
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
        }
    }

    private sealed record GuideRowViewModel(string ChannelTitle, string MatchText, IReadOnlyList<ProgramViewModel> Programs);
    private sealed record ProgramViewModel(string Title, string TimeRange, string Category);
}
