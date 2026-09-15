using System.ComponentModel;
using System.Runtime.CompilerServices;
using UniversalRemote.Abstractions;
using UniversalRemote.Presentation;
using UniversalRemote.Provider.GenericIr;

namespace UniversalRemote.Maui.Hub;

public sealed record HubCandidateItem(
    InfraredHubId Id,
    string DisplayName,
    InfraredHubTransportKind TransportKind,
    string? FirmwareVersion,
    string DiagnosticCode,
    bool IsSelected);

public sealed class HubViewModel(
    IEnumerable<IInfraredHubDiscovery> discoveries,
    IEnumerable<IInfraredHubConnector> connectors,
    IInfraredHubSelectionStore selectionStore,
    IIrProfileManager profileManager,
    IIrProfileCatalog profileCatalog,
    IIrDeviceProvisioner provisioner) : INotifyPropertyChanged
{
    private readonly IReadOnlyList<IInfraredHubDiscovery> discoveryList = discoveries.ToArray();
    private readonly IReadOnlyList<IInfraredHubConnector> connectorList = connectors.ToArray();
    private IReadOnlyList<HubCandidateItem> candidates = [];
    private IReadOnlyList<IrProfile> profiles = profileCatalog.List();
    private string status = RemoteLabels.Text("Recherchez un Hub IR puis sélectionnez-le.", "Discover an IR hub, then select it.");
    private bool isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<HubCandidateItem> Candidates { get => candidates; private set => Set(ref candidates, value); }
    public IReadOnlyList<IrProfile> Profiles { get => profiles; private set => Set(ref profiles, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            Status = RemoteLabels.Text("Recherche des Hubs IR Wi-Fi et Bluetooth…", "Discovering Wi-Fi and Bluetooth IR hubs…");
            var selected = await selectionStore.GetAsync(cancellationToken);
            var found = new List<InfraredHubAdvertisement>();
            foreach (var discovery in discoveryList)
            {
                try { found.AddRange(await discovery.DiscoverAsync(TimeSpan.FromSeconds(4), cancellationToken)); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { }
            }
            Candidates = found
                .DistinctBy(x => (x.TransportKind, x.Id.Value))
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(x => new HubCandidateItem(
                    x.Id, x.DisplayName, x.TransportKind, x.FirmwareVersion, x.DiagnosticCode,
                    selected is not null && selected.Value.TransportKind == x.TransportKind
                        && string.Equals(selected.Value.HubId.Value, x.Id.Value, StringComparison.Ordinal)))
                .ToArray();
            Profiles = profileCatalog.List();
            Status = RemoteLabels.Text($"{Candidates.Count} Hub(s) détecté(s).", $"{Candidates.Count} hub(s) detected.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = RemoteLabels.Text("Recherche annulée.", "Discovery cancelled.");
        }
        finally { IsBusy = false; }
    }

    public async Task SelectAsync(HubCandidateItem candidate, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var connector = connectorList.FirstOrDefault(x => x.TransportKind == candidate.TransportKind);
            if (connector is null)
            {
                Status = RemoteLabels.Text("Transport indisponible sur cet appareil.", "Transport unavailable on this device.");
                return;
            }
            var result = await connector.ConnectAsync(candidate.Id, cancellationToken);
            if (result.Outcome != InfraredHubConnectOutcome.Connected)
            {
                Status = RemoteLabels.Text($"Connexion refusée : {result.DiagnosticCode}", $"Connection refused: {result.DiagnosticCode}");
                return;
            }
            await selectionStore.SaveAsync(new InfraredHubSelection(candidate.Id, candidate.TransportKind), cancellationToken);
            Status = RemoteLabels.Text("Hub connecté et sélectionné pour l’infrarouge.", "Hub connected and selected for infrared.");
            await RefreshAfterOperationAsync(cancellationToken);
        }
        finally { IsBusy = false; }
    }

    public async Task LearnAsync(
        string profileId,
        string displayName,
        string actionId,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var action = new RemoteAction(actionId);
            var result = await profileManager.LearnAndSaveAsync(profileId, displayName, action, overwrite, cancellationToken);
            if (result.Outcome == IrProfileLearnOutcome.Saved && result.Profile is not null)
            {
                await provisioner.RegisterProfileAsync(result.Profile.Id, result.Profile.DisplayName, cancellationToken);
                Profiles = profileCatalog.List();
                Status = RemoteLabels.Text(
                    $"Commande {action.Id} apprise et profil {result.Profile.Id} enregistré.",
                    $"Action {action.Id} learned and profile {result.Profile.Id} saved.");
                return;
            }
            Status = LearnOutcomeText(result.Outcome);
        }
        catch (ArgumentException)
        {
            Status = RemoteLabels.Text("Identifiant de profil ou d’action invalide.", "Invalid profile or action identifier.");
        }
        finally { IsBusy = false; }
    }

    public async Task ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var profile = profileManager.ImportJson(json);
            await provisioner.RegisterProfileAsync(profile.Id, profile.DisplayName, cancellationToken);
            Profiles = profileCatalog.List();
            Status = RemoteLabels.Text(
                $"Profil {profile.Id} importé comme non vérifié et appareil IR enregistré.",
                $"Profile {profile.Id} imported as unverified and IR device registered.");
        }
        catch (FormatException)
        {
            Status = RemoteLabels.Text("Le JSON IR est invalide ou dépasse les limites autorisées.", "IR JSON is invalid or exceeds the allowed limits.");
        }
        catch (ArgumentException)
        {
            Status = RemoteLabels.Text("Le profil IR contient une valeur invalide.", "The IR profile contains an invalid value.");
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshAfterOperationAsync(CancellationToken cancellationToken)
    {
        var selected = await selectionStore.GetAsync(cancellationToken);
        Candidates = Candidates.Select(x => x with
        {
            IsSelected = selected is not null && selected.Value.TransportKind == x.TransportKind
                && string.Equals(selected.Value.HubId.Value, x.Id.Value, StringComparison.Ordinal)
        }).ToArray();
    }

    private static string LearnOutcomeText(IrProfileLearnOutcome outcome) => outcome switch
    {
        IrProfileLearnOutcome.NoSelectedHub => RemoteLabels.Text("Sélectionnez d’abord un Hub IR.", "Select an IR hub first."),
        IrProfileLearnOutcome.HubUnavailable => RemoteLabels.Text("Le Hub IR n’est pas prêt.", "The IR hub is not ready."),
        IrProfileLearnOutcome.Unsupported => RemoteLabels.Text("Ce Hub ne propose pas l’apprentissage IR.", "This hub does not support IR learning."),
        IrProfileLearnOutcome.Timeout => RemoteLabels.Text("Aucun signal IR n’a été capturé avant le délai.", "No IR signal was captured before timeout."),
        IrProfileLearnOutcome.InvalidCapture => RemoteLabels.Text("Le signal capturé est invalide et n’a pas été enregistré.", "The captured signal is invalid and was not saved."),
        IrProfileLearnOutcome.ActionAlreadyExists => RemoteLabels.Text("Cette action existe déjà. Activez le remplacement pour la réapprendre.", "This action already exists. Enable overwrite to learn it again."),
        IrProfileLearnOutcome.CarrierFrequencyMismatch => RemoteLabels.Text("La fréquence capturée diffère de celle du profil existant.", "The captured carrier frequency differs from the existing profile."),
        _ => RemoteLabels.Text("L’apprentissage a échoué sans enregistrer de signal partiel.", "Learning failed without saving a partial signal.")
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
