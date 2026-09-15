using DomainRelay.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UniversalRemote.Application;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Maui.Discovery;

public sealed record DiscoveredDeviceItem(DiscoveredDeviceSummary Device, IReadOnlyList<PairingCandidate> PairingCandidates);

public sealed class DiscoveryViewModel(IMediator mediator, DeviceSelectionState selection) : INotifyPropertyChanged
{
    private IReadOnlyList<DiscoveredDeviceItem> devices = Array.Empty<DiscoveredDeviceItem>();
    private string status = "Prêt à rechercher les appareils du réseau local.";
    private bool isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<DiscoveredDeviceItem> Devices { get => devices; private set => Set(ref devices, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            Status = "Recherche mDNS et SSDP/UPnP…";
            var discovered = await mediator.Send(new DiscoverDevices(), cancellationToken);
            var items = new List<DiscoveredDeviceItem>(discovered.Count);
            foreach (var device in discovered)
            {
                var candidates = await mediator.Send(new GetPairingCandidates(device.DisplayName, device.Addresses, device.Services, device.Metadata), cancellationToken);
                items.Add(new DiscoveredDeviceItem(device, candidates));
            }
            Devices = items;
            Status = Devices.Count == 0
                ? "Aucun service compatible détecté. Vérifie le Wi-Fi et les autorisations réseau."
                : $"{Devices.Count} appareil(s) ou service(s) consolidé(s) détecté(s).";
        }
        catch (OperationCanceledException) { Status = "Recherche annulée."; }
        catch (Exception) { Status = "Recherche réseau indisponible. Vérifie le Wi-Fi et réessaie."; Devices = Array.Empty<DiscoveredDeviceItem>(); }
        finally { IsBusy = false; }
    }

    public async Task<PairingChallenge> StartPairingAsync(PairingCandidate candidate, CancellationToken ct = default)
    {
        Status = $"Association avec {candidate.DisplayName}…";
        return await mediator.Send(new StartPairing(candidate.ProviderId, candidate.DeviceKey, candidate.DisplayName), ct);
    }

    public async Task CompletePairingAsync(PairingCandidate candidate, PairingChallenge challenge, string code, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CompletePairing(candidate.ProviderId, challenge.Id, code, candidate.DisplayName), ct);
        selection.ActiveDeviceId = result.DeviceId;
        Status = $"{result.DisplayName} associé. Ouvre l’onglet Télécommande pour le piloter.";
    }

    public void ReportPairingCancelled() => Status = "Association annulée.";
    public void ReportPairingError() => Status = "Association impossible. Vérifie le code affiché et réessaie.";

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
