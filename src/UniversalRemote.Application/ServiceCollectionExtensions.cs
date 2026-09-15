using DomainRelay.Abstractions;
using DomainRelay.DependencyInjection;
using DomainRelay.Mapping.DependencyInjection.Extensions;
using DomainRelay.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Discovery;
using UniversalRemote.Presentation;
using UniversalRemote.Abstractions;
using UniversalRemote.Core;

namespace UniversalRemote.Application;

/// <summary>Call once per container. Explicit handler registration limits assembly scanning; AOT still requires a device test.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteApplication(this IServiceCollection services, Action<CommandOptions>? configure = null)
    {
        if (services.Any(s => s.ServiceType == typeof(RegistrationMarker))) return services;
        services.AddSingleton(new RegistrationMarker());
        services.AddUniversalRemoteCore(configure);
        services.AddUniversalRemoteDiscovery();
        services.TryAddSingleton<ITelemetryRecorder>(_ => NullTelemetryRecorder.Instance);
        services.TryAddSingleton<IRemoteUiModelBuilder, CapabilityRemoteUiModelBuilder>();
        services.TryAddSingleton<IFavoriteRepository, InMemoryFavoriteRepository>();
        services.TryAddSingleton<IActivityRepository, InMemoryActivityRepository>();
        services.AddDomainRelay(
            configureOptions: options => options.WrapExceptions = false,
            configureRegistration: registration => registration.EnableAssemblyScanning = false);
        // DomainRelay.Diagnostics 3.1.3 exports exception.Message; use sanitized tracing here.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(SanitizedDiagnosticsBehavior<,>));
        services.AddDomainRelayValidation();
        services.AddTransient<IValidator<ExecuteRemoteAction>, ExecuteRemoteActionValidator>();
        services.AddTransient<IValidator<GetRemoteUiModel>, GetRemoteUiModelValidator>();
        services.AddTransient<IValidator<DiscoverDevices>, DiscoverDevicesValidator>();
        services.AddTransient<IRequestHandler<GetRemoteUiModel, RemoteUiModel?>, GetRemoteUiModelHandler>();
        services.AddTransient<IRequestHandler<DiscoverDevices, IReadOnlyList<DiscoveredDeviceSummary>>, DiscoverDevicesHandler>();
        services.AddTransient<IRequestHandler<ExecuteRemoteAction, RemoteResult>, ExecuteRemoteActionHandler>();
        services.AddTransient<IRequestHandler<ListDevices, IReadOnlyList<DeviceSummary>>, ListDevicesHandler>();
        services.AddTransient<IValidator<GetManualPairingCandidates>, GetManualPairingCandidatesValidator>();
        services.AddTransient<IValidator<StartPairing>, StartPairingValidator>();
        services.AddTransient<IValidator<CompletePairing>, CompletePairingValidator>();
        services.AddTransient<IRequestHandler<GetPairingCandidates, IReadOnlyList<PairingCandidate>>, GetPairingCandidatesHandler>();
        services.AddTransient<IRequestHandler<GetManualPairingCandidates, IReadOnlyList<PairingCandidate>>, GetManualPairingCandidatesHandler>();
        services.AddTransient<IRequestHandler<StartPairing, PairingChallenge>, StartPairingHandler>();
        services.AddTransient<IRequestHandler<CompletePairing, PairedDeviceSummary>, CompletePairingHandler>();
        services.AddTransient<IValidator<CreateRoom>, CreateRoomValidator>();
        services.AddTransient<IValidator<RenameRoom>, RenameRoomValidator>();
        services.AddTransient<IValidator<DeleteRoom>, DeleteRoomValidator>();
        services.AddTransient<IValidator<AssignDeviceToRoom>, AssignDeviceToRoomValidator>();
        services.AddTransient<IValidator<UnassignDeviceFromRoom>, UnassignDeviceFromRoomValidator>();
        services.AddTransient<IRequestHandler<ListRooms, IReadOnlyList<RoomSummary>>, ListRoomsHandler>();
        services.AddTransient<IRequestHandler<CreateRoom, RoomSummary>, CreateRoomHandler>();
        services.AddTransient<IRequestHandler<RenameRoom, RoomSummary>, RenameRoomHandler>();
        services.AddTransient<IRequestHandler<DeleteRoom, bool>, DeleteRoomHandler>();
        services.AddTransient<IRequestHandler<AssignDeviceToRoom, RoomSummary>, AssignDeviceToRoomHandler>();
        services.AddTransient<IRequestHandler<UnassignDeviceFromRoom, RoomSummary>, UnassignDeviceFromRoomHandler>();
        services.AddTransient<IValidator<ListDeviceFavorites>, ListDeviceFavoritesValidator>();
        services.AddTransient<IValidator<AddDeviceFavorite>, AddDeviceFavoriteValidator>();
        services.AddTransient<IValidator<RemoveDeviceFavorite>, RemoveDeviceFavoriteValidator>();
        services.AddTransient<IRequestHandler<ListDeviceFavorites, IReadOnlyList<FavoriteSummary>>, ListDeviceFavoritesHandler>();
        services.AddTransient<IRequestHandler<AddDeviceFavorite, FavoriteSummary>, AddDeviceFavoriteHandler>();
        services.AddTransient<IRequestHandler<RemoveDeviceFavorite, bool>, RemoveDeviceFavoriteHandler>();
        services.AddTransient<IValidator<GetActivity>, GetActivityValidator>();
        services.AddTransient<IValidator<CreateActivity>, CreateActivityValidator>();
        services.AddTransient<IValidator<SaveActivity>, SaveActivityValidator>();
        services.AddTransient<IValidator<DeleteActivity>, DeleteActivityValidator>();
        services.AddTransient<IValidator<RunActivity>, RunActivityValidator>();
        services.AddTransient<IRequestHandler<ListActivities, IReadOnlyList<ActivitySummary>>, ListActivitiesHandler>();
        services.AddTransient<IRequestHandler<GetActivity, ActivitySummary?>, GetActivityHandler>();
        services.AddTransient<IRequestHandler<CreateActivity, ActivitySummary>, CreateActivityHandler>();
        services.AddTransient<IRequestHandler<SaveActivity, ActivitySummary>, SaveActivityHandler>();
        services.AddTransient<IRequestHandler<DeleteActivity, bool>, DeleteActivityHandler>();
        services.AddTransient<IRequestHandler<RunActivity, ActivityRunReport>, RunActivityHandler>();
        services.AddDomainRelayMapping(builder => builder.AddProfile<DeviceMappingProfile>());
        return services;
    }
    private sealed class RegistrationMarker { }
}
