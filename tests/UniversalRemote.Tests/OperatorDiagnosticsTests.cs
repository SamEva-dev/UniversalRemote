using UniversalRemote.Remote.Compatibility;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class OperatorDiagnosticsTests
{
    private static OperatorCompatibilityDiagnostics Create()
        => new(BuiltInOperatorCompatibilityCatalog.Instance, BuiltInOperatorCompatibilityQualifier.Instance);

    [Fact]
    public void Snapshot_contains_every_catalog_profile_and_keeps_research_only_explicit()
    {
        var snapshot = Create().BuildSnapshot(generatedAtUtc: new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(BuiltInOperatorCompatibilityCatalog.Instance.List().Count, snapshot.Profiles.Count);
        var box8 = Assert.Single(snapshot.Profiles.Where(x => x.ProfileId == "sfr-box8-tv"));
        Assert.Equal("ResearchOnly", box8.SupportStatus);
        Assert.Equal(CompatibilityRecipeState.ResearchOnly, box8.RecipeState);
        Assert.Null(box8.ProviderId);
        Assert.False(box8.ProviderObserved);
    }

    [Fact]
    public void Exact_bbox_model_and_firmware_make_physical_recipe_ready_without_promoting_support()
    {
        var snapshot = Create().BuildSnapshot(
        [
            new OperatorDeviceProbe(
                "androidtv",
                "Bbox 4K salon",
                ["_androidtvremote2._tcp.local"],
                new Dictionary<string, string> { ["model"] = "HMB9213NW-v2.1", ["firmware"] = "BYT.2026.09" })
        ]);

        var bbox = Assert.Single(snapshot.Profiles.Where(x => x.ProfileId == "bouygues-bbox-androidtv"));
        Assert.Equal("Experimental", bbox.SupportStatus);
        Assert.Equal("HMB9213NW-v2.1", bbox.DetectedModel);
        Assert.Equal("BYT.2026.09", bbox.Firmware);
        Assert.Equal(CompatibilityRecipeState.ReadyForPhysicalRecipe, bbox.RecipeState);
        Assert.True(bbox.ProviderObserved);
    }

    [Fact]
    public void Sfr_family_detection_without_exact_hardware_stays_incomplete()
    {
        var snapshot = Create().BuildSnapshot(
        [
            new OperatorDeviceProbe(
                "androidtv",
                "SFR Connect TV v3 salon",
                ["_androidtvremote2._tcp.local"],
                new Dictionary<string, string> { ["firmware"] = "12-test" })
        ]);

        var sfr = Assert.Single(snapshot.Profiles.Where(x => x.ProfileId == "sfr-connect-tv-androidtv"));
        Assert.Equal("Connect TV v3", sfr.DetectedFamily);
        Assert.Null(sfr.DetectedModel);
        Assert.Equal(CompatibilityRecipeState.AwaitingExactModelOrFirmware, sfr.RecipeState);
        Assert.False(sfr.RecipeState == CompatibilityRecipeState.ReadyForPhysicalRecipe);
    }

    [Fact]
    public void Dedicated_orange_provider_can_be_observed_without_inventing_model_or_firmware()
    {
        var snapshot = Create().BuildSnapshot(
        [
            new OperatorDeviceProbe(
                "orange-tv-uhd",
                "Décodeur TV Orange",
                ["_http._tcp.local"],
                new Dictionary<string, string> { ["opaque"] = "must-not-be-exported" })
        ]);

        var orange = Assert.Single(snapshot.Profiles.Where(x => x.ProfileId == "orange-tv-uhd"));
        Assert.True(orange.ProviderObserved);
        Assert.Null(orange.DetectedModel);
        Assert.Null(orange.Firmware);
        Assert.Equal(CompatibilityRecipeState.PhysicalValidationRequired, orange.RecipeState);
    }

    [Fact]
    public void Generic_android_tv_probe_is_not_misattributed_to_bbox_or_sfr()
    {
        var snapshot = Create().BuildSnapshot(
        [
            new OperatorDeviceProbe(
                "androidtv",
                "Living Room TV",
                ["_androidtvremote2._tcp.local"],
                new Dictionary<string, string> { ["model"] = "GenericGoogleTV", ["firmware"] = "x" })
        ]);

        Assert.False(snapshot.Profiles.Single(x => x.ProfileId == "bouygues-bbox-androidtv").ProviderObserved);
        Assert.False(snapshot.Profiles.Single(x => x.ProfileId == "sfr-connect-tv-androidtv").ProviderObserved);
    }

    [Fact]
    public void Json_export_is_versioned_and_does_not_export_raw_probe_metadata()
    {
        var diagnostics = Create();
        var snapshot = diagnostics.BuildSnapshot(
        [
            new OperatorDeviceProbe(
                "orange-tv-uhd",
                "Orange",
                ["_http._tcp.local"],
                new Dictionary<string, string> { ["secret"] = "never-export-me" })
        ], new DateTimeOffset(2026, 9, 15, 11, 0, 0, TimeSpan.Zero));

        var json = diagnostics.Export(snapshot, CompatibilityMatrixFormat.Json);

        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"profileId\": \"orange-tv-uhd\"", json);
        Assert.Contains("\"supportStatus\": \"Experimental\"", json);
        Assert.False(json.Contains("never-export-me", StringComparison.Ordinal));
        Assert.Equal(".json", diagnostics.FileExtension(CompatibilityMatrixFormat.Json));
    }

    [Fact]
    public void Csv_export_quotes_commas_and_contains_evidence_sources()
    {
        var diagnostics = Create();
        var snapshot = diagnostics.BuildSnapshot(generatedAtUtc: new DateTimeOffset(2026, 9, 15, 11, 0, 0, TimeSpan.Zero));

        var csv = diagnostics.Export(snapshot, CompatibilityMatrixFormat.Csv);

        Assert.True(csv.StartsWith("ProfileId,Operator,ProductFamily", StringComparison.Ordinal));
        Assert.True(csv.Contains("https://", StringComparison.Ordinal));
        Assert.True(csv.Contains("sfr-box8-tv", StringComparison.Ordinal));
        Assert.Equal(".csv", diagnostics.FileExtension(CompatibilityMatrixFormat.Csv));
    }

    [Fact]
    public void Snapshot_counters_report_detected_research_and_ready_profiles()
    {
        var snapshot = Create().BuildSnapshot(
        [
            new OperatorDeviceProbe(
                "androidtv",
                "Bbox Miami",
                ["_androidtvremote2._tcp"],
                new Dictionary<string, string> { ["model"] = "HMB4213H", ["build.id"] = "BYT-1" })
        ]);

        Assert.Equal(1, snapshot.DetectedCount);
        Assert.Equal(1, snapshot.ReadyForPhysicalRecipeCount);
        Assert.Equal(1, snapshot.ResearchOnlyCount);
    }
}
