using UniversalRemote.Compatibility;
using UniversalRemote.Provider.AndroidTv;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class SfrConnectTvQualificationTests
{
    private readonly BuiltInOperatorCompatibilityQualifier qualifier = BuiltInOperatorCompatibilityQualifier.Instance;

    [Fact]
    public void Catalog_reuses_android_tv_and_keeps_hardware_alias_provenance_explicit()
    {
        var profile = Assert.IsType<OperatorCompatibilityProfile>(
            BuiltInOperatorCompatibilityCatalog.Instance.Find("sfr-connect-tv-androidtv"));

        Assert.Equal("androidtv", profile.ProviderId);
        Assert.Equal(OperatorIntegrationMode.ReuseExistingProvider, profile.IntegrationMode);
        Assert.Equal(SupportLevel.Experimental, profile.ProposedSupportLevel);
        Assert.Contains(profile.Models, x => x.Contains("DV8555", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.Models, x => x.Contains("DIW377", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.Evidence, x => x.Kind == CompatibilityEvidenceKind.OfficialPlatformDocumentation);
        Assert.Contains(profile.Evidence, x => x.Kind == CompatibilityEvidenceKind.ThirdPartyHardwareCatalog);
        Assert.True(profile.RequiresPhysicalValidation);
    }

    [Theory]
    [InlineData("DV8219_SFR", "Connect TV v1")]
    [InlineData("DV8555", "Connect TV v2")]
    [InlineData("DV8945-KFS", "Connect TV v3 (SDMC)")]
    [InlineData("DV8985", "Connect TV v3 (SDMC)")]
    [InlineData("DIW377 ALT FR", "Connect TV v3 (Sagemcom)")]
    public void Reviewed_hardware_alias_is_identified_from_android_tv_metadata(string model, string expectedFamily)
    {
        var result = qualifier.Qualify(new OperatorDeviceProbe(
            "androidtv",
            "SFR Connect TV",
            ["_androidtvremote2._tcp.local"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = model,
                ["build.id"] = "SFR.2026.09-test"
            }));

        Assert.NotNull(result);
        Assert.Equal(expectedFamily, result!.MatchedFamily);
        Assert.True(string.Equals(model, result.MatchedModel, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("SFR.2026.09-test", result.Firmware);
        Assert.Equal(OperatorQualificationConfidence.ExactModelAndFirmwareIdentified, result.Confidence);
        Assert.True(result.ReadyForPhysicalRecipe);
    }

    [Fact]
    public void Official_generation_label_can_identify_family_but_not_complete_exact_hardware_recipe()
    {
        var result = qualifier.Qualify(new OperatorDeviceProbe(
            "androidtv",
            "SFR Connect TV v3 Salon",
            ["_androidtvremote2._tcp.local"],
            new Dictionary<string, string> { ["firmware"] = "12-test" }));

        Assert.NotNull(result);
        Assert.Equal("Connect TV v3", result!.MatchedFamily);
        Assert.Null(result.MatchedModel);
        Assert.Equal("12-test", result.Firmware);
        Assert.Equal(OperatorQualificationConfidence.FamilyIdentified, result.Confidence);
        Assert.False(result.ReadyForPhysicalRecipe);
    }

    [Fact]
    public void Generic_connect_tv_name_without_sfr_clue_is_not_mislabeled()
    {
        var result = qualifier.Qualify(new OperatorDeviceProbe(
            "androidtv",
            "Connect TV",
            ["_androidtvremote2._tcp.local"],
            new Dictionary<string, string> { ["model"] = "GenericAndroidBox" }));

        Assert.Null(result);
    }

    [Fact]
    public void Sfr_connect_tv_without_android_tv_remote_service_is_not_qualified()
    {
        var result = qualifier.Qualify(new OperatorDeviceProbe(
            "androidtv",
            "SFR Connect TV v2",
            ["_googlecast._tcp.local"],
            new Dictionary<string, string> { ["model"] = "DV8555" }));

        Assert.Null(result);
    }

    [Fact]
    public void Sfr_recipe_matches_actions_currently_exposed_by_android_tv_provider()
    {
        var recipe = Assert.IsType<OperatorCompatibilityRecipe>(qualifier.GetRecipe("sfr-connect-tv-androidtv"));
        var providerActions = AndroidTvRemoteProvider.Capabilities.Select(x => x.Id).OrderBy(x => x).ToArray();
        var recipeActions = recipe.RequiredActionIds.OrderBy(x => x).ToArray();

        Assert.Equal(providerActions, recipeActions);
        Assert.True(recipe.RequiresPairing);
        Assert.True(recipe.RequiresExactModel);
        Assert.True(recipe.RequiresFirmware);
        Assert.Equal(300, recipe.TargetP95LatencyMs);
    }

    [Fact]
    public void Sfr_box8_profile_remains_outside_connect_tv_android_qualification()
    {
        var result = qualifier.Qualify(new OperatorDeviceProbe(
            "androidtv",
            "SFR Box 8 TV",
            ["_androidtvremote2._tcp.local"],
            new Dictionary<string, string> { ["firmware"] = "test" }));

        Assert.Null(result);
        var box8 = Assert.IsType<OperatorCompatibilityProfile>(
            BuiltInOperatorCompatibilityCatalog.Instance.Find("sfr-box8-tv"));
        Assert.Equal(OperatorIntegrationMode.ResearchOnly, box8.IntegrationMode);
        Assert.Null(box8.ProviderId);
    }
}
