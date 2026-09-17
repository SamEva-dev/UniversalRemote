using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

public sealed partial class PlaybackTargetPage : ContentPage
{
    private readonly MediaNavigationState navigation;
    private readonly IPlaybackTargetDiscovery discovery;
    private readonly IPlaybackTargetSelection selection;
    private readonly IPlaybackService playback;
    private readonly IMediaAccessPolicy access;
    private readonly IMediaProfileService profiles;
    private TargetRow? selected;
    private bool loading;

    public PlaybackTargetPage(
        MediaNavigationState navigation,
        IPlaybackTargetDiscovery discovery,
        IPlaybackTargetSelection selection,
        IPlaybackService playback,
        IMediaAccessPolicy access,
        IMediaProfileService profiles)
    {
        InitializeComponent();
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        this.discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        this.selection = selection ?? throw new ArgumentNullException(nameof(selection));
        this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
        this.access = access ?? throw new ArgumentNullException(nameof(access));
        this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

        refreshButton.Clicked += async (_, _) => await RefreshAsync();
        targetList.SelectionChanged += OnSelectionChanged;
        playButton.Clicked += async (_, _) => await StartAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var entry = navigation.Current;
        mediaLabel.Text = entry is null ? "Aucun média sélectionné." : $"{entry.Item.Title} • choisissez la destination";
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (loading) return;
        loading = true;
        refreshButton.IsEnabled = false;
        statusLabel.Text = "Recherche sur le réseau local…";
        try
        {
            var candidates = await discovery.DiscoverAsync();
            var rows = candidates.Select(TargetRow.From).ToArray();
            targetList.ItemsSource = rows;
            statusLabel.Text = rows.Length == 1
                ? "1 destination disponible"
                : $"{rows.Length} destinations détectées";

            var profile = await profiles.GetActiveAsync();
            var preferredTargetId = profile.Preferences.PreferredPlaybackTargetId;
            var current = (!string.IsNullOrWhiteSpace(preferredTargetId)
                              ? rows.FirstOrDefault(x => x.Target.Id == preferredTargetId)
                              : null)
                          ?? rows.FirstOrDefault(x => x.Target.Id == selection.Selected.Id)
                          ?? rows.FirstOrDefault(x => x.CanLaunch);
            targetList.SelectedItem = current;
        }
        catch (Exception)
        {
            targetList.ItemsSource = Array.Empty<TargetRow>();
            statusLabel.Text = "Impossible d’effectuer la découverte réseau.";
        }
        finally
        {
            loading = false;
            refreshButton.IsEnabled = true;
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        selected = e.CurrentSelection.FirstOrDefault() as TargetRow;
        if (selected is not null) selection.Select(selected.Target);
        playButton.IsEnabled = selected?.CanLaunch == true && navigation.Current is not null;
        playButton.Text = selected?.Target.Kind == PlaybackTargetKind.LocalDevice
            ? "Regarder sur ce téléphone"
            : selected?.CanLaunch == true
                ? $"Regarder sur {selected.Name}"
                : "Destination détectée — diffusion indisponible";
    }

    private async Task StartAsync()
    {
        if (selected?.CanLaunch != true || navigation.Current is not { } entry) return;
        var decision = await access.EvaluateAsync(entry.Item);
        if (!decision.IsAllowed)
        {
            await DisplayAlertAsync("Profil Media", decision.UserMessage ?? "Ce contenu est bloqué pour le profil actif.", "OK");
            return;
        }
        playButton.IsEnabled = false;
        try
        {
            var session = await playback.StartAsync(entry.Source, entry.Item, selected.Target);
            if (session.State == PlaybackSessionState.Failed)
            {
                await DisplayAlertAsync("Lecture", session.UserMessage ?? "Impossible de démarrer la lecture.", "OK");
                return;
            }
            await Shell.Current.GoToAsync("//media-player");
        }
        catch (NotSupportedException)
        {
            await DisplayAlertAsync("Destination", "Cette destination est détectée mais son transport de lecture n’est pas encore disponible.", "OK");
        }
        catch (Exception)
        {
            await DisplayAlertAsync("Lecture", "Impossible de démarrer la lecture sur cette destination.", "OK");
        }
        finally
        {
            playButton.IsEnabled = selected?.CanLaunch == true;
        }
    }

    private sealed class TargetRow
    {
        public required IPlaybackTarget Target { get; init; }
        public required bool CanLaunch { get; init; }
        public required string Name { get; init; }
        public required string Status { get; init; }
        public required string Icon { get; init; }
        public required string Capabilities { get; init; }
        public string Availability => CanLaunch ? "Prêt" : "Preview";

        public static TargetRow From(PlaybackTargetCandidate candidate)
            => new()
            {
                Target = candidate.Target,
                CanLaunch = candidate.CanLaunch,
                Name = candidate.Target.DisplayName,
                Status = candidate.Status,
                Icon = candidate.Target.Kind switch
                {
                    PlaybackTargetKind.LocalDevice => "📱",
                    PlaybackTargetKind.AndroidTv => "📺",
                    PlaybackTargetKind.Cast => "◉",
                    _ => "▣"
                },
                Capabilities = candidate.Target.Capabilities.Count == 0
                    ? "Aucune capability de lecture directe"
                    : string.Join(" • ", candidate.Target.Capabilities.Select(FormatCapability))
            };

        private static string FormatCapability(PlaybackCapability capability) => capability switch
        {
            PlaybackCapability.Start => "Lecture",
            PlaybackCapability.Stop => "Stop",
            PlaybackCapability.Pause => "Pause",
            PlaybackCapability.Resume => "Reprise",
            PlaybackCapability.Seek => "Seek",
            PlaybackCapability.SelectAudioTrack => "Audio",
            PlaybackCapability.SelectSubtitleTrack => "Sous-titres",
            PlaybackCapability.ChangeQuality => "Qualité",
            _ => capability.ToString()
        };
    }
}
