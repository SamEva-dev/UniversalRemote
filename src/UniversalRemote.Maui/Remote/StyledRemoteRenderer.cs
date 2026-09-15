using Microsoft.Maui.Layouts;
using UniversalRemote.Presentation;
using UniversalRemote.Theming;
namespace UniversalRemote.Maui.Remote;

/// <summary>Data-driven renderers share actions and responsive wrapping; only presentation differs.</summary>
public sealed class StyledRemoteRenderer(RemoteLayoutDefinition layout) : IRemoteLayoutRenderer
{
    public string LayoutId => layout.Id;
    public View Render(RemoteUiModel model, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync)
    {
        var container = new VerticalStackLayout { Spacing = theme.Tokens.SpacingDp };
        if (RemoteFavoritesFactory.Render(model, theme, executeAsync) is { } favorites) container.Children.Add(favorites);
        var root = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap, AlignItems = FlexAlignItems.Start };
        foreach (var section in RemoteLayoutProjection.Sections(model, layout))
        {
            var content = new VerticalStackLayout { Spacing = theme.Tokens.SpacingDp };
            if (layout.ShowSectionLabels)
                content.Children.Add(new Label { Text = RemoteLabels.Section(section.Id), TextColor = Color.FromArgb(theme.Palette.Text), FontSize = 18, FontAttributes = FontAttributes.Bold });
            var controls = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap, JustifyContent = FlexJustify.Center };
            foreach (var control in section.Controls)
            {
                var button = RemoteControlFactory.Button(control, theme, executeAsync);
                FlexLayout.SetBasis(button, new FlexBasis(layout.Id == "horizon" ? .9f : .43f, true));
                FlexLayout.SetGrow(button, 1);
                controls.Children.Add(button);
            }
            content.Children.Add(controls);
            var card = new Border
            {
                Content = content, Padding = new Thickness(12), Margin = new Thickness(6),
                BackgroundColor = Color.FromArgb(theme.Palette.Surface), StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(theme.Tokens.CornerRadiusDp) }
            };
            // Responsive columns based on the available content width, never on a device model.
            FlexLayout.SetGrow(card, 1);
            FlexLayout.SetBasis(card, new FlexBasis(100, false));
            root.Children.Add(card);
        }
        root.SizeChanged += (_, _) =>
        {
            var columns = root.Width >= 600 && layout.Id is "fusion" or "horizon" or "nova" ? 2 : 1;
            foreach (var card in root.Children) FlexLayout.SetBasis((BindableObject)card, new FlexBasis((float)Math.Max(1, root.Width / columns - 12)));
        };
        container.Children.Add(root);
        return container;
    }
}
