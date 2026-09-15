using UniversalRemote.Presentation;
using UniversalRemote.Theming;
namespace UniversalRemote.Maui.Remote;

public sealed class RemotePage : ContentPage
{
    private readonly RemoteViewModel viewModel;
    private readonly RemoteLayoutPreferenceStore preferences;
    private readonly IReadOnlyDictionary<string, IRemoteLayoutRenderer> renderers;
    private readonly VerticalStackLayout host = new() { MaximumWidthRequest = 900, HorizontalOptions = LayoutOptions.Fill };
    private readonly Label status = new() { HorizontalTextAlignment = TextAlignment.Center, FontSize = 14 };
    private readonly Label badge = new() { FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Picker selector;
    private readonly Grid footer = new() { ColumnSpacing = 4, Padding = new Thickness(8, 5) };
    private readonly Button stopActivity = new() { Text = RemoteLabels.Text("Arrêter l’activité", "Stop activity"), IsVisible = false, MinimumHeightRequest = 48 };
    private CancellationTokenSource? lifetime;
    private bool restoring;
    private Task work = Task.CompletedTask;

    public RemotePage(RemoteViewModel viewModel, IEnumerable<IRemoteLayoutRenderer> renderers, RemoteLayoutPreferenceStore preferences)
    {
        this.viewModel = viewModel;
        this.preferences = preferences;
        this.renderers = renderers.ToDictionary(x => x.LayoutId, StringComparer.Ordinal);
        Title = RemoteLabels.Text("Télécommande", "Remote");
        selector = new Picker
        {
            Title = RemoteLabels.Text("Interface", "Style"),
            ItemsSource = BuiltInRemoteStyles.Layouts.Select(x => x.DisplayName).ToArray(), SelectedIndex = 0,
            HorizontalOptions = LayoutOptions.Fill, MinimumHeightRequest = 48, AutomationId = "remote-style-selector"
        };
        selector.SelectedIndexChanged += (_, _) =>
        {
            if (restoring || selector.SelectedIndex < 0 || viewModel.IsBusy) return;
            viewModel.LayoutId = BuiltInRemoteStyles.Layouts[selector.SelectedIndex].Id;
            Render();
            if (viewModel.Model is { } model)
            {
                try { preferences.Save(model.DeviceId, viewModel.LayoutId); }
                catch (Exception) { viewModel.PreferenceSaveFailed(); }
            }
        };
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RemoteViewModel.IsBusy))
            {
                selector.IsEnabled = !viewModel.IsBusy;
                host.IsEnabled = !viewModel.IsBusy;
                footer.IsEnabled = !viewModel.IsBusy;
            }
        };
        stopActivity.Clicked += (_, _) => viewModel.CancelActivity();
        stopActivity.SetBinding(IsVisibleProperty, nameof(RemoteViewModel.IsActivityRunning));
        BindingContext = viewModel;
        status.SetBinding(Label.TextProperty, nameof(RemoteViewModel.Status));
        var page = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
        page.Add(new VerticalStackLayout { Padding = new Thickness(16, 4), Spacing = 2, Children = { badge, selector } });
        page.Add(new ScrollView { Content = host, Padding = new Thickness(8, 0) }, 0, 1);
        page.Add(new VerticalStackLayout { Padding = new Thickness(14, 5), Spacing = 4, Children = { stopActivity, status } }, 0, 2);
        page.Add(footer, 0, 3);
        Content = page;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var previous = work;
        var previousLifetime = lifetime;
        previousLifetime?.Cancel();
        var current = new CancellationTokenSource();
        lifetime = current;
        work = InitializeAfterAsync(previous, previousLifetime, current);
        await work;
    }
    private async Task InitializeAfterAsync(Task previous, CancellationTokenSource? previousLifetime, CancellationTokenSource current)
    {
        try { await previous; }
        finally { previousLifetime?.Dispose(); }
        if (current.IsCancellationRequested) return;
        await viewModel.InitializeAsync(current.Token);
        if (current.IsCancellationRequested || lifetime != current) return;
        restoring = true;
        try
        {
            var value = viewModel.Model is { } model ? preferences.Load(model.DeviceId) : RemoteLayoutPreferences.Default;
            viewModel.LayoutId = value.LayoutId;
        }
        catch (Exception) { viewModel.LayoutId = "classic"; }
        finally
        {
            selector.SelectedIndex = BuiltInRemoteStyles.Layouts.ToList().FindIndex(x => x.Id == viewModel.LayoutId);
            restoring = false;
        }
        Render();
    }
    protected override void OnDisappearing()
    {
        lifetime?.Cancel();
        // Do not dispose while a transport may still register cancellation callbacks.
        base.OnDisappearing();
    }
    private Task Track(Func<Task> action)
    {
        if (viewModel.IsBusy) return Task.CompletedTask;
        work = action();
        return work;
    }
    private async Task SelectAsync(Guid id)
    {
        var current = lifetime;
        await viewModel.SelectDeviceAsync(id, current?.Token ?? new CancellationToken(true));
        if (current?.IsCancellationRequested != false || lifetime != current) return;
        // Keep Fusion/Neo open while selecting their target; persist the visible style for this target.
        if (viewModel.Model is { } model)
        {
            try { preferences.Save(model.DeviceId, viewModel.LayoutId); }
            catch (Exception) { viewModel.PreferenceSaveFailed(); }
        }
        Render();
    }
    private async Task NavigateAsync(string route)
    {
        if (viewModel.IsBusy) return;
        if (route == "styles") { selector.Focus(); return; }
        if (route == "remote") return;
        if (route is not ("devices" or "activities" or "favorites")) return;
        try { await Shell.Current.GoToAsync("//" + route); }
        catch (Exception) { await DisplayAlertAsync(RemoteLabels.Text("Navigation", "Navigation"), RemoteLabels.Text("Impossible d’ouvrir cette page. Réessayez.", "Unable to open this page. Please try again."), "OK"); }
    }

    private void BuildFooter(RemoteThemeDefinition theme)
    {
        footer.Children.Clear(); footer.ColumnDefinitions.Clear();
        var dark = viewModel.LayoutId is "elite" or "horizon" or "fusion" or "neo";
        footer.BackgroundColor = Color.FromArgb(theme.Palette.Background);
        var tone = dark ? "light" : "dark";
        var entries = new[]
        {
            ("remote", RemoteLabels.Text("Télécommande", "Remote"), "home"),
            ("devices", RemoteLabels.Text("Appareils", "Devices"), "tv"),
            (viewModel.LayoutId == "elite" ? "favorites" : "activities", viewModel.LayoutId == "elite" ? RemoteLabels.Text("Favoris", "Favorites") : RemoteLabels.Text("Activités", "Activities"), "star"),
            ("styles", "Styles", "gear")
        };
        for (var i = 0; i < entries.Length; i++)
        {
            footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var entry = entries[i];
            var button = new Button
            {
                Text = entry.Item2, FontSize = 10, Padding = 2, MinimumHeightRequest = 54,
                ImageSource = $"ur_{entry.Item3}_{tone}.png", BackgroundColor = Colors.Transparent,
                TextColor = Color.FromArgb(i == 0 ? theme.Palette.Accent : theme.Palette.Text),
                ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Top, 3),
                AutomationId = $"remote-tab-{entry.Item1}"
            };
            button.Clicked += async (_, _) => await NavigateAsync(entry.Item1);
            footer.Add(button, i);
        }
    }

    private void Render()
    {
        host.Children.Clear();
        var theme = BuiltInRemoteStyles.ThemeFor(viewModel.LayoutId);
        BackgroundColor = Color.FromArgb(theme.Palette.Background);
        status.TextColor = badge.TextColor = selector.TextColor = Color.FromArgb(theme.Palette.Text);
        selector.TitleColor = Color.FromArgb(theme.Palette.Text);
        badge.IsVisible = viewModel.IsDemo;
        BuildFooter(theme);
        badge.Text = viewModel.IsDemo ? RemoteLabels.Text("DÉMONSTRATION", "DEMO") : "UNIVERSALREMOTE";
        if (viewModel.Model is null || !renderers.TryGetValue(viewModel.LayoutId, out var renderer)) return;
        Task Execute(RemoteUiControl control)
        {
            if (viewModel.IsBusy) return Task.CompletedTask;
            work = viewModel.ExecuteAsync(control, lifetime?.Token ?? new CancellationToken(true));
            return work;
        }
        if (renderer is IContextualRemoteLayoutRenderer contextual)
        {
            var context = new RemoteSurfaceContext(viewModel.Devices, viewModel.Activities,
                id => Track(() => SelectAsync(id)),
                id => Track(() => viewModel.RunActivityAsync(id, lifetime?.Token ?? new CancellationToken(true))),
                NavigateAsync);
            host.Children.Add(contextual.Render(viewModel.Model, theme, Execute, context));
        }
        else
        {
            host.Children.Add(new Label { Text = viewModel.Model.DisplayName, FontSize = 22, TextColor = Color.FromArgb(theme.Palette.Text) });
            host.Children.Add(renderer.Render(viewModel.Model, theme, Execute));
        }
        host.IsEnabled = !viewModel.IsBusy;
    }
}
