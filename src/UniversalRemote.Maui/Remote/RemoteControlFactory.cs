using UniversalRemote.Presentation;
using UniversalRemote.Theming;
namespace UniversalRemote.Maui.Remote;

internal static class RemoteControlFactory
{
    public static Button Button(
        RemoteUiControl control,
        RemoteThemeDefinition theme,
        Func<RemoteUiControl, Task> executeAsync,
        string textPrefix = "",
        string automationSuffix = "")
    {
        var primary = control.Role is RemoteControlRole.Power or RemoteControlRole.Primary;
        var label = RemoteLabels.Action(control);
        var button = new Button
        {
            Text = textPrefix + label,
            MinimumHeightRequest = Math.Max(48, theme.Tokens.MinimumTouchTargetDp),
            MinimumWidthRequest = Math.Max(48, theme.Tokens.MinimumTouchTargetDp),
            CornerRadius = (int)theme.Tokens.CornerRadiusDp,
            BackgroundColor = Color.FromArgb(primary ? theme.Palette.Accent : theme.Palette.Surface),
            TextColor = Color.FromArgb(primary ? theme.Palette.OnAccent : theme.Palette.Text),
            FontSize = 16,
            Padding = new Thickness(12, 14),
            Margin = new Thickness(4),
            AutomationId = $"remote-{control.Id}{automationSuffix}"
        };
        SemanticProperties.SetDescription(button, textPrefix.Length == 0
            ? label
            : RemoteLabels.Text($"Favori : {label}", $"Favorite: {label}"));
        button.Clicked += async (_, _) =>
        {
            if (!button.IsEnabled) return;
            button.IsEnabled = false;
            try { await executeAsync(control).ConfigureAwait(true); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }
}
