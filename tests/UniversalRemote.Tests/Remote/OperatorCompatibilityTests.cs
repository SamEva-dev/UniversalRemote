using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Compatibility;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class OperatorCompatibilityTests
{
    private readonly IOperatorCompatibilityCatalog catalog = BuiltInOperatorCompatibilityCatalog.Instance;

    [Fact]
    public void Catalog_contains_the_three_target_operators()
    {
        var operators = catalog.List().Select(x => x.Operator).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Orange", operators);
        Assert.Contains("Bouygues Telecom", operators);
        Assert.Contains("SFR", operators);
    }

    [Fact]
    public void Bbox_reuses_android_tv_instead_of_duplicating_a_provider()
    {
        var profile = Assert.IsType<OperatorCompatibilityProfile>(catalog.Find("bouygues-bbox-androidtv"));
        Assert.Equal(OperatorIntegrationMode.ReuseExistingProvider, profile.IntegrationMode);
        Assert.Equal("androidtv", profile.ProviderId);
        Assert.Equal(SupportLevel.Experimental, profile.ProposedSupportLevel);
        Assert.True(profile.RequiresPhysicalValidation);
    }

    [Fact]
    public void Sfr_connect_tv_reuses_android_tv()
    {
        var profile = Assert.IsType<OperatorCompatibilityProfile>(catalog.Find("sfr-connect-tv-androidtv"));
        Assert.Equal(OperatorIntegrationMode.ReuseExistingProvider, profile.IntegrationMode);
        Assert.Equal("androidtv", profile.ProviderId);
        Assert.Contains(profile.Models, x => x.Contains("v3", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sfr_box8_does_not_claim_an_unverified_provider()
    {
        var profile = Assert.IsType<OperatorCompatibilityProfile>(catalog.Find("sfr-box8-tv"));
        Assert.Equal(OperatorIntegrationMode.ResearchOnly, profile.IntegrationMode);
        Assert.Null(profile.ProviderId);
        Assert.Null(profile.ProposedSupportLevel);
    }

    [Fact]
    public void Orange_is_a_dedicated_experimental_candidate_only()
    {
        var profile = Assert.IsType<OperatorCompatibilityProfile>(catalog.Find("orange-tv-uhd"));
        Assert.Equal(OperatorIntegrationMode.DedicatedProvider, profile.IntegrationMode);
        Assert.Equal("orange-tv-uhd", profile.ProviderId);
        Assert.Equal(SupportLevel.Experimental, profile.ProposedSupportLevel);
        Assert.Contains(profile.Evidence, x => x.Kind == CompatibilityEvidenceKind.CommunityObservedProtocol);
        Assert.True(profile.RequiresPhysicalValidation);
    }

    [Fact]
    public void Stable_profile_cannot_still_require_physical_validation()
    {
        Assert.Throws<ArgumentException>(() => new OperatorCompatibilityProfile(
            "invalid", "Operator", "Family", ["Model"], "provider",
            OperatorIntegrationMode.ReuseExistingProvider, SupportLevel.Stable,
            "LAN", true,
            [new CompatibilityEvidence("Evidence", new Uri("https://example.invalid"), CompatibilityEvidenceKind.OfficialProductDocumentation, new DateOnly(2026, 9, 15))],
            "Invalid by design"));
    }
}
