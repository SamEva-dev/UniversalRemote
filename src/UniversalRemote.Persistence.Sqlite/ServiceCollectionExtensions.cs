using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Persistence.Sqlite;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteSqlitePersistence(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        services.AddSingleton(new SqliteDeviceStoreOptions(databasePath));
        services.AddSingleton<SqliteDeviceRepository>();
        services.AddSingleton<IDeviceRepository>(sp => sp.GetRequiredService<SqliteDeviceRepository>());
        services.AddSingleton<IDeviceRegistrar>(sp => sp.GetRequiredService<SqliteDeviceRepository>());
        services.AddSingleton<SqliteRoomRepository>();
        services.AddSingleton<IRoomRepository>(sp => sp.GetRequiredService<SqliteRoomRepository>());
        services.AddSingleton<SqliteFavoriteRepository>();
        services.AddSingleton<IFavoriteRepository>(sp => sp.GetRequiredService<SqliteFavoriteRepository>());
        services.AddSingleton<SqliteActivityRepository>();
        services.AddSingleton<IActivityRepository>(sp => sp.GetRequiredService<SqliteActivityRepository>());
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
