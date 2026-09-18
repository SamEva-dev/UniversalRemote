using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Maui.Activities;
using UniversalRemote.Maui.Compatibility;
using UniversalRemote.Maui.Discovery;
using UniversalRemote.Maui.Favorites;
using UniversalRemote.Maui.Hub;
using UniversalRemote.Maui.Media;
using UniversalRemote.Maui.Privacy;
using UniversalRemote.Maui.Remote;
using UniversalRemote.Maui.Rooms;

namespace UniversalRemote.Maui;

public sealed partial class AppShell : Shell
{
    private readonly IServiceProvider services;

    public AppShell(IServiceProvider services)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeComponent();

        // IMPORTANT Android/MAUI 10:
        // Keep the native flyout disabled before the handler is created (also set in XAML),
        // and do not eagerly construct every page during application startup.
        FlyoutBehavior = FlyoutBehavior.Disabled;

        AddRootPage<DiscoveryPage>("Appareils", "devices", eager: true);
        AddRootPage<DeviceSetupPage>("Finaliser l’appareil", "device-setup");
        AddRootPage<RoomsPage>("Pièces", "rooms");
        AddRootPage<FavoritesPage>("Favoris", "favorites");
        AddRootPage<ActivitiesPage>("Activités", "activities");
        AddRootPage<CompatibilityPage>("Compatibilité", "compatibility");
        AddRootPage<HubPage>("Hub IR", "ir-hub");
        AddRootPage<PrivacyPage>("Confidentialité", "privacy");
        AddRootPage<MediaHubPage>("Média", "media");
        AddRootPage<MediaSourcesPage>("Sources Media", "media-sources");
        AddRootPage<LiveTvPage>("TV en direct", "media-live");
        AddRootPage<MoviesPage>("Films", "media-movies");
        AddRootPage<SeriesPage>("Séries", "media-series");
        AddRootPage<MediaSearchPage>("Recherche Media", "media-search");
        AddRootPage<MediaLibraryPage>("Bibliothèque Media", "media-library");
        AddRootPage<MediaDetailsPage>("Détail Media", "media-details");
        AddRootPage<PlaybackTargetPage>("Où regarder ?", "playback-targets");
        AddRootPage<TvGuidePage>("Guide TV", "tv-guide");
        AddRootPage<MediaActivitiesPage>("Scénarios Media", "media-activities");
        AddRootPage<MediaProfilesPage>("Profils Media", "media-profiles");
        AddRootPage<PlaybackPage>("Lecteur", "media-player");
        AddRootPage<RemotePage>("Télécommande", "remote");

        AddModeNavigationToolbarItem("Contrôle", "remote", 0);
        AddModeNavigationToolbarItem("Médias", "media", 1);

#if ANDROID
        AddAndroidNavigationToolbar();
#endif
    }

    private void AddRootPage<TPage>(string title, string route, bool eager = false)
        where TPage : Page
    {
        var content = new ShellContent
        {
            Title = title,
            Route = route
        };

        // Only the first page is needed for the first frame. All other pages are materialized
        // the first time they are opened. This prevents one optional page/XAML/DI graph from
        // terminating the whole Android process during startup.
        if (eager)
            content.Content = services.GetRequiredService<TPage>();
        else
            content.ContentTemplate = new DataTemplate(() => services.GetRequiredService<TPage>());

        Items.Add(content);
    }

    private void AddModeNavigationToolbarItem(string title, string route, int priority)
    {
        var item = new ToolbarItem
        {
            Text = title,
            Order = ToolbarItemOrder.Primary,
            Priority = priority
        };

        item.Clicked += async (_, _) => await NavigateRootAsync(route);
        ToolbarItems.Add(item);
    }

#if ANDROID
    private void AddAndroidNavigationToolbar()
    {
        AddNavigationToolbarItem("Appareils", "devices", 0);
        AddNavigationToolbarItem("Pièces", "rooms", 1);
        AddNavigationToolbarItem("Favoris", "favorites", 2);
        AddNavigationToolbarItem("Activités", "activities", 3);
        AddNavigationToolbarItem("Compatibilité", "compatibility", 4);
        AddNavigationToolbarItem("Hub IR", "ir-hub", 5);
        AddNavigationToolbarItem("Confidentialité", "privacy", 6);
        AddNavigationToolbarItem("Guide TV", "tv-guide", 7);
        AddNavigationToolbarItem("Lecteur", "media-player", 8);
        AddNavigationToolbarItem("Scénarios Media", "media-activities", 9);
        AddNavigationToolbarItem("Profils Media", "media-profiles", 10);
        AddNavigationToolbarItem("Sources Media", "media-sources", 11);
    }

    private void AddNavigationToolbarItem(string title, string route, int priority)
    {
        var item = new ToolbarItem
        {
            Text = title,
            Order = ToolbarItemOrder.Secondary,
            Priority = priority
        };

        item.Clicked += async (_, _) => await NavigateRootAsync(route);
        ToolbarItems.Add(item);
    }
#endif

    private async Task NavigateRootAsync(string route)
    {
        try
        {
            await GoToAsync($"//{route}");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Record($"Navigation:{route}", ex);
            await DisplayAlertAsync("Navigation", "Impossible d’ouvrir cette page. Réessayez.", "OK");
        }
    }
}
