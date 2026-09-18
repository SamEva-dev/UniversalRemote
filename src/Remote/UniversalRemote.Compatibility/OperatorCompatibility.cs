using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Compatibility;

public enum OperatorIntegrationMode
{
    ReuseExistingProvider,
    DedicatedProviderCandidate,
    DedicatedProvider,
    ResearchOnly
}

public enum CompatibilityEvidenceKind
{
    OfficialProductDocumentation,
    OfficialPlatformDocumentation,
    OfficialRemoteFeatureDocumentation,
    CommunityObservedProtocol,
    OpenSourceImplementation,
    ThirdPartyHardwareCatalog
}

public sealed record CompatibilityEvidence
{
    public string Title { get; }
    public Uri SourceUri { get; }
    public CompatibilityEvidenceKind Kind { get; }
    public DateOnly ReviewedOn { get; }

    public CompatibilityEvidence(string title, Uri sourceUri, CompatibilityEvidenceKind kind, DateOnly reviewedOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(sourceUri);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!sourceUri.IsAbsoluteUri
            || (!string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sourceUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Evidence URI must be an absolute HTTP(S) URI.", nameof(sourceUri));

        Title = title.Trim();
        SourceUri = sourceUri;
        Kind = kind;
        ReviewedOn = reviewedOn;
    }
}

/// <summary>
/// Product-level compatibility decision. It is evidence, not proof that a particular device/firmware works.
/// </summary>
public sealed class OperatorCompatibilityProfile
{
    public string Id { get; }
    public string Operator { get; }
    public string ProductFamily { get; }
    public IReadOnlyList<string> Models { get; }
    public string? ProviderId { get; }
    public OperatorIntegrationMode IntegrationMode { get; }
    public SupportLevel? ProposedSupportLevel { get; }
    public string Transport { get; }
    public bool RequiresPhysicalValidation { get; }
    public IReadOnlyList<CompatibilityEvidence> Evidence { get; }
    public string Decision { get; }

    public OperatorCompatibilityProfile(
        string id,
        string @operator,
        string productFamily,
        IEnumerable<string> models,
        string? providerId,
        OperatorIntegrationMode integrationMode,
        SupportLevel? proposedSupportLevel,
        string transport,
        bool requiresPhysicalValidation,
        IEnumerable<CompatibilityEvidence> evidence,
        string decision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(@operator);
        ArgumentException.ThrowIfNullOrWhiteSpace(productFamily);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(decision);
        if (!Enum.IsDefined(integrationMode)) throw new ArgumentOutOfRangeException(nameof(integrationMode));

        var modelSnapshot = models
            .Select(static x => x?.Trim())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (modelSnapshot.Length == 0) throw new ArgumentException("At least one model/family identifier is required.", nameof(models));

        var evidenceSnapshot = evidence.ToArray();
        if (evidenceSnapshot.Length == 0 || evidenceSnapshot.Any(static x => x is null))
            throw new ArgumentException("At least one evidence source is required.", nameof(evidence));

        var normalizedProviderId = string.IsNullOrWhiteSpace(providerId) ? null : providerId.Trim();
        if (integrationMode == OperatorIntegrationMode.ResearchOnly)
        {
            if (normalizedProviderId is not null)
                throw new ArgumentException("Research-only profiles cannot claim a provider implementation.", nameof(providerId));
            if (proposedSupportLevel is not null)
                throw new ArgumentException("Research-only profiles cannot claim a provider support level.", nameof(proposedSupportLevel));
        }
        else
        {
            if (normalizedProviderId is null)
                throw new ArgumentException("Implemented/reuse candidates require a provider identifier.", nameof(providerId));
            if (proposedSupportLevel is null || !Enum.IsDefined(proposedSupportLevel.Value))
                throw new ArgumentException("Implemented/reuse candidates require a valid support level.", nameof(proposedSupportLevel));
        }

        if (proposedSupportLevel == SupportLevel.Stable && requiresPhysicalValidation)
            throw new ArgumentException("A Stable compatibility profile cannot still require physical validation.", nameof(proposedSupportLevel));

        Id = id.Trim();
        Operator = @operator.Trim();
        ProductFamily = productFamily.Trim();
        Models = Array.AsReadOnly(modelSnapshot);
        ProviderId = normalizedProviderId;
        IntegrationMode = integrationMode;
        ProposedSupportLevel = proposedSupportLevel;
        Transport = transport.Trim();
        RequiresPhysicalValidation = requiresPhysicalValidation;
        Evidence = Array.AsReadOnly(evidenceSnapshot);
        Decision = decision.Trim();
    }
}

public interface IOperatorCompatibilityCatalog
{
    IReadOnlyList<OperatorCompatibilityProfile> List();
    OperatorCompatibilityProfile? Find(string id);
    IReadOnlyList<OperatorCompatibilityProfile> FindByProvider(string providerId);
}
