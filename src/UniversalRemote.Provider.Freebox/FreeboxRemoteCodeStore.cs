namespace UniversalRemote.Provider.Freebox;

public interface IFreeboxRemoteCodeStore
{
    Task<string?> GetAsync(string deviceKey, CancellationToken cancellationToken);
    Task SaveAsync(string deviceKey, string code, CancellationToken cancellationToken);
}
