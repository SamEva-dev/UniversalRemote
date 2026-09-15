using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.GenericIr;

public enum IrProfileLearnOutcome
{
    Saved,
    NoSelectedHub,
    HubUnavailable,
    Unsupported,
    Timeout,
    InvalidCapture,
    ActionAlreadyExists,
    CarrierFrequencyMismatch,
    Failed
}

public sealed record IrProfileLearnResult(IrProfileLearnOutcome Outcome, IrProfile? Profile = null);

public interface IIrProfileManager
{
    IrProfile ImportJson(string json);
    Task<IrProfileLearnResult> LearnAndSaveAsync(
        string profileId,
        string displayName,
        RemoteAction action,
        bool overwriteExistingAction = false,
        CancellationToken cancellationToken = default);
}

/// <summary>Validates local imports and converts one hub capture into a reusable Generic IR profile command.</summary>
public sealed class IrProfileManager(
    IIrProfileCatalog catalog,
    IInfraredHubSelectionAccessor selectedHubAccessor) : IIrProfileManager
{
    public IrProfile ImportJson(string json)
    {
        var parsed = IrProfileParser.Parse(json);
        // Local import is not physical compatibility proof. Never trust a user-supplied verified=true claim.
        var imported = new IrProfile(
            parsed.Version,
            parsed.Id,
            parsed.DisplayName,
            verified: false,
            source: "imported-local",
            parsed.CarrierFrequencyHz,
            parsed.Commands);
        catalog.Upsert(imported);
        return imported;
    }

    public async Task<IrProfileLearnResult> LearnAndSaveAsync(
        string profileId,
        string displayName,
        RemoteAction action,
        bool overwriteExistingAction = false,
        CancellationToken cancellationToken = default)
    {
        IrProfile.ValidateIdentifier(profileId, nameof(profileId));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(action);

        IInfraredHub? hub;
        try { hub = await selectedHubAccessor.GetSelectedHubAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(IrProfileLearnOutcome.HubUnavailable); }
        if (hub is null) return new(IrProfileLearnOutcome.NoSelectedHub);

        InfraredHubInfo info;
        try { info = await hub.GetInfoAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(IrProfileLearnOutcome.HubUnavailable); }
        if (info.State != InfraredHubConnectionState.Ready) return new(IrProfileLearnOutcome.HubUnavailable);
        if (!info.Capabilities.CanLearn) return new(IrProfileLearnOutcome.Unsupported);

        InfraredHubLearnResult capture;
        try { capture = await hub.LearnAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(IrProfileLearnOutcome.Failed); }

        if (capture.Outcome != InfraredHubLearnOutcome.Captured || capture.Signal is null)
            return capture.Outcome switch
            {
                InfraredHubLearnOutcome.Timeout => new(IrProfileLearnOutcome.Timeout),
                InfraredHubLearnOutcome.Unsupported => new(IrProfileLearnOutcome.Unsupported),
                InfraredHubLearnOutcome.InvalidCapture => new(IrProfileLearnOutcome.InvalidCapture),
                _ => new(IrProfileLearnOutcome.Failed)
            };

        var signal = capture.Signal;
        IrProfile profile;
        if (catalog.TryGet(profileId, out var existing))
        {
            if (existing.CarrierFrequencyHz != signal.CarrierFrequencyHz)
                return new(IrProfileLearnOutcome.CarrierFrequencyMismatch, existing);
            if (existing.TryGetCommand(action, out _) && !overwriteExistingAction)
                return new(IrProfileLearnOutcome.ActionAlreadyExists, existing);

            var commands = existing.Commands
                .Where(x => !string.Equals(x.Action.Id, action.Id, StringComparison.Ordinal))
                .Append(new IrCommand(action, signal.PatternMicroseconds))
                .ToArray();
            profile = new IrProfile(
                existing.Version,
                existing.Id,
                existing.DisplayName,
                verified: false,
                source: "learned-local",
                existing.CarrierFrequencyHz,
                commands);
        }
        else
        {
            profile = new IrProfile(
                IrProfileParser.CurrentVersion,
                profileId,
                displayName.Trim(),
                verified: false,
                source: "learned-local",
                signal.CarrierFrequencyHz,
                [new IrCommand(action, signal.PatternMicroseconds)]);
        }

        catalog.Upsert(profile);
        return new(IrProfileLearnOutcome.Saved, profile);
    }
}

public interface IIrDeviceProvisioner
{
    Task<Device> RegisterProfileAsync(string profileId, string? displayName = null, CancellationToken cancellationToken = default);
}

/// <summary>Registers/updates a Generic IR device route while preserving stable Device.Id through IDeviceRegistrar.</summary>
public sealed class GenericIrDeviceProvisioner(IIrProfileCatalog catalog, IDeviceRegistrar registrar) : IIrDeviceProvisioner
{
    public Task<Device> RegisterProfileAsync(string profileId, string? displayName = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (!catalog.TryGet(profileId, out var profile))
            throw new KeyNotFoundException("IR profile was not found.");
        var route = new DeviceRoute(GenericIrRemoteProvider.ProviderId, profile.Id, profile.Capabilities);
        return registrar.RegisterPairingAsync(
            string.IsNullOrWhiteSpace(displayName) ? profile.DisplayName : displayName.Trim(),
            route,
            cancellationToken);
    }
}

internal sealed class NoSelectedInfraredHubAccessor : IInfraredHubSelectionAccessor
{
    public ValueTask<IInfraredHub?> GetSelectedHubAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IInfraredHub?>(null);
    }
}
