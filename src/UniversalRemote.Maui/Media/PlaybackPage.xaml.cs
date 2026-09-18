using Microsoft.Maui.ApplicationModel;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Control;

namespace UniversalRemote.Maui.Media;

public sealed partial class PlaybackPage : ContentPage
{
    private readonly IPlaybackService playback;
    private readonly IPlaybackTargetSelection targetSelection;
    private readonly IMediaRemoteControl mediaRemote;
    private readonly DeviceSelectionState deviceSelection;
    private bool dragging;
    private MediaRemoteState? remoteState;

    public PlaybackPage(
        IPlaybackService playback,
        MauiMediaElementPlaybackEngine engine,
        IPlaybackTargetSelection targetSelection,
        IMediaRemoteControl mediaRemote,
        DeviceSelectionState deviceSelection)
    {
        InitializeComponent();
        this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
        this.targetSelection = targetSelection ?? throw new ArgumentNullException(nameof(targetSelection));
        this.mediaRemote = mediaRemote ?? throw new ArgumentNullException(nameof(mediaRemote));
        this.deviceSelection = deviceSelection ?? throw new ArgumentNullException(nameof(deviceSelection));
        ArgumentNullException.ThrowIfNull(engine);
        engine.Attach(player);

        playback.SessionChanged += OnSessionChanged;
        timeline.DragStarted += (_, _) => dragging = true;
        timeline.DragCompleted += async (_, _) =>
        {
            dragging = false;
            if (playback.CurrentSession is { MediaKind: not MediaItemKind.LiveChannel })
                await RunSafelyAsync(() => playback.SeekAsync(TimeSpan.FromSeconds(timeline.Value)));
        };
        playButton.Clicked += async (_, _) => await RunSafelyAsync(() => playback.PlayAsync());
        pauseButton.Clicked += async (_, _) => await RunSafelyAsync(() => playback.PauseAsync());
        stopButton.Clicked += async (_, _) => await RunSafelyAsync(() => playback.StopAsync());
        backButton.Clicked += async (_, _) => await SeekRelativeAsync(TimeSpan.FromSeconds(-10));
        forwardButton.Clicked += async (_, _) => await SeekRelativeAsync(TimeSpan.FromSeconds(30));

        WireRemote(volumeDownButton, RemoteActions.VolumeDown);
        WireRemote(muteButton, RemoteActions.MuteToggle);
        WireRemote(volumeUpButton, RemoteActions.VolumeUp);
        WireRemote(upButton, RemoteActions.Up);
        WireRemote(leftButton, RemoteActions.Left);
        WireRemote(okButton, RemoteActions.Ok);
        WireRemote(rightButton, RemoteActions.Right);
        WireRemote(downButton, RemoteActions.Down);
        WireRemote(backRemoteButton, RemoteActions.Back);
        WireRemote(homeButton, RemoteActions.Home);
        WireRemote(playPauseRemoteButton, RemoteActions.PlayPause);
        WireRemote(guideButton, RemoteActions.Guide);
        fullRemoteButton.Clicked += async (_, _) => await OpenFullRemoteAsync();

        Render(playback.CurrentSession);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Render(playback.CurrentSession);
        await RefreshRemoteAsync();
    }

    private void WireRemote(Button button, RemoteAction action)
        => button.Clicked += async (_, _) => await ExecuteRemoteAsync(action);

    private async Task RefreshRemoteAsync()
    {
        var target = targetSelection.Selected;
        targetLabel.Text = $"Destination : {target.DisplayName}";
        try
        {
            remoteState = await mediaRemote.GetStateAsync(target);
            remotePanel.IsVisible = remoteState.HasRemoteDevice;
            remoteTargetLabel.Text = remoteState.DeviceName;
            fullRemoteButton.IsEnabled = remoteState.HasRemoteDevice;
            ApplyRemoteCapability(volumeDownButton, RemoteActions.VolumeDown);
            ApplyRemoteCapability(muteButton, RemoteActions.MuteToggle);
            ApplyRemoteCapability(volumeUpButton, RemoteActions.VolumeUp);
            ApplyRemoteCapability(upButton, RemoteActions.Up);
            ApplyRemoteCapability(leftButton, RemoteActions.Left);
            ApplyRemoteCapability(okButton, RemoteActions.Ok);
            ApplyRemoteCapability(rightButton, RemoteActions.Right);
            ApplyRemoteCapability(downButton, RemoteActions.Down);
            ApplyRemoteCapability(backRemoteButton, RemoteActions.Back);
            ApplyRemoteCapability(homeButton, RemoteActions.Home);
            ApplyRemoteCapability(playPauseRemoteButton, RemoteActions.PlayPause);
            ApplyRemoteCapability(guideButton, RemoteActions.Guide);
            remoteStatusLabel.Text = remoteState.Commands.Count == 0
                ? "Aucune commande rapide compatible avec cet appareil."
                : $"{remoteState.Commands.Count} commande(s) disponible(s) selon les capabilities.";
        }
        catch (Exception)
        {
            remoteState = null;
            remotePanel.IsVisible = false;
        }
    }

    private void ApplyRemoteCapability(Button button, RemoteAction action)
    {
        var supported = remoteState?.Supports(action) == true;
        button.IsVisible = supported;
        button.IsEnabled = supported;
    }

