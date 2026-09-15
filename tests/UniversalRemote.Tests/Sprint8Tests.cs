using System.Globalization;
using UniversalRemote.Abstractions;
using UniversalRemote.Presentation;
using UniversalRemote.Theming;
using Xunit;
namespace UniversalRemote.Tests;
public sealed class Sprint8Tests
{
    [Fact]
    public void SevenDistinctStylesAreAvailable()
    {
        Assert.Equal(new[] { "classic", "minimal", "nova", "elite", "horizon", "fusion", "neo" }, BuiltInRemoteStyles.Layouts.Select(x => x.Id));
        Assert.Throws<NotSupportedException>(() => ((IList<RemoteLayoutDefinition>)BuiltInRemoteStyles.Layouts).Clear());
    }
    [Theory]
    [InlineData("classic")][InlineData("minimal")][InlineData("nova")][InlineData("elite")][InlineData("horizon")][InlineData("fusion")][InlineData("neo")]
    public void EachLayoutPreservesEveryCapabilityAndDevice(string id)
    {
        var actions = new[] { RemoteActions.PowerToggle, RemoteActions.Up, RemoteActions.Down, RemoteActions.Left, RemoteActions.Right, RemoteActions.Ok, RemoteActions.VolumeUp, new RemoteAction("future.action") };
        var device = new Device(Guid.NewGuid(), "TV", [new DeviceRoute("any", "key", actions)]);
        var model = new CapabilityRemoteUiModelBuilder().Build(device);
        var snapshot = model.Controls.ToArray();
        var controls = RemoteLayoutProjection.Sections(model, BuiltInRemoteStyles.Resolve(id)).SelectMany(s => s.Controls).ToArray();
        Assert.Equal(actions.OrderBy(a => a.Id), controls.Select(c => c.Action).OrderBy(a => a.Id));
        Assert.Equal(snapshot, model.Controls); Assert.Equal(device.Id, model.DeviceId);
        Assert.All(controls, c => Assert.Contains(snapshot, x => ReferenceEquals(x, c)));
    }
    [Fact]
    public void FutureSectionsRemainVisible()
    {
        var control = new RemoteUiControl("custom", new("future.custom"), "label", RemoteControlRole.Extra, 1);
        var model = new RemoteUiModel(Guid.NewGuid(), "Future", [new("future-section", "label", [control])]);
        Assert.Same(control, Assert.Single(Assert.Single(RemoteLayoutProjection.Sections(model, BuiltInRemoteStyles.Nova)).Controls));
    }
    [Theory]
    [InlineData("classic")][InlineData("minimal")][InlineData("nova")][InlineData("elite")][InlineData("horizon")][InlineData("fusion")][InlineData("neo")]
    public void PreferencesRoundTripAndThemesRemainAccessible(string id)
    {
        var settings = RemoteLayoutPreferences.ForLayout(id);
        Assert.Equal(settings, RemoteLayoutPreferencesJson.ParseOrDefault(RemoteLayoutPreferencesJson.Serialize(settings)));
        var theme = BuiltInRemoteStyles.ThemeFor(id);
        Assert.True(theme.Tokens.MinimumTouchTargetDp >= 48);
        Assert.True(Contrast(theme.Palette.Text, theme.Palette.Surface) >= 4.5);
        Assert.True(Contrast(theme.Palette.Text, theme.Palette.Background) >= 4.5);
        Assert.True(Contrast(theme.Palette.OnAccent, theme.Palette.Accent) >= 4.5);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("{")][InlineData("null")][InlineData("[]")]
    [InlineData("{\"schemaVersion\":2,\"layoutId\":\"neo\"}")]
    [InlineData("{\"schemaVersion\":1,\"layoutId\":\"uninstalled\"}")]
    [InlineData("{\"schemaVersion\":1,\"layoutId\":null}")]
    [InlineData("{\"schemaVersion\":1,\"layoutId\":\"neo\",\"token\":\"secret\"}")]
    [InlineData("{\"schemaVersion\":1,\"layoutId\":\"neo\",\"layoutId\":\"elite\"}")]
    public void InvalidOrFuturePreferencesFallBackSafely(string? json) => Assert.Equal("classic", RemoteLayoutPreferencesJson.ParseOrDefault(json).LayoutId);
    [Fact]
    public void OversizePreferencesFallBack() => Assert.Equal(RemoteLayoutPreferences.Default, RemoteLayoutPreferencesJson.ParseOrDefault(new string('x', 2049)));
    [Fact]
    public void UnsupportedPreferenceVersionCannotBeWritten() => Assert.Throws<ArgumentException>(() => RemoteLayoutPreferencesJson.Serialize(new(2, "neo")));
    [Fact]
    public void LayoutSelectionContainsNoEndpointOrActionPayload()
    {
        Assert.Equal("{\"schemaVersion\":1,\"layoutId\":\"neo\"}", RemoteLayoutPreferencesJson.Serialize(RemoteLayoutPreferences.ForLayout("neo")));
    }
    [Theory]
    [InlineData("fr-FR", "Haut")][InlineData("en-US", "Up")]
    public void NavigationLabelsAreSpokenWords(string culture, string expected)
    {
        var saved = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = new(culture); Assert.Equal(expected, RemoteLabels.Action(new("up", RemoteActions.Up, "key", RemoteControlRole.Navigation, 1))); }
        finally { CultureInfo.CurrentUICulture = saved; }
    }
    private static double Contrast(string a, string b)
    {
        static double Luminance(string hex)
        {
            double Channel(int index) { var value = Convert.ToInt32(hex.Substring(index, 2), 16) / 255.0; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Channel(1) + .7152 * Channel(3) + .0722 * Channel(5);
        }
        var x = Luminance(a); var y = Luminance(b); return (Math.Max(x,y) + .05) / (Math.Min(x,y) + .05);
    }
}
