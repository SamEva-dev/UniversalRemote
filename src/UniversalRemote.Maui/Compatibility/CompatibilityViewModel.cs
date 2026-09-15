using Microsoft.Maui.Storage;
using DomainRelay.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using UniversalRemote.Application;
using UniversalRemote.Compatibility;
using UniversalRemote.Presentation;

namespace UniversalRemote.Maui.Compatibility;

public sealed class CompatibilityViewModel(IMediator mediator, IOperatorCompatibilityDiagnostics diagnostics) : INotifyPropertyChanged
{
    private OperatorCompatibilitySnapshot? snapshot;
    private string status = RemoteLabels.Text("Actualisez pour comparer les profils opérateurs avec les appareils détectés.", "Refresh to compare operator profiles with discovered devices.");
    private bool isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;
    public OperatorCompatibilitySnapshot? Snapshot { get => snapshot; private set => Set(ref snapshot, value); }
    public IReadOnlyList<OperatorCompatibilityDiagnostic> Profiles => Snapshot?.Profiles ?? Array.Empty<OperatorCompatibilityDiagnostic>();
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            Status = RemoteLabels.Text("Recherche des équipements et qualification de compatibilité…", "Discovering devices and qualifying compatibility…");
            var discovered = await mediator.Send(new DiscoverDevices(), cancellationToken);
            var probes = new List<OperatorDeviceProbe>();

            foreach (var device in discovered)
            {
                var candidates = await mediator.Send(
                    new GetPairingCandidates(device.DisplayName, device.Addresses, device.Services, device.Metadata),
                    cancellationToken);
                foreach (var candidate in candidates.DistinctBy(static x => x.ProviderId, StringComparer.OrdinalIgnoreCase))
                {
                    probes.Add(new OperatorDeviceProbe(
                        candidate.ProviderId,
                        device.DisplayName,
                        device.Services,
                        device.Metadata));
                }
            }

            Snapshot = diagnostics.BuildSnapshot(probes);
            OnPropertyChanged(nameof(Profiles));
            Status = RemoteLabels.Text(
                $"{Snapshot.Profiles.Count} profil(s) suivi(s) • {Snapshot.DetectedCount} correspondance(s) détectée(s) • {Snapshot.ReadyForPhysicalRecipeCount} recette(s) prête(s).",
                $"{Snapshot.Profiles.Count} tracked profile(s) • {Snapshot.DetectedCount} detected match(es) • {Snapshot.ReadyForPhysicalRecipeCount} recipe(s) ready.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = RemoteLabels.Text("Actualisation annulée.", "Refresh cancelled.");
        }
        catch (Exception)
        {
            Snapshot = diagnostics.BuildSnapshot();
            OnPropertyChanged(nameof(Profiles));
            Status = RemoteLabels.Text(
                "Le scan réseau est indisponible. La matrice documentaire reste affichée sans détection runtime.",
                "Network scan is unavailable. The documented matrix remains visible without runtime detection.");
        }
        finally { IsBusy = false; }
    }

    public async Task<string> ExportAsync(CompatibilityMatrixFormat format, CancellationToken cancellationToken = default)
    {
        var current = Snapshot ?? diagnostics.BuildSnapshot();
        Snapshot = current;
        OnPropertyChanged(nameof(Profiles));
        var content = diagnostics.Export(current, format);
        var extension = diagnostics.FileExtension(format);
        var filename = $"universalremote-compatibility-{current.GeneratedAtUtc:yyyyMMdd-HHmmss}{extension}";
        var path = Path.Combine(FileSystem.CacheDirectory, filename);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: format == CompatibilityMatrixFormat.Csv), cancellationToken);
        Status = RemoteLabels.Text($"Matrice exportée : {filename}", $"Matrix exported: {filename}");
        return path;
    }

    public string RecipeLabel(CompatibilityRecipeState state) => state switch
    {
        CompatibilityRecipeState.NotRequired => RemoteLabels.Text("Aucune recette requise", "No recipe required"),
        CompatibilityRecipeState.ResearchOnly => RemoteLabels.Text("Recherche uniquement", "Research only"),
        CompatibilityRecipeState.PhysicalValidationRequired => RemoteLabels.Text("Recette physique requise", "Physical recipe required"),
        CompatibilityRecipeState.AwaitingDeviceDetection => RemoteLabels.Text("En attente de détection d’un appareil", "Waiting for device detection"),
        CompatibilityRecipeState.AwaitingExactModelOrFirmware => RemoteLabels.Text("Modèle exact / firmware manquant", "Exact model / firmware missing"),
        CompatibilityRecipeState.ReadyForPhysicalRecipe => RemoteLabels.Text("Prêt pour recette physique", "Ready for physical recipe"),
        _ => state.ToString()
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
