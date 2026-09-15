using UniversalRemote.Maui.Activities;
using UniversalRemote.Maui.Compatibility;
using UniversalRemote.Maui.Discovery;
using UniversalRemote.Maui.Favorites;
using UniversalRemote.Maui.Hub;
using UniversalRemote.Maui.Remote;
using UniversalRemote.Maui.Privacy;
using UniversalRemote.Maui.Rooms;

namespace UniversalRemote.Maui;

public sealed partial class AppShell : Shell
{
    public AppShell(RemotePage remotePage, DiscoveryPage discoveryPage, RoomsPage roomsPage, FavoritesPage favoritesPage, ActivitiesPage activitiesPage, CompatibilityPage compatibilityPage, HubPage hubPage, PrivacyPage privacyPage)
    {
        InitializeComponent();

        Items.Add(new ShellContent
        {
            Title = "Appareils",
            Route = "devices",
            Content = discoveryPage
        });

        Items.Add(new ShellContent
        {
            Title = "Pièces",
            Route = "rooms",
            Content = roomsPage
        });

        Items.Add(new ShellContent
        {
            Title = "Favoris",
            Route = "favorites",
            Content = favoritesPage
        });

        Items.Add(new ShellContent
        {
            Title = "Activités",
            Route = "activities",
            Content = activitiesPage
        });

        Items.Add(new ShellContent
        {
            Title = "Compatibilité",
            Route = "compatibility",
            Content = compatibilityPage
        });

        Items.Add(new ShellContent
        {
            Title = "Hub IR",
            Route = "ir-hub",
            Content = hubPage
        });

        Items.Add(new ShellContent
        {
            Title = "Confidentialité",
            Route = "privacy",
            Content = privacyPage
        });

        Items.Add(new ShellContent
        {
            Title = "Télécommande",
            Route = "remote",
            Content = remotePage
        });
    }
}
