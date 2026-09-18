using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Control;

public sealed record MediaRemoteCommand(RemoteAction Action, string Label, string Group);

public sealed record MediaRemoteState(
    IPlaybackTarget Target,
    Guid? DeviceId,
    string DeviceName,
    IReadOnlyList<MediaRemoteCommand> Commands)
{
    public bool HasRemoteDevice => DeviceId.HasValue;
    public bool Supports(RemoteAction action) => Commands.Any(x => x.Action == action);
}

public interface IMediaRemoteControl
{
    Task<MediaRemoteState> GetStateAsync(IPlaybackTarget target, CancellationToken cancellationToken = default);
    Task<RemoteResult> ExecuteAsync(IPlaybackTarget target, RemoteAction action, CancellationToken cancellationToken = default);
}

/// <summary>
/// Capability-driven media/remote bridge. It only uses the target's DeviceId and the device's declared
/// capabilities; it has no knowledge of Samsung, LG, Android TV, Freebox or any other concrete provider.
/// </summary>
public sealed class MediaRemoteControl(IDeviceRepository devices, IRemoteControl remote) : IMediaRemoteControl
{
    private static readonly (RemoteAction Action, string Label, string Group)[] QuickActions =
    [
        (RemoteActions.VolumeDown, "Volume −", "audio"),
        (RemoteActions.MuteToggle, "Muet", "audio"),
        (RemoteActions.VolumeUp, "Volume +", "audio"),
        (RemoteActions.Up, "Haut", "navigation"),
        (RemoteActions.Left, "Gauche", "navigation"),
        (RemoteActions.Ok, "OK", "navigation"),
        (RemoteActions.Right, "Droite", "navigation"),
        (RemoteActions.Down, "Bas", "navigation"),
        (RemoteActions.Back, "Retour", "navigation"),
        (RemoteActions.Home, "Accueil", "navigation"),
        (RemoteActions.PlayPause, "Lecture/Pause", "media"),
        (RemoteActions.Guide, "Guide", "media")
    ];

    public async Task<MediaRemoteState> GetStateAsync(IPlaybackTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.DeviceId is not { } deviceId)
            return new MediaRemoteState(target, null, target.DisplayName, Array.Empty<MediaRemoteCommand>());

        var device = await devices.FindAsync(deviceId, cancellationToken).ConfigureAwait(false);
        if (device is null)
            return new MediaRemoteState(target, null, target.DisplayName, Array.Empty<MediaRemoteCommand>());

        var commands = QuickActions
            .Where(x => device.Capabilities.Contains(x.Action))
            .Select(x => new MediaRemoteCommand(x.Action, x.Label, x.Group))
            .ToArray();

        return new MediaRemoteState(target, device.Id, device.DisplayName, commands);
    }

    public async Task<RemoteResult> ExecuteAsync(
        IPlaybackTarget target,
        RemoteAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(action);
        if (target.DeviceId is not { } deviceId)
            return RemoteResult.Failed(RemoteErrorCode.DeviceNotFound);

        var device = await devices.FindAsync(deviceId, cancellationToken).ConfigureAwait(false);
        if (device is null)
            return RemoteResult.Failed(RemoteErrorCode.DeviceNotFound);
        if (!device.Capabilities.Contains(action))
            return RemoteResult.Failed(RemoteErrorCode.UnsupportedAction);

        return await remote.ExecuteAsync(deviceId, action, cancellationToken).ConfigureAwait(false);
    }
}
