namespace UniversalRemote.Remote.Abstractions;

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
    public static readonly RemoteAction Menu = new("navigation.menu");
    public static readonly RemoteAction ChannelUp = new("channel.up");
    public static readonly RemoteAction ChannelDown = new("channel.down");
    public static readonly RemoteAction PlayPause = new("media.playpause");
    public static readonly RemoteAction Play = new("media.play");
    public static readonly RemoteAction Pause = new("media.pause");
    public static readonly RemoteAction Stop = new("media.stop");
    public static readonly RemoteAction Previous = new("media.previous");
    public static readonly RemoteAction Next = new("media.next");
    public static readonly RemoteAction Rewind = new("media.rewind");
    public static readonly RemoteAction FastForward = new("media.fastforward");
    public static readonly RemoteAction Record = new("media.record");
    public static readonly RemoteAction Input = new("input.select");
    public static readonly RemoteAction Tv = new("input.tv");
    public static readonly RemoteAction Hdmi1 = new("input.hdmi1");
    public static readonly RemoteAction Hdmi2 = new("input.hdmi2");
    public static readonly RemoteAction Apps = new("apps.open");
    public static readonly RemoteAction Guide = new("navigation.guide");
    public static readonly RemoteAction Epg = new("navigation.epg");
    public static readonly RemoteAction Settings = new("navigation.settings");
    public static readonly RemoteAction Favorite = new("navigation.favorite");
    public static readonly RemoteAction Shift = new("key.shift");
    public static readonly RemoteAction Exit = new("navigation.exit");
    public static readonly RemoteAction Delete = new("text.delete");
    public static readonly RemoteAction Red = new("key.red");
    public static readonly RemoteAction Green = new("key.green");
    public static readonly RemoteAction Yellow = new("key.yellow");
    public static readonly RemoteAction Blue = new("key.blue");
    public static readonly RemoteAction Digit0 = new("digit.0");
    public static readonly RemoteAction Digit1 = new("digit.1");
    public static readonly RemoteAction Digit2 = new("digit.2");
    public static readonly RemoteAction Digit3 = new("digit.3");
    public static readonly RemoteAction Digit4 = new("digit.4");
    public static readonly RemoteAction Digit5 = new("digit.5");
    public static readonly RemoteAction Digit6 = new("digit.6");
    public static readonly RemoteAction Digit7 = new("digit.7");
    public static readonly RemoteAction Digit8 = new("digit.8");
    public static readonly RemoteAction Digit9 = new("digit.9");
    public static readonly RemoteAction Netflix = new("app.netflix");
    public static readonly RemoteAction YouTube = new("app.youtube");
    public static readonly RemoteAction PrimeVideo = new("app.primevideo");
    public static readonly RemoteAction DisneyPlus = new("app.disneyplus");

}