    private async Task ExecuteRemoteAsync(RemoteAction action)
    {
        if (remoteState is null || !remoteState.Supports(action)) return;
        try
        {
            SetRemoteButtonsEnabled(false);
            remoteStatusLabel.Text = "Envoi de la commande…";
            var result = await mediaRemote.ExecuteAsync(targetSelection.Selected, action);
            remoteStatusLabel.Text = result.IsSuccess
                ? "Commande acceptée — vérifiez la réaction de l’appareil."
                : result.Error switch
                {
                    RemoteErrorCode.PairingRequired => "L’appareil doit être associé à nouveau.",
                    RemoteErrorCode.UnsupportedAction => "Cette commande n’est plus disponible.",
                    RemoteErrorCode.DeviceNotFound => "L’appareil n’est plus disponible.",
                    _ => "La commande n’a pas pu être confirmée."
                };
        }
        catch (OperationCanceledException)
        {
            remoteStatusLabel.Text = "Commande annulée.";
        }
        catch (Exception)
        {
            remoteStatusLabel.Text = "Impossible de confirmer la commande.";
        }
        finally
        {
            SetRemoteButtonsEnabled(true);
        }
    }

    private void SetRemoteButtonsEnabled(bool enabled)
    {
        foreach (var pair in RemoteButtons())
            pair.Button.IsEnabled = enabled && remoteState?.Supports(pair.Action) == true;
        fullRemoteButton.IsEnabled = enabled && remoteState?.HasRemoteDevice == true;
    }

    private IEnumerable<(Button Button, RemoteAction Action)> RemoteButtons()
    {
        yield return (volumeDownButton, RemoteActions.VolumeDown);
        yield return (muteButton, RemoteActions.MuteToggle);
        yield return (volumeUpButton, RemoteActions.VolumeUp);
        yield return (upButton, RemoteActions.Up);
        yield return (leftButton, RemoteActions.Left);
        yield return (okButton, RemoteActions.Ok);
        yield return (rightButton, RemoteActions.Right);
        yield return (downButton, RemoteActions.Down);
        yield return (backRemoteButton, RemoteActions.Back);
        yield return (homeButton, RemoteActions.Home);
        yield return (playPauseRemoteButton, RemoteActions.PlayPause);
        yield return (guideButton, RemoteActions.Guide);
    }

    private async Task OpenFullRemoteAsync()
    {
        if (remoteState?.DeviceId is not { } deviceId) return;
        deviceSelection.ActiveDeviceId = deviceId;
        await Shell.Current.GoToAsync("//remote");
    }

    private async Task SeekRelativeAsync(TimeSpan delta)
    {
        var session = playback.CurrentSession;
        if (session is null || session.MediaKind == MediaItemKind.LiveChannel) return;
        var target = session.Position + delta;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (session.Duration is { } duration && target > duration) target = duration;
        await RunSafelyAsync(() => playback.SeekAsync(target));
    }

    private void OnSessionChanged(object? sender, PlaybackSessionChangedEventArgs e)
        => MainThread.BeginInvokeOnMainThread(() => Render(e.Session));

    private void Render(PlaybackSession? session)
    {
        emptyState.IsVisible = session is null;
        var hasSession = session is not null;
        titleLabel.Text = session?.Title ?? "Aucune lecture";
        stateLabel.Text = session is null ? "Prêt" : StateText(session);
        positionLabel.Text = Format(session?.Position ?? TimeSpan.Zero);
        durationLabel.Text = session?.Duration is { } duration ? Format(duration) : "--:--";

        var durationSeconds = Math.Max(1, session?.Duration?.TotalSeconds ?? 1);
        timeline.Maximum = durationSeconds;
        if (!dragging)
            timeline.Value = Math.Clamp(session?.Position.TotalSeconds ?? 0, 0, durationSeconds);

        var seekEnabled = hasSession && session!.MediaKind != MediaItemKind.LiveChannel && session.Duration > TimeSpan.Zero;
        timeline.IsEnabled = seekEnabled;
        backButton.IsEnabled = seekEnabled;
        forwardButton.IsEnabled = seekEnabled;
        playButton.IsEnabled = hasSession && session!.State is PlaybackSessionState.Paused or PlaybackSessionState.Stopped or PlaybackSessionState.Opening;
        pauseButton.IsEnabled = hasSession && session!.State is PlaybackSessionState.Playing or PlaybackSessionState.Buffering;
        stopButton.IsEnabled = hasSession && session!.State is not PlaybackSessionState.Stopped and not PlaybackSessionState.Completed and not PlaybackSessionState.Failed;
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            await DisplayAlertAsync("Lecture", "La commande de lecture n’a pas pu être exécutée.", "OK");
        }
    }

    private static string StateText(PlaybackSession session) => session.State switch
    {
        PlaybackSessionState.Resolving => "Préparation du flux…",
        PlaybackSessionState.Opening => "Ouverture…",
        PlaybackSessionState.Buffering => "Mise en mémoire tampon…",
        PlaybackSessionState.Playing => "Lecture",
        PlaybackSessionState.Paused => "En pause",
        PlaybackSessionState.Stopped => "Arrêtée",
        PlaybackSessionState.Completed => "Terminée",
        PlaybackSessionState.Failed => session.UserMessage ?? "Erreur de lecture",
        _ => "Lecture"
    };

    private static string Format(TimeSpan value)
        => value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
}
