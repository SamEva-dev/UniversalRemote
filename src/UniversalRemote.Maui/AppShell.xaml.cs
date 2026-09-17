using UniversalRemote.Maui.Activities;
using UniversalRemote.Maui.Compatibility;
using UniversalRemote.Maui.Discovery;
using UniversalRemote.Maui.Favorites;
using UniversalRemote.Maui.Hub;
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
        PrivacyPage privacyPage)
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
        AddRootPage("Télécommande", "remote", remotePage);

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

#if ANDROID
    private void AddAndroidNavigationToolbar()
    {
        AddNavigationToolbarItem("Appareils", "devices", 0);
        AddNavigationToolbarItem("Télécommande", "remote", 1);
        AddNavigationToolbarItem("Pièces", "rooms", 2);
        AddNavigationToolbarItem("Favoris", "favorites", 3);
        AddNavigationToolbarItem("Activités", "activities", 4);
        AddNavigationToolbarItem("Compatibilité", "compatibility", 5);
        AddNavigationToolbarItem("Hub IR", "ir-hub", 6);
        AddNavigationToolbarItem("Confidentialité", "privacy", 7);
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
