using UniversalRemote.Abstractions;

namespace UniversalRemote.Presentation;

/// <summary>
/// Builds a semantic remote from capabilities only. Provider and manufacturer names are deliberately ignored.
/// </summary>
public sealed class CapabilityRemoteUiModelBuilder : IRemoteUiModelBuilder
{
    private static readonly IReadOnlyDictionary<string, ControlMetadata> Metadata =
        new Dictionary<string, ControlMetadata>(StringComparer.Ordinal)
        {
            [RemoteActions.PowerToggle.Id] = new("power", "remote.action.power", RemoteControlRole.Power, 10),
            [RemoteActions.VolumeUp.Id] = new("audio", "remote.action.volumeUp", RemoteControlRole.Audio, 20),
            [RemoteActions.VolumeDown.Id] = new("audio", "remote.action.volumeDown", RemoteControlRole.Audio, 21),
            [RemoteActions.MuteToggle.Id] = new("audio", "remote.action.mute", RemoteControlRole.Audio, 22),
            [RemoteActions.Up.Id] = new("navigation", "remote.action.up", RemoteControlRole.Navigation, 30),
            [RemoteActions.Left.Id] = new("navigation", "remote.action.left", RemoteControlRole.Navigation, 31),
            [RemoteActions.Ok.Id] = new("navigation", "remote.action.ok", RemoteControlRole.Primary, 32),
            [RemoteActions.Right.Id] = new("navigation", "remote.action.right", RemoteControlRole.Navigation, 33),
            [RemoteActions.Down.Id] = new("navigation", "remote.action.down", RemoteControlRole.Navigation, 34),
            [RemoteActions.Back.Id] = new("navigation", "remote.action.back", RemoteControlRole.Secondary, 35),
            [RemoteActions.Home.Id] = new("navigation", "remote.action.home", RemoteControlRole.Secondary, 36),
            [RemoteActions.Menu.Id] = new("navigation", "remote.action.menu", RemoteControlRole.Secondary, 37),
            [RemoteActions.ChannelUp.Id] = new("channel", "remote.action.channelUp", RemoteControlRole.Extra, 40),
            [RemoteActions.ChannelDown.Id] = new("channel", "remote.action.channelDown", RemoteControlRole.Extra, 41),
            [RemoteActions.PlayPause.Id] = new("media", "remote.action.playPause", RemoteControlRole.Extra, 50),
            [RemoteActions.Rewind.Id] = new("media", "remote.action.rewind", RemoteControlRole.Extra, 51),
            [RemoteActions.FastForward.Id] = new("media", "remote.action.fastForward", RemoteControlRole.Extra, 52),
            [RemoteActions.Record.Id] = new("media", "remote.action.record", RemoteControlRole.Extra, 53)
        };

    private static readonly string[] SectionOrder = ["power", "navigation", "channel", "audio", "media", "extras"];

    public RemoteUiModel Build(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var groups = new Dictionary<string, List<RemoteUiControl>>(StringComparer.Ordinal);
        foreach (var action in device.Capabilities.OrderBy(capability => capability.Id, StringComparer.Ordinal))
        {
            var metadata = Metadata.TryGetValue(action.Id, out var known)
                ? known
                : new ControlMetadata("extras", "remote.action.generic", RemoteControlRole.Extra, 1000);

            if (!groups.TryGetValue(metadata.SectionId, out var controls))
            {
                controls = [];
                groups.Add(metadata.SectionId, controls);
            }

            controls.Add(new RemoteUiControl(
                action.Id,
                action,
                metadata.LabelKey == "remote.action.generic" ? $"remote.action.{action.Id}" : metadata.LabelKey,
                metadata.Role,
                metadata.Priority));
        }

        var sections = SectionOrder
            .Where(groups.ContainsKey)
            .Select(sectionId => new RemoteUiSection(
                sectionId,
                $"remote.section.{sectionId}",
                groups[sectionId]
                    .OrderBy(control => control.Priority)
                    .ThenBy(control => control.Action.Id, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        return new RemoteUiModel(device.Id, device.DisplayName, sections);
    }

    private sealed record ControlMetadata(
        string SectionId,
        string LabelKey,
        RemoteControlRole Role,
        int Priority);
}
