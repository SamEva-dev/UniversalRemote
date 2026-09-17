using UniversalRemote.Maui.Remote.Views;
using UniversalRemote.Remote.Presentation;
using UniversalRemote.Remote.Theming;

namespace UniversalRemote.Maui.Remote;

/// <summary>
/// Hybrid renderer: the visual composition is XAML, while the semantic model and actions remain generated
/// from device capabilities. A C# renderer is retained as an explicit fallback for migration safety.
/// </summary>
public sealed class XamlRemoteRenderer(string layoutId, IRemoteLayoutRenderer fallback) : IContextualRemoteLayoutRenderer
{
    public string LayoutId { get; } = layoutId;

    public View Render(RemoteUiModel model, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync)
        => RenderCore(model, theme, executeAsync, null);

    public View Render(
        RemoteUiModel model,
        RemoteThemeDefinition theme,
        Func<RemoteUiControl, Task> executeAsync,
        RemoteSurfaceContext context)
        => RenderCore(model, theme, executeAsync, context);

    private View RenderCore(
        RemoteUiModel model,
        RemoteThemeDefinition theme,
        Func<RemoteUiControl, Task> executeAsync,
        RemoteSurfaceContext? context)
    {
        var view = CreateView(LayoutId);
        if (view is null)
        {
            return context is not null && fallback is IContextualRemoteLayoutRenderer contextual
                ? contextual.Render(model, theme, executeAsync, context)
                : fallback.Render(model, theme, executeAsync);
        }

        view.BindingContext = new RemoteXamlViewModel(LayoutId, model, theme, executeAsync, context);
        return view;
    }

    private static ContentView? CreateView(string id) => id switch
    {
        "classic" => new ClassicRemoteView(),
        "minimal" => new MinimalRemoteView(),
        "nova" => new NovaRemoteView(),
        "elite" => new EliteRemoteView(),
        "horizon" => new HorizonRemoteView(),
        "fusion" => new FusionRemoteView(),
        "neo" => new NeoRemoteView(),
        _ => null
    };
}
