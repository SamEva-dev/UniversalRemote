using UniversalRemote.Media.Provider.Xtream;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class XtreamM3uCompatibilityTests
{
    [Fact]
    public void Builds_standard_m3u_plus_export_for_root_server()
    {
        var credentials = new XtreamCredentials(new Uri("https://media.example.test:8443/"), "demo user", "p@ss word");

        var uri = XtreamM3uCompatibility.BuildM3uPlusPlaylistUri(credentials);

        Assert.Equal("/get.php", uri.AbsolutePath);
        Assert.Contains("username=demo%20user", uri.Query, StringComparison.Ordinal);
        Assert.Contains("password=p%40ss%20word", uri.Query, StringComparison.Ordinal);
        Assert.Contains("type=m3u_plus", uri.Query, StringComparison.Ordinal);
        Assert.Contains("output=ts", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserves_provider_base_path_and_replaces_api_suffix()
    {
        var credentials = new XtreamCredentials(new Uri("https://media.example.test/portal/player_api.php"), "u", "p");

        var uri = XtreamM3uCompatibility.BuildM3uPlusPlaylistUri(credentials, "m3u8");

        Assert.Equal("/portal/get.php", uri.AbsolutePath);
        Assert.Contains("output=m3u8", uri.Query, StringComparison.Ordinal);
    }
}
