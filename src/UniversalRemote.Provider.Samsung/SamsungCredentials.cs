namespace UniversalRemote.Remote.Provider.Samsung;

public sealed record SamsungCredentials(string Host, string Token, string? ServerCertificateSha256);

public interface ISamsungCredentialStore
{
    Task<SamsungCredentials?> GetAsync(string host, CancellationToken cancellationToken = default);
    Task SaveAsync(SamsungCredentials credentials, CancellationToken cancellationToken = default);
}
