using UniversalRemote.Remote.Presentation;
using UniversalRemote.Remote.Theming;

namespace UniversalRemote.Maui.Remote;

public sealed record RemoteDeviceTile(Guid Id, string Name, string Room, string Kind);
public sealed record RemoteActivityTile(Guid Id, string Name, int StepCount);

public sealed record RemoteSurfaceContext(
    IReadOnlyList<RemoteDeviceTile> Devices,
    IReadOnlyList<RemoteActivityTile> Activities,
    Func<Guid, Task> SelectDevice,
    Func<Guid, Task> RunActivity,
    Func<string, Task> Navigate);

public interface IContextualRemoteLayoutRenderer : IRemoteLayoutRenderer
{
    View Render(RemoteUiModel model,
        RemoteThemeDefinition theme,
        Func<RemoteUiControl, Task> executeAsync,
        RemoteSurfaceContext context);
}
