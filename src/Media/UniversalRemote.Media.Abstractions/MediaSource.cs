namespace UniversalRemote.Media.Abstractions;

/// <summary>
/// User-configured source of media. Credentials, playlist URLs carrying tokens and provider secrets
/// are deliberately excluded from this model and referenced through CredentialReference instead.
/// </summary>
public sealed record MediaSource
{
    public Guid Id { get; }
    public string DisplayName { get; }
    public string ProviderId { get; }
    public string? CredentialReference { get; }
    public bool IsEnabled { get; }

    public MediaSource(
        Guid id,
        string displayName,
        string providerId,
        string? credentialReference = null,
        bool isEnabled = true)
    {
        if (id == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        var normalizedName = displayName.Trim();
        var normalizedProvider = providerId.Trim();
        var normalizedCredentialReference = string.IsNullOrWhiteSpace(credentialReference)
            ? null
            : credentialReference.Trim();

        if (normalizedName.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(displayName), "Media source name must be 100 characters or fewer.");
        if (normalizedProvider.Length > 80)
            throw new ArgumentOutOfRangeException(nameof(providerId), "Media provider ID must be 80 characters or fewer.");
        if (normalizedCredentialReference?.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(credentialReference), "Credential reference must be 200 characters or fewer.");

        Id = id;
        DisplayName = normalizedName;
        ProviderId = normalizedProvider;
        CredentialReference = normalizedCredentialReference;
        IsEnabled = isEnabled;
    }

    public MediaSource WithEnabled(bool isEnabled) =>
        new(Id, DisplayName, ProviderId, CredentialReference, isEnabled);
}
