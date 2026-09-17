using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.ApplicationModel;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Playback;

namespace UniversalRemote.Maui.Media;

/// <summary>
/// MAUI MediaElement adapter for MEDIA 3. On Android, MediaElement is backed by Android Media3/ExoPlayer.
/// The adapter never logs or exposes the resolved source URI.
/// </summary>
public sealed class MauiMediaElementPlaybackEngine : ILocalPlaybackEngine
{
    private readonly object gate = new();
    private MediaElement? element;
    private LocalPlaybackEngineSnapshot snapshot = new(LocalPlaybackEngineState.None, TimeSpan.Zero, null);
    private TaskCompletionSource<bool>? pendingOpen;

    public LocalPlaybackEngineSnapshot Snapshot
    {
        get { lock (gate) return snapshot; }
    }

    public event EventHandler<LocalPlaybackEngineChangedEventArgs>? Changed;

    public void Attach(MediaElement mediaElement)
    {
        ArgumentNullException.ThrowIfNull(mediaElement);
        lock (gate)
        {
            if (ReferenceEquals(element, mediaElement)) return;
            if (element is not null) Unsubscribe(element);
            element = mediaElement;
            Subscribe(mediaElement);
            snapshot = SnapshotFrom(mediaElement, Map(mediaElement.CurrentState));
        }
        Publish(Snapshot);
    }

    public async Task LoadAsync(
        ResolvedMediaStream stream,
        string title,
        TimeSpan resumePosition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (resumePosition < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(resumePosition));
        ValidateScheme(stream.StreamUri);

        var media = RequiredElement();
        var open = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            pendingOpen?.TrySetCanceled();
            pendingOpen = open;
        }

        using var registration = cancellationToken.Register(() => open.TrySetCanceled(cancellationToken));
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            media.Stop();
            media.MetadataTitle = title.Trim();
            media.ShouldAutoPlay = false;
            media.ShouldKeepScreenOn = true;
            media.ShouldShowPlaybackControls = false;
            media.Source = CommunityToolkit.Maui.Views.MediaSource.FromUri(stream.StreamUri);
        }).ConfigureAwait(false);

        Publish(new LocalPlaybackEngineSnapshot(LocalPlaybackEngineState.Opening, TimeSpan.Zero, null));
        await open.Task.ConfigureAwait(false);

        if (resumePosition > TimeSpan.Zero && media.Duration > TimeSpan.Zero && resumePosition < media.Duration)
            await MainThread.InvokeOnMainThreadAsync(() => media.SeekTo(resumePosition, cancellationToken)).ConfigureAwait(false);
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var media = RequiredElement();
        return MainThread.InvokeOnMainThreadAsync(media.Play);
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var media = RequiredElement();
        return MainThread.InvokeOnMainThreadAsync(media.Pause);
    }

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (position < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(position));
        var media = RequiredElement();
        return MainThread.InvokeOnMainThreadAsync(() => media.SeekTo(position, cancellationToken));
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var media = RequiredElement();
        return MainThread.InvokeOnMainThreadAsync(media.Stop);
    }

    private MediaElement RequiredElement()
    {
        lock (gate)
            return element ?? throw new InvalidOperationException("The media player surface is not attached. Open the Media player page before starting local playback.");
    }

    private void Subscribe(MediaElement media)
    {
        media.MediaOpened += OnMediaOpened;
        media.MediaEnded += OnMediaEnded;
        media.MediaFailed += OnMediaFailed;
        media.StateChanged += OnStateChanged;
        media.PositionChanged += OnPositionChanged;
    }

    private void Unsubscribe(MediaElement media)
    {
        media.MediaOpened -= OnMediaOpened;
        media.MediaEnded -= OnMediaEnded;
        media.MediaFailed -= OnMediaFailed;
        media.StateChanged -= OnStateChanged;
        media.PositionChanged -= OnPositionChanged;
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        if (sender is not MediaElement media) return;
        TaskCompletionSource<bool>? open;
        lock (gate)
        {
            open = pendingOpen;
            pendingOpen = null;
        }
        Publish(SnapshotFrom(media, LocalPlaybackEngineState.Stopped));
        open?.TrySetResult(true);
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (sender is MediaElement media)
            Publish(SnapshotFrom(media, LocalPlaybackEngineState.Completed));
    }

    private void OnMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        TaskCompletionSource<bool>? open;
        lock (gate)
        {
            open = pendingOpen;
            pendingOpen = null;
        }
        Publish(new LocalPlaybackEngineSnapshot(LocalPlaybackEngineState.Failed, Snapshot.Position, Snapshot.Duration, "player.media_failed"));
        open?.TrySetException(new InvalidOperationException("The platform media player could not open the selected source."));
    }

    private void OnStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        if (sender is MediaElement media)
            Publish(SnapshotFrom(media, Map(e.NewState)));
    }

    private void OnPositionChanged(object? sender, MediaPositionChangedEventArgs e)
    {
        if (sender is not MediaElement media) return;
        var current = Snapshot;
        Publish(new LocalPlaybackEngineSnapshot(current.State, e.Position, DurationOrNull(media.Duration), current.FailureCode));
    }

    private void Publish(LocalPlaybackEngineSnapshot next)
    {
        lock (gate) snapshot = next;
        Changed?.Invoke(this, new LocalPlaybackEngineChangedEventArgs(next));
    }

    private static LocalPlaybackEngineSnapshot SnapshotFrom(MediaElement media, LocalPlaybackEngineState state)
        => new(state, media.Position, DurationOrNull(media.Duration), state == LocalPlaybackEngineState.Failed ? "player.media_failed" : null);

    private static TimeSpan? DurationOrNull(TimeSpan duration) => duration > TimeSpan.Zero ? duration : null;

    private static LocalPlaybackEngineState Map(MediaElementState state) => state switch
    {
        MediaElementState.Opening => LocalPlaybackEngineState.Opening,
        MediaElementState.Buffering => LocalPlaybackEngineState.Buffering,
        MediaElementState.Playing => LocalPlaybackEngineState.Playing,
        MediaElementState.Paused => LocalPlaybackEngineState.Paused,
        MediaElementState.Stopped => LocalPlaybackEngineState.Stopped,
        MediaElementState.Failed => LocalPlaybackEngineState.Failed,
        _ => LocalPlaybackEngineState.None
    };

    private static void ValidateScheme(Uri uri)
    {
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
            return;

        throw new NotSupportedException("The local player supports HTTP, HTTPS and RTSP sources in MEDIA 3.");
    }
}
