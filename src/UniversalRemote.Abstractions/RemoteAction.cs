namespace UniversalRemote.Abstractions;

/// <summary>Stable, extensible wire identifier; never serialize enum ordinals as action IDs.</summary>
public sealed record RemoteAction
{
    public string Id { get; }
    public RemoteAction(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 100 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
            throw new ArgumentException("Invalid action identifier.", nameof(id));
        Id = id;
    }
    public override string ToString() => Id;
}

/// <summary>Initial parameterless commands. Parameterized actions get typed contracts in a later sprint.</summary>
public static class RemoteActions
{
    public static readonly RemoteAction PowerToggle = new("power.toggle");
    public static readonly RemoteAction VolumeUp = new("volume.up");
    public static readonly RemoteAction VolumeDown = new("volume.down");
    public static readonly RemoteAction MuteToggle = new("audio.mute.toggle");
    public static readonly RemoteAction Up = new("navigation.up");
    public static readonly RemoteAction Down = new("navigation.down");
    public static readonly RemoteAction Left = new("navigation.left");
    public static readonly RemoteAction Right = new("navigation.right");
    public static readonly RemoteAction Ok = new("navigation.ok");
    public static readonly RemoteAction Back = new("navigation.back");
    public static readonly RemoteAction Home = new("navigation.home");
}
