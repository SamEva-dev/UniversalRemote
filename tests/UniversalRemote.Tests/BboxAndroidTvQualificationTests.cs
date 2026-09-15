using UniversalRemote.Compatibility;
using UniversalRemote.Provider.AndroidTv;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class BboxAndroidTvQualificationTests
{
    private readonly BuiltInOperatorCompatibilityQualifier qualifier = BuiltInOperatorCompatibilityQualifier.Instance;

    [Fact]
    public void Catalog_tracks_official_bbox_hardware_variants_without_a_duplicate_provider()
    {
        var profile = Assert.IsType<OperatorCompatibilityProfile>(
            BuiltInOperatorCompatibilityCatalog.Instance.Find("bouygues-bbox-androidtv"));

        Assert.Equal("androidtv", profile.ProviderId);
        Assert.Equal(OperatorIntegrationMode.ReuseExistingProvider, profile.IntegrationMode);
        Assert.Contains("HMB4213H", profile.Models);
        Assert.Contains("HMB9213NW-v2.1", profile.Models);
        Assert.Contains("UZW4020BYT4", profile.Models);
        Assert.True(profile.RequiresPhysicalValidation);
    }

    [Theory]
    [InlineData("HMB4213H", "Bbox Miami")]
    [InlineData("HMB9213NW", "Bbox 4K")]
    [InlineData("HMB9213NW-v2.1", "Bbox 4K")]
    [InlineData("UZW4020BYT", "Bbox 4K HDR")]
    [InlineData("UZW4020BYT4", "Bbox 4K HDR")]
    public void Exact_model_is_identified_from_android_tv_discovery_metadata(string model, string expectedFamily)
    {
        var result = qualifier.Qualify(new OperatorDeviceProbe(
            "androidtv",
            "Bouygues TV",
            ["_androidtvremote2._tcp.local"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = model,
                ["firmwareVersion"] = "2026.09-test"
            }));

        Assert.NotNull(result);
        Assert.Equal(expectedFamily, result!.MatchedFamily);
        Assert.True(string.Equals(model, result.MatchedModel, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("2026.09-test", result.Firmware);
        Assert.Equal(OperatorQualificationConfidence.ExactModelAndFirmwareIdentified, result.Confidence);
        Assert.True(result.ReadyForPhysicalRecipe);
    }

    [Fact]
    public void Family_name_can_be_recognized_but_does_not_replace_exact_model_and_firmware()
    {
        var result = qualifier.Qualify(new OperatorDeviceProbe(
            "androidtv",
            "Bbox 4K HDR Salon",
            ["_androidtvremote2._tcp.local"]));

        Assert.NotNull(result);
        Assert.Equal("Bbox 4K HDR", result!.MatchedFamily);
        Assert.Null(result.MatchedModel);
        Assert.Null(result.Firmware);
        Assert.Equal(OperatorQualificationConfidence.FamilyIdentified, result.Confidence);
        Assert.False(result.ReadyForPhysicalRecipe);
    }

    [Fact]
    public void Bbox_name_without_android_tv_remote_service_is_not_qualified()
    {
        var result = qualifier.Qualify(new OperatorDeviceProbe(
            "androidtv",
            "Bbox 4K",
            ["_googlecast._tcp.local"],
            new Dictionary<string, string> { ["model"] = "HMB9213NW" }));

        Assert.Null(result);
    }

    [Fact]
    public void Generic_android_tv_is_not_mislabeled_as_bbox()
    {
        var result = qualifier.Qualify(new OperatorDeviceProbe(
            "androidtv",
            "Living Room TV",
            ["_androidtvremote2._tcp.local"],
            new Dictionary<string, string> { ["model"] = "GenericGoogleTV" }));

        Assert.Null(result);
    }

    [Fact]
    public void Bbox_recipe_matches_the_actions_currently_exposed_by_android_tv_provider()
    {
        var recipe = Assert.IsType<OperatorCompatibilityRecipe>(qualifier.GetRecipe("bouygues-bbox-androidtv"));
        var providerActions = AndroidTvRemoteProvider.Capabilities.Select(x => x.Id).OrderBy(x => x).ToArray();
        var recipeActions = recipe.RequiredActionIds.OrderBy(x => x).ToArray();

        Assert.Equal(providerActions, recipeActions);
        Assert.True(recipe.RequiresPairing);
        Assert.True(recipe.RequiresExactModel);
        Assert.True(recipe.RequiresFirmware);
        Assert.Equal(300, recipe.TargetP95LatencyMs);
    }

    [Fact]
    public void Firmware_can_be_extracted_from_common_discovery_keys_case_insensitively()
    {
        var result = qualifier.Qualify(new OperatorDeviceProbe(
            "androidtv",
            "Bbox Miami",
            ["_androidtvremote2._tcp"],
            new Dictionary<string, string>
            {
                ["MODEL"] = "HMB4213H",
                ["Build.Id"] = "BYT.2026.09.15"
            }));

        Assert.NotNull(result);
        Assert.Equal("BYT.2026.09.15", result!.Firmware);
        Assert.True(result.ReadyForPhysicalRecipe);
    }
}
