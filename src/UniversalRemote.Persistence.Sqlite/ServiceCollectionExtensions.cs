using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Persistence.Sqlite;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteSqlitePersistence(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        services.AddSingleton(new SqliteDeviceStoreOptions(databasePath));
        services.AddSingleton<SqliteDeviceRepository>();
        services.AddSingleton<IDeviceRepository>(sp => sp.GetRequiredService<SqliteDeviceRepository>());
        services.AddSingleton<IDeviceRegistrar>(sp => sp.GetRequiredService<SqliteDeviceRepository>());
        return services;
    }
}

public sealed record SqliteDeviceStoreOptions
{
    public string DatabasePath { get; }

    public SqliteDeviceStoreOptions(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
    }
}
