namespace UniversalRemote.Remote.Provider.AndroidTv;

public sealed record AndroidTvCredentials(
    string Host,
    string PfxBase64,
    string Password,
    string ServerCertificateSha256);

public interface IAndroidTvCredentialStore
{
    Task<AndroidTvCredentials?> GetAsync(string host, CancellationToken cancellationToken);
    Task SaveAsync(AndroidTvCredentials credentials, CancellationToken cancellationToken);
}

/// <summary>Tests/samples only. MAUI uses secure platform storage.</summary>
public sealed class InMemoryAndroidTvCredentialStore : IAndroidTvCredentialStore
{
    private readonly Dictionary<string, AndroidTvCredentials> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<AndroidTvCredentials?> GetAsync(string host, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return values.GetValueOrDefault(host); }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(AndroidTvCredentials credentials, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { values[credentials.Host] = credentials; }
        finally { gate.Release(); }
    }
}
