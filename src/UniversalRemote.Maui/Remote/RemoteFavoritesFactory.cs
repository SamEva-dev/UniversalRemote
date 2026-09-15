using Microsoft.Maui.Layouts;
using UniversalRemote.Presentation;
using UniversalRemote.Theming;

namespace UniversalRemote.Maui.Remote;

internal static class RemoteFavoritesFactory
{
    public static View? Render(RemoteUiModel model, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync)
    {
        if (model.Favorites.Count == 0) return null;

        var row = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Center
        };
        foreach (var control in model.Favorites)
            row.Children.Add(RemoteControlFactory.Button(control, theme, executeAsync, "★ ", "-favorite"));

        return new VerticalStackLayout
        {
            Spacing = theme.Tokens.SpacingDp,
            Children =
            {
                new Label
                {
                    Text = RemoteLabels.Text("Favoris", "Favorites"),
                    TextColor = Color.FromArgb(theme.Palette.Text),
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold
                },
                row
            }
        };
    }
}
