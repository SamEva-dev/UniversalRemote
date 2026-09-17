using DomainRelay.Abstractions;
using FluentValidation;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Application;

public sealed record RoomSummary(Guid Id, string Name, IReadOnlyList<DeviceSummary> Devices);

public sealed record ListRooms : IRequest<IReadOnlyList<RoomSummary>>;
public sealed record CreateRoom(string Name) : IRequest<RoomSummary>;
public sealed record RenameRoom(Guid RoomId, string Name) : IRequest<RoomSummary>;
public sealed record DeleteRoom(Guid RoomId) : IRequest<bool>;
public sealed record AssignDeviceToRoom(Guid RoomId, Guid DeviceId) : IRequest<RoomSummary>;
public sealed record UnassignDeviceFromRoom(Guid RoomId, Guid DeviceId) : IRequest<RoomSummary>;

public sealed class CreateRoomValidator : AbstractValidator<CreateRoom>
{
    public CreateRoomValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
}

public sealed class RenameRoomValidator : AbstractValidator<RenameRoom>
{
    public RenameRoomValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
    }
}

public sealed class DeleteRoomValidator : AbstractValidator<DeleteRoom>
{
    public DeleteRoomValidator() => RuleFor(x => x.RoomId).NotEmpty();
}

public sealed class AssignDeviceToRoomValidator : AbstractValidator<AssignDeviceToRoom>
{
    public AssignDeviceToRoomValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty();
        RuleFor(x => x.DeviceId).NotEmpty();
    }
}

public sealed class UnassignDeviceFromRoomValidator : AbstractValidator<UnassignDeviceFromRoom>
{
    public UnassignDeviceFromRoomValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty();
        RuleFor(x => x.DeviceId).NotEmpty();
    }
}

public sealed class ListRoomsHandler(IRoomRepository rooms, IDeviceRepository devices)
    : IRequestHandler<ListRooms, IReadOnlyList<RoomSummary>>
{
    public async Task<IReadOnlyList<RoomSummary>> Handle(ListRooms request, CancellationToken ct)
    {
        var roomItems = await rooms.ListAsync(ct).ConfigureAwait(false);
        var deviceItems = await devices.ListAsync(ct).ConfigureAwait(false);
        return roomItems.Select(room => RoomProjection.ToSummary(room, deviceItems)).ToArray();
    }
}

public sealed class CreateRoomHandler(IRoomRepository rooms, IDeviceRepository devices)
    : IRequestHandler<CreateRoom, RoomSummary>
{
    public async Task<RoomSummary> Handle(CreateRoom request, CancellationToken ct)
        => RoomProjection.ToSummary(
            await rooms.CreateAsync(request.Name, ct).ConfigureAwait(false),
            await devices.ListAsync(ct).ConfigureAwait(false));
}

public sealed class RenameRoomHandler(IRoomRepository rooms, IDeviceRepository devices)
    : IRequestHandler<RenameRoom, RoomSummary>
{
    public async Task<RoomSummary> Handle(RenameRoom request, CancellationToken ct)
        => RoomProjection.ToSummary(
            await rooms.RenameAsync(request.RoomId, request.Name, ct).ConfigureAwait(false),
            await devices.ListAsync(ct).ConfigureAwait(false));
}

public sealed class DeleteRoomHandler(IRoomRepository rooms) : IRequestHandler<DeleteRoom, bool>
{
    public Task<bool> Handle(DeleteRoom request, CancellationToken ct) => rooms.DeleteAsync(request.RoomId, ct);
}

public sealed class AssignDeviceToRoomHandler(IRoomRepository rooms, IDeviceRepository devices)
    : IRequestHandler<AssignDeviceToRoom, RoomSummary>
{
    public async Task<RoomSummary> Handle(AssignDeviceToRoom request, CancellationToken ct)
    {
        if (await devices.FindAsync(request.DeviceId, ct).ConfigureAwait(false) is null)
            throw new KeyNotFoundException("Device not found.");
        var room = await rooms.AssignDeviceAsync(request.RoomId, request.DeviceId, ct).ConfigureAwait(false);
        return RoomProjection.ToSummary(room, await devices.ListAsync(ct).ConfigureAwait(false));
    }
}

public sealed class UnassignDeviceFromRoomHandler(IRoomRepository rooms, IDeviceRepository devices)
    : IRequestHandler<UnassignDeviceFromRoom, RoomSummary>
{
    public async Task<RoomSummary> Handle(UnassignDeviceFromRoom request, CancellationToken ct)
    {
        var room = await rooms.UnassignDeviceAsync(request.RoomId, request.DeviceId, ct).ConfigureAwait(false);
        return RoomProjection.ToSummary(room, await devices.ListAsync(ct).ConfigureAwait(false));
    }
}

internal static class RoomProjection
{
    public static RoomSummary ToSummary(Room room, IReadOnlyList<Device> devices)
    {
        var byId = devices.ToDictionary(x => x.Id);
        var assigned = room.DeviceIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new DeviceSummary { Id = x.Id, DisplayName = x.DisplayName })
            .ToArray();
        return new RoomSummary(room.Id, room.Name, assigned);
    }
}
