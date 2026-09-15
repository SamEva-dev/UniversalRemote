using System.Globalization;
using System.Text;
using System.Text.Json;

namespace UniversalRemote.Compatibility;

public enum CompatibilityRecipeState
{
    NotRequired,
    ResearchOnly,
    PhysicalValidationRequired,
    AwaitingDeviceDetection,
    AwaitingExactModelOrFirmware,
    ReadyForPhysicalRecipe
}

public sealed record OperatorCompatibilityDiagnostic(
    string ProfileId,
    string Operator,
    string ProductFamily,
    string SupportStatus,
    string? ProviderId,
    OperatorIntegrationMode IntegrationMode,
    string Transport,
    bool RequiresPhysicalValidation,
    bool ProviderObserved,
    string? DetectedFamily,
    string? DetectedModel,
    string? Firmware,
    OperatorQualificationConfidence? Confidence,
    CompatibilityRecipeState RecipeState,
    DateOnly ReviewedOn,
    IReadOnlyList<CompatibilityEvidence> Evidence,
    string Decision);

public sealed record OperatorCompatibilitySnapshot(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<OperatorCompatibilityDiagnostic> Profiles)
{
    public int DetectedCount => Profiles.Count(x => x.ProviderObserved || x.Confidence is not null);
    public int ResearchOnlyCount => Profiles.Count(x => x.RecipeState == CompatibilityRecipeState.ResearchOnly);
    public int ReadyForPhysicalRecipeCount => Profiles.Count(x => x.RecipeState == CompatibilityRecipeState.ReadyForPhysicalRecipe);
}

public enum CompatibilityMatrixFormat
{
    Csv,
    Json
}

public interface IOperatorCompatibilityDiagnostics
{
    OperatorCompatibilitySnapshot BuildSnapshot(IEnumerable<OperatorDeviceProbe>? probes = null, DateTimeOffset? generatedAtUtc = null);
    string Export(OperatorCompatibilitySnapshot snapshot, CompatibilityMatrixFormat format);
    string FileExtension(CompatibilityMatrixFormat format);
}

