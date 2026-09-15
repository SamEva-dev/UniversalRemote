using UniversalRemote.Maui.Discovery;
using UniversalRemote.Maui.Remote;
using UniversalRemote.Maui.Rooms;

namespace UniversalRemote.Maui;

public sealed partial class AppShell : Shell
{
    public AppShell(RemotePage remotePage, DiscoveryPage discoveryPage, RoomsPage roomsPage)
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
            Title = "Télécommande",
            Route = "remote",
            Content = remotePage
        });
    }
}
