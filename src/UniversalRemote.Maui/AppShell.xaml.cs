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
    public AppShell(
        RemotePage remotePage,
        DiscoveryPage discoveryPage,
        RoomsPage roomsPage,
        FavoritesPage favoritesPage,
        ActivitiesPage activitiesPage,
        CompatibilityPage compatibilityPage,
        HubPage hubPage,
        PrivacyPage privacyPage,
        MediaHubPage mediaHubPage,
        LiveTvPage liveTvPage,
        MoviesPage moviesPage,
        SeriesPage seriesPage,
        MediaSearchPage mediaSearchPage,
        MediaLibraryPage mediaLibraryPage,
        MediaDetailsPage mediaDetailsPage,
        PlaybackTargetPage playbackTargetPage,
        PlaybackPage playbackPage,
        TvGuidePage tvGuidePage,
        MediaActivitiesPage mediaActivitiesPage,
        MediaProfilesPage mediaProfilesPage)
    {
        InitializeComponent();

#if ANDROID
        // .NET MAUI 10 Android can crash while constructing the native Shell flyout
        // before the first page is shown (ShellFlyoutTemplatedContentRenderer.LoadView).
        // UniversalRemote does not need the native drawer to route pages, so disable it
        // on Android and expose the same destinations through the toolbar overflow menu.
        // Windows keeps the normal Shell flyout behavior.
        FlyoutBehavior = FlyoutBehavior.Disabled;
#endif

        AddRootPage("Appareils", "devices", discoveryPage);
        AddRootPage("Pièces", "rooms", roomsPage);
        AddRootPage("Favoris", "favorites", favoritesPage);
        AddRootPage("Activités", "activities", activitiesPage);
        AddRootPage("Compatibilité", "compatibility", compatibilityPage);
        AddRootPage("Hub IR", "ir-hub", hubPage);
        AddRootPage("Confidentialité", "privacy", privacyPage);
        AddRootPage("Média", "media", mediaHubPage);
        AddRootPage("TV en direct", "media-live", liveTvPage);
        AddRootPage("Films", "media-movies", moviesPage);
        AddRootPage("Séries", "media-series", seriesPage);
        AddRootPage("Recherche Media", "media-search", mediaSearchPage);
        AddRootPage("Bibliothèque Media", "media-library", mediaLibraryPage);
        AddRootPage("Détail Media", "media-details", mediaDetailsPage);
        AddRootPage("Où regarder ?", "playback-targets", playbackTargetPage);
        AddRootPage("Guide TV", "tv-guide", tvGuidePage);
        AddRootPage("Scénarios Media", "media-activities", mediaActivitiesPage);
        AddRootPage("Profils Media", "media-profiles", mediaProfilesPage);
        AddRootPage("Lecteur", "media-player", playbackPage);
        AddRootPage("Télécommande", "remote", remotePage);

        // MEDIA 10: Control ↔ Media stays one tap away on every supported UI shell.
        AddModeNavigationToolbarItem("Contrôle", "remote", 0);
        AddModeNavigationToolbarItem("Médias", "media", 1);

#if ANDROID
        AddAndroidNavigationToolbar();
#endif
    }

    private void AddRootPage(string title, string route, Page page)
    {
        Items.Add(new ShellContent
        {
            Title = title,
            Route = route,
            Content = page
        });
    }

    private void AddModeNavigationToolbarItem(string title, string route, int priority)
    {
        var item = new ToolbarItem
        {
            Text = title,
            Order = ToolbarItemOrder.Primary,
            Priority = priority
        };

        item.Clicked += async (_, _) =>
        {
            try { await GoToAsync($"//{route}"); }
            catch (Exception)
            {
                await DisplayAlertAsync("Navigation", "Impossible d’ouvrir cette page. Réessayez.", "OK");
            }
        };

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
    }

    private void AddNavigationToolbarItem(string title, string route, int priority)
    {
        var item = new ToolbarItem
        {
            Text = title,
            Order = ToolbarItemOrder.Secondary,
            Priority = priority
        };

        item.Clicked += async (_, _) =>
        {
            try
            {
                await GoToAsync($"//{route}");
            }
            catch (Exception)
            {
                await DisplayAlertAsync(
                    "Navigation",
                    "Impossible d’ouvrir cette page. Réessayez.",
                    "OK");
            }
        };

        ToolbarItems.Add(item);
    }
#endif
}
