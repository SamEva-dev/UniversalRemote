using System.Windows.Input;
using UniversalRemote.Remote.Presentation;
using UniversalRemote.Remote.Theming;

namespace UniversalRemote.Maui.Remote;

/// <summary>
/// Binding model used only by the XAML remote views. It stays provider-neutral and exposes
/// the semantic controls already projected by UniversalRemote.Presentation.
/// </summary>
public sealed class RemoteXamlViewModel
{
    private static readonly string[] QuickActionIds =
    [
        "input.select", "input.tv", "input.hdmi1", "input.hdmi2", "apps.open",
        "navigation.home", "navigation.back", "navigation.menu", "navigation.guide", "navigation.exit"
    ];

    private static readonly HashSet<string> NavigationCoreIds = new(StringComparer.Ordinal)
    {
        "navigation.up", "navigation.down", "navigation.left", "navigation.right", "navigation.ok"
    };

    private static readonly HashSet<string> ReservedExtraIds = new(QuickActionIds, StringComparer.Ordinal);

    public RemoteXamlViewModel(
        string layoutId,
        RemoteUiModel model,
        RemoteThemeDefinition theme,
        Func<RemoteUiControl, Task> executeAsync,
        RemoteSurfaceContext? context)
    {
        LayoutId = layoutId;
        DisplayName = model.DisplayName;
        BackgroundColor = Color.FromArgb(theme.Palette.Background);
        SurfaceColor = Color.FromArgb(theme.Palette.Surface);
        TextColor = Color.FromArgb(theme.Palette.Text);
        AccentColor = Color.FromArgb(theme.Palette.Accent);
        OnAccentColor = Color.FromArgb(theme.Palette.OnAccent);
        CornerRadius = theme.Tokens.CornerRadiusDp;
        Spacing = theme.Tokens.SpacingDp;
        MinimumTouchTarget = Math.Max(48, theme.Tokens.MinimumTouchTargetDp);

        var dark = layoutId is "elite" or "horizon" or "fusion" or "neo";
        var tone = dark ? "light" : "dark";
        var controls = model.Controls.Select(control => new RemoteXamlControlItem(control, theme, tone, executeAsync)).ToArray();
        var byId = controls.ToDictionary(item => item.Id, StringComparer.Ordinal);

        Power = Find("power.toggle");
        Up = Find("navigation.up");
        Down = Find("navigation.down");
        Left = Find("navigation.left");
        Right = Find("navigation.right");
        Ok = Find("navigation.ok");
        Back = Find("navigation.back");
        Home = Find("navigation.home");
        Menu = Find("navigation.menu");
        VolumeUp = Find("volume.up");
        VolumeDown = Find("volume.down");
        Mute = Find("audio.mute.toggle");
        ChannelUp = Find("channel.up");
        ChannelDown = Find("channel.down");

        AllControls = controls;
        Favorites = model.Favorites
            .Select(control => byId.TryGetValue(control.Action.Id, out var item) ? item : null)
            .Where(item => item is not null)
            .Cast<RemoteXamlControlItem>()
            .ToArray();

        Media = FromSection("media");
        Audio = FromSection("audio");
        Channel = FromSection("channel");
        Navigation = FromSection("navigation");
        QuickActions = QuickActionIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        Apps = controls.Where(item => item.Id.StartsWith("app.", StringComparison.Ordinal)).ToArray();
        Digits = controls.Where(item => item.Id.StartsWith("digit.", StringComparison.Ordinal)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        ColorKeys = controls.Where(item => item.Id.StartsWith("key.", StringComparison.Ordinal)).ToArray();
        // Keep every capability-driven action that is not already presented as a quick action.
        // This is the safety net that prevents a new provider action, digit or app shortcut from disappearing.
        Extras = FromSection("extras")
            .Where(item => !ReservedExtraIds.Contains(item.Id))
            .ToArray();
        NavigationExtras = Navigation.Where(item => !NavigationCoreIds.Contains(item.Id)).ToArray();

        Devices = (context?.Devices ?? Array.Empty<RemoteDeviceTile>())
            .Select(device => new RemoteXamlDeviceItem(device, context?.SelectDevice))
            .ToArray();
        Activities = (context?.Activities ?? Array.Empty<RemoteActivityTile>())
            .Select(activity => new RemoteXamlActivityItem(activity, context?.RunActivity))
            .ToArray();

        NavigateToDevicesCommand = CreateNavigationCommand("devices");
        NavigateToActivitiesCommand = CreateNavigationCommand("activities");
        NavigateToFavoritesCommand = CreateNavigationCommand("favorites");

        RemoteXamlControlItem? Find(string id) => byId.TryGetValue(id, out var item) ? item : null;
        IReadOnlyList<RemoteXamlControlItem> FromSection(string id) =>
            model.Sections.FirstOrDefault(section => section.Id == id)?.Controls
                .Select(control => byId[control.Action.Id]).ToArray()
            ?? Array.Empty<RemoteXamlControlItem>();

        ICommand CreateNavigationCommand(string route) => new Command(async () =>
        {
            if (context is not null)
                await context.Navigate(route).ConfigureAwait(true);
        });
    }

    public string LayoutId { get; }
    public string DisplayName { get; }
    public string FavoritesTitle => RemoteLabels.Text("Favoris", "Favorites");
    public string NavigationTitle => RemoteLabels.Text("Navigation", "Navigation");
    public string VolumeTitle => RemoteLabels.Text("Volume", "Volume");
    public string ChannelsTitle => RemoteLabels.Text("Chaînes", "Channels");
    public string ShortcutsTitle => RemoteLabels.Text("Raccourcis", "Shortcuts");
    public string MediaTitle => RemoteLabels.Text("Média", "Media");
    public string MoreControlsTitle => RemoteLabels.Text("Autres commandes", "More controls");
    public string DevicesTitle => RemoteLabels.Text("Appareils", "Devices");
    public string ActivitiesTitle => RemoteLabels.Text("Activités", "Activities");
    public string AppsTitle => RemoteLabels.Text("Applications", "Apps");
    public string MinimalSubtitle => RemoteLabels.Text("Interface minimale · grandes cibles tactiles", "Minimal interface · large touch targets");
    public string ScenesTitle => RemoteLabels.Text("Scènes", "Scenes");
    public Color BackgroundColor { get; }
    public Color SurfaceColor { get; }
    public Color TextColor { get; }
    public Color AccentColor { get; }
    public Color OnAccentColor { get; }
    public double CornerRadius { get; }
    public double Spacing { get; }
    public double MinimumTouchTarget { get; }

    public RemoteXamlControlItem? Power { get; }
    public RemoteXamlControlItem? Up { get; }
    public RemoteXamlControlItem? Down { get; }
    public RemoteXamlControlItem? Left { get; }
    public RemoteXamlControlItem? Right { get; }
    public RemoteXamlControlItem? Ok { get; }
    public RemoteXamlControlItem? Back { get; }
    public RemoteXamlControlItem? Home { get; }
    public RemoteXamlControlItem? Menu { get; }
    public RemoteXamlControlItem? VolumeUp { get; }
    public RemoteXamlControlItem? VolumeDown { get; }
    public RemoteXamlControlItem? Mute { get; }
    public RemoteXamlControlItem? ChannelUp { get; }
    public RemoteXamlControlItem? ChannelDown { get; }

    public IReadOnlyList<RemoteXamlControlItem> AllControls { get; }
    public IReadOnlyList<RemoteXamlControlItem> Favorites { get; }
    public IReadOnlyList<RemoteXamlControlItem> Media { get; }
    public IReadOnlyList<RemoteXamlControlItem> Audio { get; }
    public IReadOnlyList<RemoteXamlControlItem> Channel { get; }
    public IReadOnlyList<RemoteXamlControlItem> Navigation { get; }
    public IReadOnlyList<RemoteXamlControlItem> NavigationExtras { get; }
    public IReadOnlyList<RemoteXamlControlItem> QuickActions { get; }
    public IReadOnlyList<RemoteXamlControlItem> Apps { get; }
    public IReadOnlyList<RemoteXamlControlItem> Digits { get; }
    public IReadOnlyList<RemoteXamlControlItem> ColorKeys { get; }
    public IReadOnlyList<RemoteXamlControlItem> Extras { get; }
    public IReadOnlyList<RemoteXamlDeviceItem> Devices { get; }
    public IReadOnlyList<RemoteXamlActivityItem> Activities { get; }

    public bool HasPower => Power is not null;
    public bool HasUp => Up is not null;
    public bool HasDown => Down is not null;
    public bool HasLeft => Left is not null;
    public bool HasRight => Right is not null;
    public bool HasOk => Ok is not null;
    public bool HasBack => Back is not null;
    public bool HasHome => Home is not null;
    public bool HasMenu => Menu is not null;
    public bool HasVolumeUp => VolumeUp is not null;
    public bool HasVolumeDown => VolumeDown is not null;
    public bool HasMute => Mute is not null;
    public bool HasChannelUp => ChannelUp is not null;
    public bool HasChannelDown => ChannelDown is not null;
    public bool HasFavorites => Favorites.Count > 0;
    public bool HasMedia => Media.Count > 0;
    public bool HasAudio => Audio.Count > 0;
    public bool HasChannel => Channel.Count > 0;
    public bool HasNavigation => Navigation.Count > 0;
    public bool HasNavigationExtras => NavigationExtras.Count > 0;
    public bool HasQuickActions => QuickActions.Count > 0;
    public bool HasApps => Apps.Count > 0;
    public bool HasDigits => Digits.Count > 0;
    public bool HasColorKeys => ColorKeys.Count > 0;
    public bool HasExtras => Extras.Count > 0;
    public bool HasDevices => Devices.Count > 0;
    public bool HasActivities => Activities.Count > 0;

    public ICommand NavigateToDevicesCommand { get; }
    public ICommand NavigateToActivitiesCommand { get; }
    public ICommand NavigateToFavoritesCommand { get; }
}

public sealed class RemoteXamlControlItem
{
    private bool running;
    private readonly Command executeCommand;
    private readonly Func<RemoteUiControl, Task> executeAsync;

