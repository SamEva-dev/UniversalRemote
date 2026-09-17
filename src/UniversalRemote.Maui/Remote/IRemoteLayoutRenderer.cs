using UniversalRemote.Remote.Presentation;
using UniversalRemote.Remote.Theming;

namespace UniversalRemote.Maui.Remote;

public interface IRemoteLayoutRenderer
{
    string LayoutId { get; }
    View Render(RemoteUiModel model, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync);
}
