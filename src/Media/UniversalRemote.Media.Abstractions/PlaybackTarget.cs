using System.Collections.Frozen;

namespace UniversalRemote.Media.Abstractions;

public enum PlaybackTargetKind
{
    LocalDevice,
    AndroidTv,
    Cast,
    RemoteDevice,
    Other
}

public enum PlaybackCapability
{
    Start,
    Stop,
    Pause,
    Resume,
    Seek,
    SelectAudioTrack,
    SelectSubtitleTrack,
    ChangeQuality
}

/// <summary>A destination capable of receiving media playback.</summary>
public interface IPlaybackTarget
{
    string Id { get; }
    string DisplayName { get; }
    PlaybackTargetKind Kind { get; }
    Guid? DeviceId { get; }
    IReadOnlySet<PlaybackCapability> Capabilities { get; }
}

/// <summary>
/// Immutable provider-independent playback destination. DeviceId links a remote playback target to the
/// control domain without turning a MediaSource into a Device.
/// </summary>
public sealed class PlaybackTarget : IPlaybackTarget
{
    public string Id { get; }
    public string DisplayName { get; }
    public PlaybackTargetKind Kind { get; }
    public Guid? DeviceId { get; }
    public IReadOnlySet<PlaybackCapability> Capabilities { get; }

    public PlaybackTarget(
        string id,
        string displayName,
        PlaybackTargetKind kind,
        IEnumerable<PlaybackCapability> capabilities,
        Guid? deviceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (deviceId == Guid.Empty) throw new ArgumentException("Playback target device ID must not be empty when specified.", nameof(deviceId));

        var normalizedId = id.Trim();
        var normalizedName = displayName.Trim();
        var capabilitySnapshot = capabilities.ToArray();

        if (normalizedId.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(id), "Playback target ID must be 200 characters or fewer.");
        if (normalizedName.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(displayName), "Playback target name must be 100 characters or fewer.");
        if (capabilitySnapshot.Any(x => !Enum.IsDefined(x)))
            throw new ArgumentException("Playback target contains an unknown capability.", nameof(capabilities));

        Id = normalizedId;
        DisplayName = normalizedName;
        Kind = kind;
        DeviceId = deviceId;
        Capabilities = capabilitySnapshot.ToFrozenSet();
    }
}

/// <summary>
/// Discovery result enriched with availability. A target can be visible before a transport capable of
/// launching arbitrary media on it is installed. This prevents the UI from advertising a non-working cast.
/// </summary>
public sealed record PlaybackTargetCandidate
{
    public required IPlaybackTarget Target { get; init; }
    public required bool CanLaunch { get; init; }
    public required string Status { get; init; }
    public string? AddressHint { get; init; }
}

/// <summary>Provider-independent discovery of local and remote playback destinations.</summary>
public interface IPlaybackTargetDiscovery
{
    Task<IReadOnlyList<PlaybackTargetCandidate>> DiscoverAsync(CancellationToken cancellationToken = default);
}

/// <summary>Keeps the user's selected destination independent from the selected MediaSource.</summary>
public interface IPlaybackTargetSelection
{
    IPlaybackTarget Selected { get; }
    void Select(IPlaybackTarget target);
}

public sealed class PlaybackTargetSelection : IPlaybackTargetSelection
{
    private readonly object gate = new();
    private IPlaybackTarget selected = PlaybackTargets.LocalDevice;

    public IPlaybackTarget Selected
    {
        get { lock (gate) return selected; }
    }

    public void Select(IPlaybackTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (gate) selected = target;
    }
}
