namespace UniversalRemote.Remote.Provider.LG;
public sealed record LgCredentials(string Host, string ClientKey, string? ServerCertificateSha256);
public interface ILgCredentialStore
{
    Task<LgCredentials?> GetAsync(string host, CancellationToken cancellationToken = default);
    Task SaveAsync(LgCredentials credentials, CancellationToken cancellationToken = default);
}