    public RemoteXamlControlItem(
        RemoteUiControl control,
        RemoteThemeDefinition theme,
        string iconTone,
        Func<RemoteUiControl, Task> executeAsync)
    {
        Control = control;
        this.executeAsync = executeAsync;
        Id = control.Action.Id;
        Label = RemoteLabels.Action(control);
        Icon = IconFor(control.Action.Id, iconTone);
        AutomationId = $"remote-xaml-{control.Id}";
        IsPrimary = control.Role is RemoteControlRole.Power or RemoteControlRole.Primary;
        BackgroundColor = Color.FromArgb(IsPrimary ? theme.Palette.Accent : theme.Palette.Surface);
        TextColor = Color.FromArgb(IsPrimary ? theme.Palette.OnAccent : theme.Palette.Text);
        executeCommand = new Command(async () => await ExecuteAsync().ConfigureAwait(true), () => !running);
        ExecuteCommand = executeCommand;
    }

    public RemoteUiControl Control { get; }
    public string Id { get; }
    public string Label { get; }
    public string? Icon { get; }
    public string AutomationId { get; }
    public bool IsPrimary { get; }
    public Color BackgroundColor { get; }
    public Color TextColor { get; }
    public ICommand ExecuteCommand { get; }

    private async Task ExecuteAsync()
    {
        if (running) return;
        running = true;
        executeCommand.ChangeCanExecute();
        try { await executeAsync(Control).ConfigureAwait(true); }
        finally
        {
            running = false;
            executeCommand.ChangeCanExecute();
        }
    }

