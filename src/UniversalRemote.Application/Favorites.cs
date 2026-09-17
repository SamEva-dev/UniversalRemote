using DomainRelay.Abstractions;
using FluentValidation;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Application;

public sealed record FavoriteSummary(Guid DeviceId, string ActionId, int Position);
public sealed record ListDeviceFavorites(Guid DeviceId) : IRequest<IReadOnlyList<FavoriteSummary>>;
public sealed record AddDeviceFavorite(Guid DeviceId, string ActionId) : IRequest<FavoriteSummary>;
public sealed record RemoveDeviceFavorite(Guid DeviceId, string ActionId) : IRequest<bool>;

public sealed class ListDeviceFavoritesValidator : AbstractValidator<ListDeviceFavorites>
{
    public ListDeviceFavoritesValidator() => RuleFor(x => x.DeviceId).NotEmpty();
}

public sealed class AddDeviceFavoriteValidator : AbstractValidator<AddDeviceFavorite>
{
    public AddDeviceFavoriteValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.ActionId).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9._-]+$");
    }
}

public sealed class RemoveDeviceFavoriteValidator : AbstractValidator<RemoveDeviceFavorite>
{
    public RemoveDeviceFavoriteValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.ActionId).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9._-]+$");
    }
}

public sealed class ListDeviceFavoritesHandler(IFavoriteRepository favorites, IDeviceRepository devices)
    : IRequestHandler<ListDeviceFavorites, IReadOnlyList<FavoriteSummary>>
{
    public async Task<IReadOnlyList<FavoriteSummary>> Handle(ListDeviceFavorites request, CancellationToken ct)
    {
        if (await devices.FindAsync(request.DeviceId, ct).ConfigureAwait(false) is null)
            throw new KeyNotFoundException("Device not found.");

        var result = await favorites.ListAsync(request.DeviceId, ct).ConfigureAwait(false);
        return result.Select(ToSummary).ToArray();
    }

    internal static FavoriteSummary ToSummary(Favorite favorite)
        => new(favorite.DeviceId, favorite.Action.Id, favorite.Position);
}

public sealed class AddDeviceFavoriteHandler(IFavoriteRepository favorites, IDeviceRepository devices)
    : IRequestHandler<AddDeviceFavorite, FavoriteSummary>
{
    public async Task<FavoriteSummary> Handle(AddDeviceFavorite request, CancellationToken ct)
    {
        var device = await devices.FindAsync(request.DeviceId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Device not found.");
        var action = device.Capabilities.FirstOrDefault(x => string.Equals(x.Id, request.ActionId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The action is not currently supported by this device.");

        var favorite = await favorites.AddAsync(device.Id, action, ct).ConfigureAwait(false);
        return ListDeviceFavoritesHandler.ToSummary(favorite);
    }
}

public sealed class RemoveDeviceFavoriteHandler(IFavoriteRepository favorites, IDeviceRepository devices)
    : IRequestHandler<RemoveDeviceFavorite, bool>
{
    public async Task<bool> Handle(RemoveDeviceFavorite request, CancellationToken ct)
    {
        if (await devices.FindAsync(request.DeviceId, ct).ConfigureAwait(false) is null)
            throw new KeyNotFoundException("Device not found.");
        return await favorites.RemoveAsync(request.DeviceId, new RemoteAction(request.ActionId), ct).ConfigureAwait(false);
    }
}
