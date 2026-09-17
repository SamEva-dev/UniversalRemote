using UniversalRemote.Remote.Presentation;
using UniversalRemote.Remote.Theming;
namespace UniversalRemote.Maui.Remote;
public sealed class MinimalRemoteRenderer : IRemoteLayoutRenderer
{
    public string LayoutId => BuiltInRemoteStyles.Minimal.Id;
    public View Render(RemoteUiModel model, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync)
    {
        var root = new VerticalStackLayout { Spacing = theme.Tokens.SpacingDp };
        if (RemoteFavoritesFactory.Render(model, theme, executeAsync) is { } favorites) root.Children.Add(favorites);
        foreach (var section in RemoteLayoutProjection.Sections(model, BuiltInRemoteStyles.Minimal))
            foreach (var control in section.Controls)
            {
                var button = RemoteControlFactory.Button(control, theme, executeAsync);
                button.MinimumHeightRequest = Math.Max(56, theme.Tokens.MinimumTouchTargetDp);
                button.HorizontalOptions = LayoutOptions.Fill;
                root.Children.Add(button);
            }
        return root;
    }
}
