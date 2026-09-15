using UniversalRemote.Maui.Discovery;
using UniversalRemote.Maui.Remote;

namespace UniversalRemote.Maui;

public sealed partial class AppShell : Shell
{
    public AppShell(RemotePage remotePage, DiscoveryPage discoveryPage)
    {
        Title = "UniversalRemote";
        Items.Add(new ShellContent
        {
            Title = "Appareils",
            Route = "devices",
            Content = discoveryPage
        });
        Items.Add(new ShellContent
        {
            Title = "Télécommande",
            Route = "remote",
            Content = remotePage
        });
    }
}
