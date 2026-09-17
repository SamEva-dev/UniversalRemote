using Microsoft.Maui.Controls.Shapes;
using UniversalRemote.Remote.Presentation;
using UniversalRemote.Remote.Theming;

namespace UniversalRemote.Maui.Remote;

/// <summary>Six compositions from the project reference. Each render owns its state; no device/provider logic lives here.</summary>
public sealed class ReferenceRemoteRenderer(string layoutId) : IContextualRemoteLayoutRenderer
{
    public string LayoutId { get; } = layoutId;
    public View Render(RemoteUiModel model, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync)
        => new Surface(LayoutId, model, theme, executeAsync, null).Build();
    public View Render(RemoteUiModel model, RemoteThemeDefinition theme, Func<RemoteUiControl, Task> executeAsync, RemoteSurfaceContext context)
        => new Surface(LayoutId, model, theme, executeAsync, context).Build();

    private sealed class Surface(string id, RemoteUiModel model, RemoteThemeDefinition theme,
        Func<RemoteUiControl, Task> execute, RemoteSurfaceContext? context)
    {
        private readonly HashSet<string> placed = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
        private readonly bool dark = id is "elite" or "horizon" or "fusion" or "neo";
        private string Ink => theme.Palette.Text;
        private string Face => theme.Palette.Surface;
        private string Tone => dark ? "light" : "dark";
        private static string T(string fr, string en) => RemoteLabels.Text(fr, en);
        private static Color C(string hex) => Color.FromArgb(hex);
        private static LinearGradientBrush Gradient(string from, string to) => new()
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection { new() { Color = C(from), Offset = 0 }, new() { Color = C(to), Offset = 1 } }
        };
        private Label Text(string text, double size = 14, bool bold = false) => new()
        {
            Text = text, TextColor = C(Ink), FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            LineBreakMode = LineBreakMode.WordWrap
        };
        private static VerticalStackLayout Stack(double spacing = 12) => new() { Spacing = spacing };
        private Border Card(View child, double radius = 22, double padding = 14, Brush? background = null) => new()
        {
            Content = child, Padding = padding, StrokeThickness = dark ? .6 : 1,
            Stroke = new SolidColorBrush(C(dark ? "#354153" : "#DDE1E7")),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(radius) },
            Background = background ?? new SolidColorBrush(C(Face))
        };
        private static Grid Columns(int count, params View[] children)
        {
            var grid = new Grid { ColumnSpacing = 9, RowSpacing = 9 };
            for (var i = 0; i < count; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (var i = 0; i < (children.Length + count - 1) / count; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var i = 0; i < children.Length; i++) grid.Add(children[i], i % count, i / count);
            return grid;
        }
        private Button Local(string text, Func<Task>? action, string? icon = null)
        {
            var button = new Button
            {
                Text = text, TextColor = C(Ink), FontSize = 13, BackgroundColor = C(Face),
                MinimumHeightRequest = 48, MinimumWidthRequest = 48, CornerRadius = 17,
                Padding = new Thickness(8), IsEnabled = action is not null,
                ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Top, 5)
            };
            if (icon is not null) button.ImageSource = $"ur_{icon}_{Tone}.png";
            if (action is not null) button.Clicked += async (_, _) =>
            {
                if (!button.IsEnabled) return;
                button.IsEnabled = false;
                try { await action(); }
                finally { button.IsEnabled = true; }
            };
            return button;
        }
        private Button Key(string actionId, string? text = null, string? icon = null, double height = 48, bool round = false)
        {
            var control = model.Controls.FirstOrDefault(x => x.Action.Id == actionId);
            if (control is not null) placed.Add(actionId);
            var label = control is null ? text ?? actionId : RemoteLabels.Action(control);
            var button = Local(text ?? label, control is null ? null : () => execute(control), icon);
            button.MinimumHeightRequest = height;
            button.CornerRadius = round ? 150 : id == "classic" ? 16 : 18;
            occurrences.TryGetValue(actionId, out var occurrence);
            occurrences[actionId] = occurrence + 1;
            button.AutomationId = $"concept-{id}-{actionId}-{occurrence}";
            button.Opacity = control is null ? .35 : 1;
            if (control is null) SemanticProperties.SetDescription(button, label + T(" — non pris en charge", " — unsupported"));
            else SemanticProperties.SetDescription(button, label);
            if (id == "elite")
            {
                button.Background = Gradient("#353A40", "#0A0D10");
                button.BorderWidth = .8; button.BorderColor = C("#576069");
                button.Shadow = new Shadow { Brush = Brush.Black, Offset = new Point(0, 4), Radius = 6, Opacity = .65f };
            }
            else if (id == "nova" || id == "classic") button.Background = Gradient("#FAFBFC", "#DFE2E6");
            if (actionId == "power.toggle")
            {
                button.TextColor = C(dark ? "#F06464" : "#D62D35");
                if (icon is not null && id is not ("horizon" or "neo")) button.ImageSource = "ur_power_red.png";
            }
            return button;
        }
        private View Header()
        {
            var name = Stack(3);
            name.Children.Add(Text(model.DisplayName, 19, true));
            var room = context?.Devices.FirstOrDefault(d => d.Id == model.DeviceId)?.Room;
            name.Children.Add(Text(string.IsNullOrWhiteSpace(room) ? T("Appareil sélectionné", "Selected device") : room, 12));
            var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 };
            grid.Add(name);
            var power = Key("power.toggle", "", "power", round: true); power.WidthRequest = 52;
            grid.Add(power, 1);
            return grid;
        }
        private View Sources() => Columns(4,
            Key("input.tv", "TV", "tv"), Key("input.hdmi1", "HDMI 1", "source"),
            Key("input.hdmi2", "HDMI 2", "source"), Key("apps.open", "Apps", "apps"));
        private View DirectionPad(double size = 224)
        {
            var grid = new Grid
            {
                WidthRequest = size, HeightRequest = size, HorizontalOptions = LayoutOptions.Center,
                RowDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) },
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) }
            };
            var bezel = Card(new Grid(), size / 2, 0,
                dark ? Gradient("#454C53", "#090D10") : Gradient("#FFFFFF", "#D9DDE1"));
            Grid.SetRowSpan(bezel, 3); Grid.SetColumnSpan(bezel, 3); grid.Add(bezel);
            Add("navigation.up", "⌃", 1, 0); Add("navigation.left", "‹", 0, 1);
            Add("navigation.ok", "OK", 1, 1); Add("navigation.right", "›", 2, 1); Add("navigation.down", "⌄", 1, 2);
            return grid;
            void Add(string action, string label, int column, int row)
            {
                var key = Key(action, label, round: true); key.FontSize = label == "OK" ? 20 : 30;
                key.Margin = 3; key.Padding = 0;
                if (label != "OK") { key.Background = Brush.Transparent; key.BorderWidth = 0; key.Shadow = null; }
                grid.Add(key, column, row);
            }
        }
        private View NavRow() => Columns(3, Key("navigation.back", "", "back", round: true),
            Key("navigation.home", "", "home", round: true), Key("navigation.menu", "", "menu", round: true));
        private View Rocker(string up, string down, string label)
        {
            var stack = Stack(1);
            stack.Children.Add(Key(up, "+", height: 48, round: true));
            var title = Text(label, 12, true); title.HorizontalTextAlignment = TextAlignment.Center;
            stack.Children.Add(title); stack.Children.Add(Key(down, "−", height: 48, round: true));
            return Card(stack, 32, 2, dark ? Gradient("#30363D", "#0A0E13") : Gradient("#E2E5E9", "#F7F8FA"));
        }
        private View Rockers(bool compact = false)
        {
            var middle = compact ? DirectionPad(148) : Key("audio.mute.toggle", "", "mute", height: 58, round: true);
            middle.VerticalOptions = LayoutOptions.Center;
            var grid = new Grid { ColumnSpacing = compact ? 6 : 10, ColumnDefinitions = { new(new GridLength(compact ? 48 : 64)), new(GridLength.Star), new(new GridLength(compact ? 48 : 64)) } };
            grid.Add(Rocker("volume.up", "volume.down", "VOL")); grid.Add(middle, 1);
            grid.Add(Rocker("channel.up", "channel.down", "CH"), 2);
            return grid;
        }
        private View Applications()
        {
            var items = new[] { ("app.netflix", "NETFLIX", "#E42B33"), ("app.youtube", "YouTube", "#B5252D"),
                ("app.primevideo", "prime video", "#08628E"), ("app.disneyplus", "Disney+", "#1A3579") };
            var row = Columns(id == "horizon" ? 2 : 4, items.Select(item =>
            {
                var key = Key(item.Item1, item.Item2, height: 60); key.FontSize = id == "horizon" ? 15 : 10;
                key.TextColor = C(item.Item3); key.Background = Brush.White; return (View)key;
            }).ToArray());
            return row;
        }
        private View Keypad()
        {
            var keys = Enumerable.Range(1, 9).Select(n => (View)Key($"digit.{n}", n.ToString())).ToList();
            keys.Add(Key("digit.separator", "−")); keys.Add(Key("digit.0", "0")); keys.Add(Key("text.delete", "⌫"));
            return Columns(3, keys.ToArray());
        }
        private View Classic()
        {
            var root = Stack(14); root.Children.Add(Header()); root.Children.Add(Sources()); root.Children.Add(Keypad());
            root.Children.Add(Rockers(true));
            root.Children.Add(Columns(2, Key("navigation.menu", "Menu"), Key("navigation.guide", "Guide"),
                Key("navigation.back", T("Retour", "Back")), Key("navigation.exit", "Exit")));
            root.Children.Add(Columns(4, new[] { ("red", "#E6342E"), ("green", "#12A34D"), ("yellow", "#F5BC2E"), ("blue", "#0A92D5") }
                .Select(item => { var b = Key("key." + item.Item1, "", round: true); b.Background = new SolidColorBrush(C(item.Item2)); return (View)b; }).ToArray()));
            return root;
        }
        private View Nova()
        {
            var root = Stack(20); root.Children.Add(Header()); root.Children.Add(Sources()); root.Children.Add(DirectionPad());
            root.Children.Add(NavRow()); root.Children.Add(Rockers()); root.Children.Add(Applications()); return root;
        }
        private View Elite()
        {
            var root = Stack(24); root.Children.Add(Header()); root.Children.Add(DirectionPad(248));
            root.Children.Add(NavRow()); root.Children.Add(Rockers());
            var drawer = Card(Keypad()); drawer.IsVisible = false;
            var keyboard = Local(T("Clavier", "Keypad"), () => { drawer.IsVisible = !drawer.IsVisible; return Task.CompletedTask; }, "keyboard");
            root.Children.Add(Columns(3, Key("input.select", T("Sources", "Inputs"), "source"), keyboard,
                Key("apps.open", "Apps", "apps")));
            root.Children.Add(drawer); return root;
        }
        private View Horizon()
        {
            var root = Stack(14); root.Children.Add(Header());
            var scene = new Grid { HeightRequest = 230 };
            var image = new Image { Source = "horizon_landscape.png", Aspect = Aspect.AspectFill };
            SemanticProperties.SetDescription(image, T("Paysage décoratif de montagne et de lac", "Decorative mountain and lake landscape"));
            scene.Add(image);
            var screen = Card(new Image { Source = "horizon_landscape.png", Aspect = Aspect.AspectFill }, 3, 3, Brush.Black);
            screen.WidthRequest = 168; screen.HeightRequest = 100; screen.HorizontalOptions = LayoutOptions.Center; screen.VerticalOptions = LayoutOptions.Center;
            scene.Add(screen);
            var caption = Card(Text(model.DisplayName, 15, true), 16, 12, new SolidColorBrush(C("#B00A2134")));
            caption.VerticalOptions = LayoutOptions.End; caption.Margin = 12; scene.Add(caption);
            root.Children.Add(Card(scene, 24, 0));
            root.Children.Add(Columns(2, Key("power.toggle", T("Marche / arrêt", "Power"), "power", 78),
                Key("input.select", T("Sources", "Inputs"), "source", 78), Key("navigation.menu", "Menu", "menu", 78),
                Key("navigation.back", T("Retour", "Back"), "back", 78)));
            root.Children.Add(Text("Applications", 16, true)); root.Children.Add(Applications());
            root.Children.Add(DirectionPad(206)); root.Children.Add(Rockers()); return root;
        }
        private View DeviceCards(bool orbit)
        {
            var root = Stack(10);
            if (context is null || context.Devices.Count == 0)
            {
                root.Children.Add(Text(T("Ajoutez vos appareils pour les retrouver ici.", "Add your devices to see them here.")));
                root.Children.Add(Local(T("Ajouter un appareil", "Add device"), context is null ? null : () => context.Navigate("devices"), "tv"));
                return root;
            }
            var visibleDevices = orbit ? context.Devices.Take(4) : context.Devices;
            var cards = visibleDevices.Select(device =>
            {
                var button = Local(device.Name + (string.IsNullOrWhiteSpace(device.Room) ? "" : "\n" + device.Room), () => context.SelectDevice(device.Id), device.Kind);
                button.AutomationId = $"concept-device-{device.Id:N}";
                button.MinimumHeightRequest = orbit ? 82 : 84;
                button.Background = Gradient(device.Id == model.DeviceId ? "#203D60" : "#202A37", "#131B28");
                if (device.Id == model.DeviceId) { button.BorderWidth = 1.5; button.BorderColor = C(theme.Palette.Accent); }
                if (!orbit) button.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 18);
                return (View)button;
            }).ToArray();
            root.Children.Add(Columns(orbit ? 2 : 1, cards));
            if (orbit && context.Devices.Count > 4) root.Children.Add(Local(T("Tous les appareils", "All devices"), () => context.Navigate("devices"), "tv"));
            return root;
        }
        private View Scenes()
        {
            var root = Stack(10); root.Children.Add(Text(T("Ambiances", "Scenes"), 16, true));
            if (context is not null && context.Activities.Count > 0)
            {
                root.Children.Add(Columns(2, context.Activities.Select((activity, index) =>
                {
                    var button = Local(activity.Name, () => context.RunActivity(activity.Id), new[] { "film", "tv", "music", "game" }[index % 4]);
                    button.MinimumHeightRequest = 84; button.AutomationId = $"concept-activity-{activity.Id:N}";
                    button.Background = Gradient("#244366", "#172138");
                    SemanticProperties.SetDescription(button, T($"Exécuter {activity.Name}, {activity.StepCount} étapes", $"Run {activity.Name}, {activity.StepCount} steps"));
                    return (View)button;
                }).ToArray()));
            }
            else root.Children.Add(Text(T("Créez une activité Film, Musique ou Jeux avec vos propres appareils.", "Create a Movie, Music or Games activity with your own devices."), 13));
            root.Children.Add(Local(T("Gérer les activités", "Manage activities"), context is null ? null : () => context.Navigate("activities"), "star"));
            return root;
        }
        private View Fusion()
        {
            var root = Stack(20); root.Children.Add(Text("Universal Remote", 23, true));
            root.Children.Add(Text(T("Tous vos appareils, au même endroit.", "All your devices, in one place."), 13));
            root.Children.Add(DeviceCards(false));
            root.Children.Add(Columns(2, Local(T("Scènes", "Scenes"), context is null ? null : () => context.Navigate("activities"), "star"),
                Local("Macros", context is null ? null : () => context.Navigate("activities"), "play")));
            root.Children.Add(Scenes());
            root.Children.Add(Text(model.DisplayName, 17, true)); root.Children.Add(NavRow()); root.Children.Add(Rockers());
            return root;
        }
        private View Neo()
        {
            var root = Stack(20); root.Children.Add(Text(model.DisplayName, 20, true)); root.Children.Add(DeviceCards(true));
            var power = Key("power.toggle", "", "power", 158, true); power.WidthRequest = 158;
            power.Background = Gradient("#13294C", "#11132D");
            power.ImageSource = "ur_power_large_light.png";
            var ring = Card(power, 110, 6, Gradient("#07C1FF", "#8A31F4"));
            ring.StrokeThickness = 0; ring.WidthRequest = 178; ring.HeightRequest = 178; ring.HorizontalOptions = LayoutOptions.Center;
            ring.Shadow = new Shadow { Brush = new SolidColorBrush(C("#6750FF")), Offset = new Point(0, 0), Radius = 28, Opacity = .8f };
            root.Children.Add(ring); root.Children.Add(Scenes()); root.Children.Add(NavRow()); root.Children.Add(DirectionPad(206)); root.Children.Add(Rockers());
            return root;
        }
        public View Build()
        {
            var root = Stack(18); root.Padding = new Thickness(18, 20); root.MaximumWidthRequest = 460; root.HorizontalOptions = LayoutOptions.Fill;
            root.Background = id == "neo" ? Gradient("#09283C", "#130D2B") : new SolidColorBrush(C(theme.Palette.Background));
            root.Children.Add(id switch { "classic" => Classic(), "elite" => Elite(), "horizon" => Horizon(), "fusion" => Fusion(), "neo" => Neo(), _ => Nova() });
            // Never drop new provider actions or custom IR commands that have no dedicated reference slot.
            var other = model.Controls.Where(x => !placed.Contains(x.Action.Id)).ToArray();
            if (other.Length > 0)
            {
                var extras = Stack(8); extras.Children.Add(Text(T("Autres commandes", "More controls"), 14, true));
                extras.Children.Add(Columns(3, other.Select(x => (View)Key(x.Action.Id)).ToArray()));
                root.Children.Add(extras);
            }
            if (model.Favorites.Count > 0)
            {
                root.Children.Add(Text(T("Favoris", "Favorites"), 14, true));
                root.Children.Add(Columns(2, model.Favorites.Select(x => (View)Key(x.Action.Id)).ToArray()));
            }
            var note = Text(T("Touches grisées : non prises en charge par cet appareil.", "Dimmed controls are not supported by this device."), 11);
            note.Opacity = .8; root.Children.Add(note);
            return root;
        }
    }
}
