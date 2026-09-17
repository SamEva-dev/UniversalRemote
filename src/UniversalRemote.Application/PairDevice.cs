using DomainRelay.Abstractions;
using FluentValidation;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Application;

public sealed record GetPairingCandidates(
    string DisplayName,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<string> Services,
    IReadOnlyDictionary<string, string>? Metadata = null) : IRequest<IReadOnlyList<PairingCandidate>>;

public sealed class GetPairingCandidatesHandler(IEnumerable<IDevicePairingProvider> providers)
    : IRequestHandler<GetPairingCandidates, IReadOnlyList<PairingCandidate>>
{
    private readonly IDevicePairingProvider[] providers = providers.ToArray();

    public Task<IReadOnlyList<PairingCandidate>> Handle(GetPairingCandidates request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var probe = new PairingProbe(request.DisplayName, request.Addresses, request.Services, request.Metadata);
        IReadOnlyList<PairingCandidate> matches = providers.Select(x => x.Match(probe)).OfType<PairingCandidate>().ToArray();
        return Task.FromResult(matches);
    }
}

public sealed record GetManualPairingCandidates(string DeviceKey, string? DisplayName = null) : IRequest<IReadOnlyList<PairingCandidate>>;

public sealed class GetManualPairingCandidatesValidator : AbstractValidator<GetManualPairingCandidates>
{
    public GetManualPairingCandidatesValidator()
    {
        RuleFor(x => x.DeviceKey).NotEmpty().MaximumLength(255);
        RuleFor(x => x.DisplayName).MaximumLength(200);
    }
}

public sealed class GetManualPairingCandidatesHandler(IEnumerable<IManualPairingProvider> providers)
    : IRequestHandler<GetManualPairingCandidates, IReadOnlyList<PairingCandidate>>
{
    private readonly IManualPairingProvider[] providers = providers.ToArray();

    public Task<IReadOnlyList<PairingCandidate>> Handle(GetManualPairingCandidates request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<PairingCandidate> matches = providers
            .Select(x => x.CreateManualCandidate(request.DeviceKey, request.DisplayName))
            .OfType<PairingCandidate>()
            .ToArray();
        return Task.FromResult(matches);
    }
}

public sealed record StartPairing(string ProviderId, string DeviceKey, string DisplayName) : IRequest<PairingChallenge>;

public sealed class StartPairingValidator : AbstractValidator<StartPairing>
{
    public StartPairingValidator()
    {
        RuleFor(x => x.ProviderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceKey).NotEmpty().MaximumLength(255);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
    }
}

public sealed class StartPairingHandler(IEnumerable<IDevicePairingProvider> providers)
    : IRequestHandler<StartPairing, PairingChallenge>
{
    private readonly IDevicePairingProvider[] providers = providers.ToArray();

    public Task<PairingChallenge> Handle(StartPairing request, CancellationToken ct)
    {
        var provider = providers.FirstOrDefault(x => string.Equals(x.Id, request.ProviderId, StringComparison.Ordinal));
        if (provider is null) throw new InvalidOperationException("Requested pairing provider is unavailable.");
        return provider.StartAsync(new PairingCandidate(request.ProviderId, request.DeviceKey, request.DisplayName), ct);
    }
}

public sealed record CompletePairing(
    string ProviderId,
    Guid ChallengeId,
    string Code,
    string DisplayName) : IRequest<PairedDeviceSummary>;

public sealed record PairedDeviceSummary(Guid DeviceId, string DisplayName, string ProviderId);

public sealed class CompletePairingValidator : AbstractValidator<CompletePairing>
{
    public CompletePairingValidator()
    {
        RuleFor(x => x.ProviderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChallengeId).NotEmpty();
        RuleFor(x => x.Code).MaximumLength(32);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
    }
}

public sealed class CompletePairingHandler(
    IEnumerable<IDevicePairingProvider> providers,
    IDeviceRegistrar registrar)
    : IRequestHandler<CompletePairing, PairedDeviceSummary>
{
    private readonly IDevicePairingProvider[] providers = providers.ToArray();

    public async Task<PairedDeviceSummary> Handle(CompletePairing request, CancellationToken ct)
    {
        var provider = providers.FirstOrDefault(x => string.Equals(x.Id, request.ProviderId, StringComparison.Ordinal));
        if (provider is null) throw new InvalidOperationException("Requested pairing provider is unavailable.");
        var completion = await provider.CompleteAsync(request.ChallengeId, request.Code, ct).ConfigureAwait(false);
        var displayName = string.IsNullOrWhiteSpace(completion.DisplayName) ? request.DisplayName : completion.DisplayName;
        var device = await registrar.RegisterPairingAsync(displayName, completion.Route, ct).ConfigureAwait(false);
        return new PairedDeviceSummary(device.Id, device.DisplayName, completion.Route.ProviderId);
    }
}