/// <summary>
/// Builds a product-facing compatibility/status view from the reviewed catalog and runtime discovery evidence.
/// Raw discovery metadata, credentials and protocol payloads are never exported.
/// </summary>
public sealed class OperatorCompatibilityDiagnostics(
    IOperatorCompatibilityCatalog catalog,
    IOperatorCompatibilityQualifier qualifier) : IOperatorCompatibilityDiagnostics
{
    public OperatorCompatibilitySnapshot BuildSnapshot(IEnumerable<OperatorDeviceProbe>? probes = null, DateTimeOffset? generatedAtUtc = null)
    {
        var probeSnapshot = probes?.Where(static x => x is not null).ToArray() ?? Array.Empty<OperatorDeviceProbe>();
        var qualifications = probeSnapshot
            .Select(qualifier.Qualify)
            .OfType<OperatorDeviceQualification>()
            .GroupBy(x => x.Profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(static q => q.Confidence)
                      .ThenByDescending(static q => !string.IsNullOrWhiteSpace(q.Firmware))
                      .First(),
                StringComparer.OrdinalIgnoreCase);

        var rows = catalog.List()
            .Select(profile => BuildDiagnostic(profile, probeSnapshot, qualifications.GetValueOrDefault(profile.Id)))
            .OrderBy(static x => x.Operator, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.ProductFamily, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OperatorCompatibilitySnapshot(
            (generatedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            Array.AsReadOnly(rows));
    }

    public string FileExtension(CompatibilityMatrixFormat format) => format switch
    {
        CompatibilityMatrixFormat.Csv => ".csv",
        CompatibilityMatrixFormat.Json => ".json",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public string Export(OperatorCompatibilitySnapshot snapshot, CompatibilityMatrixFormat format)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return format switch
        {
            CompatibilityMatrixFormat.Csv => ExportCsv(snapshot),
            CompatibilityMatrixFormat.Json => ExportJson(snapshot),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private OperatorCompatibilityDiagnostic BuildDiagnostic(
        OperatorCompatibilityProfile profile,
        IReadOnlyList<OperatorDeviceProbe> probes,
        OperatorDeviceQualification? qualification)
    {
        var providerObserved = qualification is not null
            || (profile.IntegrationMode == OperatorIntegrationMode.DedicatedProvider
                && profile.ProviderId is not null
                && probes.Any(x => string.Equals(x.ProviderId, profile.ProviderId, StringComparison.OrdinalIgnoreCase)));

        var recipe = qualifier.GetRecipe(profile.Id);
        var recipeState = ResolveRecipeState(profile, qualification, recipe);
        var reviewedOn = profile.Evidence.Max(static x => x.ReviewedOn);
        var supportStatus = profile.ProposedSupportLevel?.ToString() ?? nameof(OperatorIntegrationMode.ResearchOnly);

        return new OperatorCompatibilityDiagnostic(
            profile.Id,
            profile.Operator,
            profile.ProductFamily,
            supportStatus,
            profile.ProviderId,
            profile.IntegrationMode,
            profile.Transport,
            profile.RequiresPhysicalValidation,
            providerObserved,
            qualification?.MatchedFamily,
            qualification?.MatchedModel,
            qualification?.Firmware,
            qualification?.Confidence,
            recipeState,
            reviewedOn,
            profile.Evidence,
            profile.Decision);
    }

    private static CompatibilityRecipeState ResolveRecipeState(
        OperatorCompatibilityProfile profile,
        OperatorDeviceQualification? qualification,
        OperatorCompatibilityRecipe? recipe)
    {
        if (profile.IntegrationMode == OperatorIntegrationMode.ResearchOnly)
            return CompatibilityRecipeState.ResearchOnly;
        if (!profile.RequiresPhysicalValidation)
            return CompatibilityRecipeState.NotRequired;
        if (qualification?.ReadyForPhysicalRecipe == true)
            return CompatibilityRecipeState.ReadyForPhysicalRecipe;
        if (qualification is not null)
            return CompatibilityRecipeState.AwaitingExactModelOrFirmware;
        if (recipe is not null)
            return CompatibilityRecipeState.AwaitingDeviceDetection;
        return CompatibilityRecipeState.PhysicalValidationRequired;
    }

    private static string ExportCsv(OperatorCompatibilitySnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ProfileId,Operator,ProductFamily,SupportStatus,IntegrationMode,ProviderId,Transport,RequiresPhysicalValidation,ProviderObserved,DetectedFamily,DetectedModel,Firmware,Confidence,RecipeState,ReviewedOn,EvidenceSources,Decision");
        foreach (var row in snapshot.Profiles)
        {
            var evidence = string.Join(" | ", row.Evidence.Select(x => $"{x.Kind}: {x.Title} ({x.SourceUri})"));
            var values = new[]
            {
                row.ProfileId,
                row.Operator,
                row.ProductFamily,
                row.SupportStatus,
                row.IntegrationMode.ToString(),
                row.ProviderId ?? string.Empty,
                row.Transport,
                row.RequiresPhysicalValidation.ToString(),
                row.ProviderObserved.ToString(),
                row.DetectedFamily ?? string.Empty,
                row.DetectedModel ?? string.Empty,
                row.Firmware ?? string.Empty,
                row.Confidence?.ToString() ?? string.Empty,
                row.RecipeState.ToString(),
                row.ReviewedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                evidence,
                row.Decision
            };
            builder.AppendLine(string.Join(',', values.Select(EscapeCsv)));
        }
        return builder.ToString();
    }

    private static string ExportJson(OperatorCompatibilitySnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("generatedAtUtc", snapshot.GeneratedAtUtc);
            writer.WriteNumber("profileCount", snapshot.Profiles.Count);
            writer.WriteNumber("detectedCount", snapshot.DetectedCount);
            writer.WriteNumber("researchOnlyCount", snapshot.ResearchOnlyCount);
            writer.WriteNumber("readyForPhysicalRecipeCount", snapshot.ReadyForPhysicalRecipeCount);
            writer.WriteStartArray("profiles");
            foreach (var row in snapshot.Profiles)
            {
                writer.WriteStartObject();
                writer.WriteString("profileId", row.ProfileId);
                writer.WriteString("operator", row.Operator);
                writer.WriteString("productFamily", row.ProductFamily);
                writer.WriteString("supportStatus", row.SupportStatus);
                writer.WriteString("integrationMode", row.IntegrationMode.ToString());
                WriteOptional(writer, "providerId", row.ProviderId);
                writer.WriteString("transport", row.Transport);
                writer.WriteBoolean("requiresPhysicalValidation", row.RequiresPhysicalValidation);
                writer.WriteBoolean("providerObserved", row.ProviderObserved);
                WriteOptional(writer, "detectedFamily", row.DetectedFamily);
                WriteOptional(writer, "detectedModel", row.DetectedModel);
                WriteOptional(writer, "firmware", row.Firmware);
                WriteOptional(writer, "confidence", row.Confidence?.ToString());
                writer.WriteString("recipeState", row.RecipeState.ToString());
                writer.WriteString("reviewedOn", row.ReviewedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteString("decision", row.Decision);
                writer.WriteStartArray("evidence");
                foreach (var evidence in row.Evidence)
                {
                    writer.WriteStartObject();
                    writer.WriteString("kind", evidence.Kind.ToString());
                    writer.WriteString("title", evidence.Title);
                    writer.WriteString("source", evidence.SourceUri.ToString());
                    writer.WriteString("reviewedOn", evidence.ReviewedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteOptional(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) writer.WriteNull(propertyName);
        else writer.WriteString(propertyName, value);
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n')) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
