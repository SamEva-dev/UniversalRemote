using System.Globalization;
using System.Text;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Compatibility;

public enum OperatorQualificationConfidence
{
    FamilyIdentified,
    ExactModelIdentified,
    ExactModelAndFirmwareIdentified
}

/// <summary>
/// Runtime evidence collected during discovery/pairing. This data may identify a product family,
/// but it does not replace physical compatibility validation on the exact model/firmware.
/// </summary>
public sealed record OperatorDeviceProbe(
    string ProviderId,
    string DisplayName,
    IReadOnlyList<string> Services,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Repeatable physical recipe attached to an operator compatibility profile.
/// Actions are normalized UniversalRemote IDs, never provider-specific payloads.
/// </summary>
public sealed class OperatorCompatibilityRecipe
{
    public string ProfileId { get; }
    public IReadOnlyList<string> RequiredActionIds { get; }
    public bool RequiresPairing { get; }
    public bool RequiresExactModel { get; }
    public bool RequiresFirmware { get; }
    public int TargetP95LatencyMs { get; }
    public string Notes { get; }

    public OperatorCompatibilityRecipe(
        string profileId,
        IEnumerable<string> requiredActionIds,
        bool requiresPairing,
        bool requiresExactModel,
        bool requiresFirmware,
        int targetP95LatencyMs,
        string notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(requiredActionIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(notes);
        if (targetP95LatencyMs <= 0 || targetP95LatencyMs > 10_000)
            throw new ArgumentOutOfRangeException(nameof(targetP95LatencyMs));

        var actions = requiredActionIds
            .Select(static x => x?.Trim())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (actions.Length == 0)
            throw new ArgumentException("At least one normalized action is required.", nameof(requiredActionIds));

        ProfileId = profileId.Trim();
        RequiredActionIds = Array.AsReadOnly(actions);
        RequiresPairing = requiresPairing;
        RequiresExactModel = requiresExactModel;
        RequiresFirmware = requiresFirmware;
        TargetP95LatencyMs = targetP95LatencyMs;
        Notes = notes.Trim();
    }
}

public sealed record OperatorDeviceQualification(
    OperatorCompatibilityProfile Profile,
    string MatchedFamily,
    string? MatchedModel,
    string? Firmware,
    OperatorQualificationConfidence Confidence,
    OperatorCompatibilityRecipe Recipe)
{
    /// <summary>
    /// True when runtime discovery is specific enough to start the physical recipe. It never means Stable support.
    /// </summary>
    public bool ReadyForPhysicalRecipe =>
        !string.IsNullOrWhiteSpace(MatchedFamily)
        && (!Recipe.RequiresExactModel || !string.IsNullOrWhiteSpace(MatchedModel))
        && (!Recipe.RequiresFirmware || !string.IsNullOrWhiteSpace(Firmware));
}

public interface IOperatorCompatibilityQualifier
{
    OperatorDeviceQualification? Qualify(OperatorDeviceProbe probe);
    OperatorCompatibilityRecipe? GetRecipe(string profileId);
}

/// <summary>
/// Built-in runtime qualifier for operator products that reuse an existing protocol provider.
/// A recognized product/model is only evidence to start a physical recipe; it is never a compatibility guarantee.
/// </summary>
public sealed class BuiltInOperatorCompatibilityQualifier : IOperatorCompatibilityQualifier
{
    public static BuiltInOperatorCompatibilityQualifier Instance { get; } = new();

    private const string BboxProfileId = "bouygues-bbox-androidtv";
    private const string SfrProfileId = "sfr-connect-tv-androidtv";
    private const string AndroidTvProviderId = "androidtv";
    private const string AndroidTvRemoteService = "_androidtvremote2._tcp";

    private static readonly (string Model, string Family)[] BboxModels =
    [
        // Most-specific first: normalized variant identifiers contain their base model token.
        ("HMB9213NW-v2.1", "Bbox 4K"),
        ("HMB9213NW-v2", "Bbox 4K"),
        ("HMB9213NW", "Bbox 4K"),
        ("UZW4020BYT4", "Bbox 4K HDR"),
        ("UZW4020BYT3", "Bbox 4K HDR"),
        ("UZW4020BYT", "Bbox 4K HDR"),
        ("HMB4213H", "Bbox Miami")
    ];

    // Hardware identifiers are treated as discovery aliases. SFR's official support confirms the v1/v2/v3
    // product generations and Android TV platform; these exact aliases are additionally sourced from a reviewed
    // third-party Android TV hardware catalog and therefore still require physical validation.
    private static readonly (string Model, string Family)[] SfrModels =
    [
        ("DV8219_SFR", "Connect TV v1"),
        ("DV8555", "Connect TV v2"),
        ("DV8945-KFS", "Connect TV v3 (SDMC)"),
        ("DV8985", "Connect TV v3 (SDMC)"),
        ("DIW377 ALT FR", "Connect TV v3 (Sagemcom)")
    ];

    private static readonly string[] FirmwareKeys =
    [
        "firmware", "firmwareversion", "firmware.version", "firmware_version",
        "build", "buildid", "build.id", "version", "androidversion", "android.version"
    ];

    private static readonly string[] AndroidTvActionIds =
    [
        RemoteActions.PowerToggle.Id,
        RemoteActions.VolumeUp.Id,
        RemoteActions.VolumeDown.Id,
        RemoteActions.MuteToggle.Id,
        RemoteActions.Up.Id,
        RemoteActions.Down.Id,
        RemoteActions.Left.Id,
        RemoteActions.Right.Id,
        RemoteActions.Ok.Id,
        RemoteActions.Back.Id,
        RemoteActions.Home.Id
    ];

    private static readonly OperatorCompatibilityRecipe BboxRecipe = new(
        BboxProfileId,
        AndroidTvActionIds,
        requiresPairing: true,
        requiresExactModel: true,
        requiresFirmware: true,
        targetP95LatencyMs: 300,
        notes: "Validate one exact Bbox model/firmware at a time through the existing Android TV provider. " +
               "Record pairing, every normalized action, failures/Unknown outcomes and p95 local latency. " +
               "Do not promote the whole operator family from a single successful device.");

    private static readonly OperatorCompatibilityRecipe SfrRecipe = new(
        SfrProfileId,
        AndroidTvActionIds,
        requiresPairing: true,
        requiresExactModel: true,
        requiresFirmware: true,
        targetP95LatencyMs: 300,
        notes: "Validate each SFR Connect TV hardware reference and firmware independently through the existing Android TV provider. " +
               "The DV*/DIW377 discovery aliases are identification hints from reviewed third-party hardware data, not an SFR API guarantee. " +
               "Record physical effects and never replay an Unknown command automatically.");

    private BuiltInOperatorCompatibilityQualifier() { }

    public OperatorCompatibilityRecipe? GetRecipe(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (string.Equals(profileId, BboxProfileId, StringComparison.OrdinalIgnoreCase)) return BboxRecipe;
        if (string.Equals(profileId, SfrProfileId, StringComparison.OrdinalIgnoreCase)) return SfrRecipe;
        return null;
    }

    public OperatorDeviceQualification? Qualify(OperatorDeviceProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrWhiteSpace(probe.ProviderId);

        if (!string.Equals(probe.ProviderId, AndroidTvProviderId, StringComparison.OrdinalIgnoreCase)) return null;
        if (!probe.Services.Any(static service => service.Contains(AndroidTvRemoteService, StringComparison.OrdinalIgnoreCase))) return null;

        var searchable = BuildSearchableText(probe);
        return QualifyBbox(probe, searchable) ?? QualifySfr(probe, searchable);
    }

    private static OperatorDeviceQualification? QualifyBbox(OperatorDeviceProbe probe, string searchable)
    {
        var modelMatch = BboxModels.FirstOrDefault(candidate => ContainsToken(searchable, candidate.Model));
        var matchedModel = string.IsNullOrWhiteSpace(modelMatch.Model) ? null : modelMatch.Model;
        var family = matchedModel is not null ? modelMatch.Family : MatchBboxFamily(searchable);
        if (family is null) return null;

        var profile = BuiltInOperatorCompatibilityCatalog.Instance.Find(BboxProfileId);
        return profile is null ? null : BuildQualification(profile, family, matchedModel, probe.Metadata, BboxRecipe);
    }

    private static OperatorDeviceQualification? QualifySfr(OperatorDeviceProbe probe, string searchable)
    {
        var modelMatch = SfrModels.FirstOrDefault(candidate => ContainsToken(searchable, candidate.Model));
        var matchedModel = string.IsNullOrWhiteSpace(modelMatch.Model) ? null : modelMatch.Model;
        var family = matchedModel is not null ? modelMatch.Family : MatchSfrFamily(searchable);
        if (family is null) return null;

        var profile = BuiltInOperatorCompatibilityCatalog.Instance.Find(SfrProfileId);
        return profile is null ? null : BuildQualification(profile, family, matchedModel, probe.Metadata, SfrRecipe);
    }

    private static OperatorDeviceQualification BuildQualification(
        OperatorCompatibilityProfile profile,
        string family,
        string? matchedModel,
        IReadOnlyDictionary<string, string>? metadata,
        OperatorCompatibilityRecipe recipe)
    {
        var firmware = FindFirmware(metadata);
        var confidence = matchedModel is null
            ? OperatorQualificationConfidence.FamilyIdentified
            : firmware is null
                ? OperatorQualificationConfidence.ExactModelIdentified
                : OperatorQualificationConfidence.ExactModelAndFirmwareIdentified;
        return new OperatorDeviceQualification(profile, family, matchedModel, firmware, confidence, recipe);
    }

    private static string? MatchBboxFamily(string searchable)
    {
        // Prefer the most specific label first so "Bbox 4K HDR" is not reduced to "Bbox 4K".
        if (ContainsToken(searchable, "bbox 4k hdr")) return "Bbox 4K HDR";
        if (ContainsToken(searchable, "bbox miami")) return "Bbox Miami";
        if (ContainsToken(searchable, "bbox 4k")) return "Bbox 4K";
        return null;
    }

    private static string? MatchSfrFamily(string searchable)
    {
        // Product-family labels are accepted only with an explicit SFR clue. A generic device named
        // "Connect TV" must not be rebranded as an SFR product solely because it runs Android TV.
        if (!ContainsToken(searchable, "sfr") || !ContainsToken(searchable, "connect tv")) return null;
        if (ContainsToken(searchable, "connect tv v3") || ContainsToken(searchable, "connect tv 3")) return "Connect TV v3";
        if (ContainsToken(searchable, "connect tv v2") || ContainsToken(searchable, "connect tv 2")) return "Connect TV v2";
        if (ContainsToken(searchable, "connect tv v1") || ContainsToken(searchable, "connect tv 1")) return "Connect TV v1";
        return "SFR Connect TV";
    }

    private static string? FindFirmware(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return null;
        foreach (var pair in metadata)
        {
            var key = NormalizeKey(pair.Key);
            if (!FirmwareKeys.Any(candidate => string.Equals(NormalizeKey(candidate), key, StringComparison.Ordinal))) continue;
            var value = pair.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static string BuildSearchableText(OperatorDeviceProbe probe)
    {
        var parts = new List<string> { probe.DisplayName ?? string.Empty };
        if (probe.Metadata is not null)
        {
            foreach (var pair in probe.Metadata)
            {
                parts.Add(pair.Key);
                parts.Add(pair.Value);
            }
        }
        return Normalize(string.Join(' ', parts));
    }

    private static bool ContainsToken(string normalizedHaystack, string token)
        => normalizedHaystack.Contains(Normalize(token), StringComparison.Ordinal);

    private static string NormalizeKey(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }
        return builder.ToString().Trim();
    }
}
