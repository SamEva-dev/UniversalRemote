using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Playback;

public enum LocalPlaybackEngineState
{
    None,
    Opening,
    Buffering,
    Playing,
    Paused,
    Stopped,
    Completed,
    Failed
}

/// <summary>
/// Sanitized state published by the platform player. It must never contain the source URI or platform exception text.
/// </summary>
public sealed record LocalPlaybackEngineSnapshot(
    LocalPlaybackEngineState State,
    TimeSpan Position,
    TimeSpan? Duration,
    string? FailureCode = null);

public sealed class LocalPlaybackEngineChangedEventArgs : EventArgs
{
    public LocalPlaybackEngineSnapshot Snapshot { get; }

    public LocalPlaybackEngineChangedEventArgs(LocalPlaybackEngineSnapshot snapshot)
        => Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
}

/// <summary>
/// Platform adapter used by the provider-independent playback coordinator. Implementations receive an ephemeral
/// ResolvedMediaStream and are responsible for ensuring it never appears in logs or diagnostics.
/// </summary>
public interface ILocalPlaybackEngine
{
    LocalPlaybackEngineSnapshot Snapshot { get; }
    event EventHandler<LocalPlaybackEngineChangedEventArgs>? Changed;

    Task LoadAsync(
        ResolvedMediaStream stream,
        string title,
        TimeSpan resumePosition,
        CancellationToken cancellationToken = default);

    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
