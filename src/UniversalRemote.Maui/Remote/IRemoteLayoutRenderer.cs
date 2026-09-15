using UniversalRemote.Presentation;
using UniversalRemote.Theming;

namespace UniversalRemote.Maui.Remote;

public interface IRemoteLayoutRenderer
{
    string LayoutId { get; }
    View Render(RemoteUiModel model, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync);
}