    private static string? IconFor(string actionId, string tone) => actionId switch
    {
        "power.toggle" => $"ur_power_{tone}.png",
        "audio.mute.toggle" => $"ur_mute_{tone}.png",
        "navigation.home" => $"ur_home_{tone}.png",
        "navigation.back" => $"ur_back_{tone}.png",
        "navigation.menu" => $"ur_menu_{tone}.png",
        "input.select" => $"ur_source_{tone}.png",
        "apps.open" => $"ur_apps_{tone}.png",
        "media.playpause" => $"ur_play_{tone}.png",
        _ => null
    };
}

public sealed class RemoteXamlDeviceItem
{
    private bool running;
    private readonly Command selectCommand;
    private readonly Func<Guid, Task>? selectAsync;

    public RemoteXamlDeviceItem(RemoteDeviceTile tile, Func<Guid, Task>? selectAsync)
    {
        Id = tile.Id;
        Name = tile.Name;
        Room = tile.Room;
        Kind = tile.Kind;
        this.selectAsync = selectAsync;
        selectCommand = new Command(async () => await SelectAsync().ConfigureAwait(true), () => !running && this.selectAsync is not null);
        SelectCommand = selectCommand;
    }

    public Guid Id { get; }
    public string Name { get; }
    public string Room { get; }
    public string Kind { get; }
    public ICommand SelectCommand { get; }

    private async Task SelectAsync()
    {
        if (running || selectAsync is null) return;
        running = true;
        selectCommand.ChangeCanExecute();
        try { await selectAsync(Id).ConfigureAwait(true); }
        finally
        {
            running = false;
            selectCommand.ChangeCanExecute();
        }
    }
}

public sealed class RemoteXamlActivityItem
{
    private bool running;
    private readonly Command runCommand;
    private readonly Func<Guid, Task>? runAsync;

    public RemoteXamlActivityItem(RemoteActivityTile tile, Func<Guid, Task>? runAsync)
    {
        Id = tile.Id;
        Name = tile.Name;
        StepCount = tile.StepCount;
        Subtitle = RemoteLabels.Text($"{StepCount} étapes", $"{StepCount} steps");
        this.runAsync = runAsync;
        runCommand = new Command(async () => await RunAsync().ConfigureAwait(true), () => !running && this.runAsync is not null);
        RunCommand = runCommand;
    }

    public Guid Id { get; }
    public string Name { get; }
    public int StepCount { get; }
    public string Subtitle { get; }
    public ICommand RunCommand { get; }

    private async Task RunAsync()
    {
        if (running || runAsync is null) return;
        running = true;
        runCommand.ChangeCanExecute();
        try { await runAsync(Id).ConfigureAwait(true); }
        finally
        {
            running = false;
            runCommand.ChangeCanExecute();
        }
    }
}
