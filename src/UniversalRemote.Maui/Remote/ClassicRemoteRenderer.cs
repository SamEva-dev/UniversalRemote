using Microsoft.Maui.Layouts;
using UniversalRemote.Remote.Presentation;
using UniversalRemote.Remote.Theming;

namespace UniversalRemote.Maui.Remote;

public sealed class ClassicRemoteRenderer : IRemoteLayoutRenderer
{
    public string LayoutId => BuiltInRemoteStyles.Classic.Id;

    public View Render(RemoteUiModel model, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync)
    {
        var root = new VerticalStackLayout { Spacing = theme.Tokens.SpacingDp };
        if (RemoteFavoritesFactory.Render(model, theme, executeAsync) is { } favorites) root.Children.Add(favorites);
        AddLinearSection(root, model, "power", theme, executeAsync);
        AddNavigation(root, model, theme, executeAsync);
        AddLinearSection(root, model, "channel", theme, executeAsync);
        AddLinearSection(root, model, "audio", theme, executeAsync);
        AddLinearSection(root, model, "media", theme, executeAsync);
        AddLinearSection(root, model, "extras", theme, executeAsync);
        return root;
    }

    private static void AddLinearSection(Layout root, RemoteUiModel model, string sectionId, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync)
    {
        var section = model.Sections.FirstOrDefault(x => x.Id == sectionId);
        if (section is null) return;
        var row = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap, JustifyContent = FlexJustify.Center };
        foreach (var control in section.Controls) row.Children.Add(RemoteControlFactory.Button(control, theme, executeAsync));
        root.Children.Add(row);
    }

    private static void AddNavigation(Layout root, RemoteUiModel model, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync)
    {
        var section = model.Sections.FirstOrDefault(x => x.Id == "navigation");
        if (section is null) return;

        var byId = section.Controls.ToDictionary(x => x.Action.Id, StringComparer.Ordinal);
        var grid = new Grid
        {
            RowSpacing = theme.Tokens.SpacingDp,
            ColumnSpacing = theme.Tokens.SpacingDp,
            HorizontalOptions = LayoutOptions.Fill,
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)
            },
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)
            }
        };

        Add("navigation.up", 1, 0); Add("navigation.left", 0, 1); Add("navigation.ok", 1, 1);
        Add("navigation.right", 2, 1); Add("navigation.down", 1, 2); Add("navigation.back", 0, 3); Add("navigation.home", 2, 3);
        root.Children.Add(grid);
        var placed = new[] { "navigation.up", "navigation.left", "navigation.ok", "navigation.right", "navigation.down", "navigation.back", "navigation.home" };
        foreach (var control in section.Controls.Where(c => !placed.Contains(c.Action.Id)))
            root.Children.Add(RemoteControlFactory.Button(control, theme, executeAsync));

        void Add(string id, int column, int row)
        {
            if (!byId.TryGetValue(id, out var control)) return;
            grid.Add(RemoteControlFactory.Button(control, theme, executeAsync), column, row);
        }
    }
}
