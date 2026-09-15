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
            }
        };
        BindingContext = viewModel;
        status.SetBinding(Label.TextProperty, nameof(RemoteViewModel.Status));
        Content = new ScrollView
        {
            Content = new VerticalStackLayout { Padding = new Thickness(16), Spacing = 16, Children = { badge, selector, host, status } }
        };
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
    private void Render()
    {
        host.Children.Clear();
        var theme = BuiltInRemoteStyles.ThemeFor(viewModel.LayoutId);
        BackgroundColor = Color.FromArgb(theme.Palette.Background);
        status.TextColor = badge.TextColor = selector.TextColor = Color.FromArgb(theme.Palette.Text);
        selector.TitleColor = Color.FromArgb(theme.Palette.Text);
        badge.Text = viewModel.IsDemo ? RemoteLabels.Text("DÉMONSTRATION", "DEMO") : "UNIVERSALREMOTE";
        if (viewModel.Model is null || !renderers.TryGetValue(viewModel.LayoutId, out var renderer)) return;
        host.Children.Add(new Label
        {
            Text = viewModel.Model.DisplayName, TextColor = Color.FromArgb(theme.Palette.Text), FontSize = 24,
            FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center
        });
        host.Children.Add(renderer.Render(viewModel.Model, theme, control =>
        {
            if (viewModel.IsBusy) return Task.CompletedTask;
            work = viewModel.ExecuteAsync(control, lifetime?.Token ?? new CancellationToken(true));
            return work;
        }));
        host.IsEnabled = !viewModel.IsBusy;
    }
}
