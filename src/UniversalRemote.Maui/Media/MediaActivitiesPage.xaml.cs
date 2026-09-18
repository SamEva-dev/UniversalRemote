using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Activities;

namespace UniversalRemote.Maui.Media;

public sealed partial class MediaActivitiesPage : ContentPage
{
    private readonly MediaNavigationState navigation;
    private readonly IMediaActivityRepository repository;
    private readonly IMediaActivityRunner runner;
    private readonly IPlaybackTargetDiscovery targetDiscovery;
    private readonly IPlaybackTargetSelection targetSelection;
    private readonly IActivityRepository activities;
    private MediaCatalogEntry? currentMedia;
    private ScenarioRow? selectedScenario;
    private bool busy;

    public MediaActivitiesPage(
        MediaNavigationState navigation,
        IMediaActivityRepository repository,
        IMediaActivityRunner runner,
        IPlaybackTargetDiscovery targetDiscovery,
        IPlaybackTargetSelection targetSelection,
        IActivityRepository activities)
    {
        InitializeComponent();
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.targetDiscovery = targetDiscovery ?? throw new ArgumentNullException(nameof(targetDiscovery));
        this.targetSelection = targetSelection ?? throw new ArgumentNullException(nameof(targetSelection));
        this.activities = activities ?? throw new ArgumentNullException(nameof(activities));

        saveButton.Clicked += async (_, _) => await SaveAsync();
        runButton.Clicked += async (_, _) => await RunSelectedAsync();
        deleteButton.Clicked += async (_, _) => await DeleteSelectedAsync();
        scenarioList.SelectionChanged += (_, e) =>
        {
            selectedScenario = e.CurrentSelection.FirstOrDefault() as ScenarioRow;
            runButton.IsEnabled = selectedScenario is not null && !busy;
            deleteButton.IsEnabled = selectedScenario is not null && !busy;
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        currentMedia = navigation.Current;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            currentMedia = navigation.Current ?? currentMedia;
            mediaLabel.Text = currentMedia is null
                ? "Aucun média sélectionné — ouvrez d’abord un contenu depuis le catalogue."
                : $"{currentMedia.Item.Title} • {MediaText.Kind(currentMedia.Item.Kind)}";
            saveButton.IsEnabled = currentMedia is not null;
            if (currentMedia is not null && string.IsNullOrWhiteSpace(nameEntry.Text))
                nameEntry.Text = $"Regarder {currentMedia.Item.Title}";

            var discovered = await targetDiscovery.DiscoverAsync();
            var targets = discovered.Select(x => new TargetOption(
                x.Target,
                x.CanLaunch,
                x.CanLaunch ? x.Target.DisplayName : $"{x.Target.DisplayName} — Preview"))
                .ToArray();
            targetPicker.ItemsSource = targets;
            var selectedIndex = Array.FindIndex(targets, x => x.Target.Id == targetSelection.Selected.Id);
            targetPicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (targets.Length > 0 ? 0 : -1);

            var controlActivities = await activities.ListAsync();
            var preparations = new[] { new PreparationOption(null, "Aucune préparation") }
                .Concat(controlActivities.Select(x => new PreparationOption(x.Id, x.Name)))
                .ToArray();
            preparationPicker.ItemsSource = preparations;
            if (preparationPicker.SelectedIndex < 0) preparationPicker.SelectedIndex = 0;

            var saved = await repository.ListAsync();
            scenarioList.ItemsSource = saved.Select(x => new ScenarioRow(
                x,
                BuildScenarioSubtitle(x, preparations, targets))).ToArray();
            if (saved.Count == 0) runStatusLabel.Text = "Aucun scénario Media enregistré.";
        }
        catch (Exception)
        {
            editorStatusLabel.Text = "Impossible de charger les scénarios Media.";
        }
        finally { SetBusy(false); }
    }

    private async Task SaveAsync()
    {
        if (busy || currentMedia is null) return;
        if (targetPicker.SelectedItem is not TargetOption target)
        {
            editorStatusLabel.Text = "Choisissez une cible de lecture.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(nameEntry.Text)
            ? $"Regarder {currentMedia.Item.Title}"
            : nameEntry.Text.Trim();
        var preparation = preparationPicker.SelectedItem as PreparationOption;
        var policy = continueSwitch.IsToggled
            ? ActivityFailurePolicy.ContinueAfterNonAccepted
            : ActivityFailurePolicy.StopOnFirstNonAccepted;

        var definition = new MediaActivityDefinition(
            Guid.NewGuid(),
            name,
            preparation?.Id,
            MediaReference.From(currentMedia.Item),
            currentMedia.Item.Title,
            MediaActivityTargetMode.SpecificTarget,
            target.Target.Id,
            policy);

        SetBusy(true);
        try
        {
            await repository.SaveAsync(definition);
            targetSelection.Select(target.Target);
            editorStatusLabel.Text = target.CanLaunch
                ? "Scénario enregistré."
                : "Scénario enregistré en Preview : cette cible ne peut pas encore recevoir directement le flux.";
        }
        catch (Exception)
        {
            editorStatusLabel.Text = "Le scénario n’a pas pu être enregistré.";
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private async Task RunSelectedAsync()
    {
        if (busy || selectedScenario is null) return;
        SetBusy(true);
        try
        {
            runStatusLabel.Text = "Préparation des appareils…";
            var report = await runner.RunAsync(selectedScenario.Definition.Id);
            runStatusLabel.Text = report.Status switch
            {
                MediaActivityRunStatus.Completed => "Scénario exécuté : lecture démarrée.",
                MediaActivityRunStatus.CompletedWithIssues => "Lecture démarrée avec une ou plusieurs commandes de préparation non confirmées.",
                MediaActivityRunStatus.StoppedBeforePlayback => "Scénario arrêté pendant la préparation. La lecture n’a pas été lancée.",
                MediaActivityRunStatus.PlaybackUnavailable => "La préparation est terminée, mais la cible ou le contenu n’est pas disponible pour la lecture.",
                MediaActivityRunStatus.PlaybackFailed => "La préparation est terminée, mais le lecteur n’a pas pu démarrer le média.",
                MediaActivityRunStatus.Cancelled => "Scénario annulé.",
                _ => report.UserMessage ?? report.Status.ToString()
            };

            if (report.PlaybackStatus == MediaActivityPlaybackStatus.Started)
                await Shell.Current.GoToAsync("//media-player");
        }
        catch (Exception)
        {
            runStatusLabel.Text = "Le scénario n’a pas pu être exécuté.";
        }
        finally { SetBusy(false); }
    }

    private async Task DeleteSelectedAsync()
    {
        if (busy || selectedScenario is null) return;
        var selected = selectedScenario;
        SetBusy(true);
        try
        {
            await repository.DeleteAsync(selected.Definition.Id);
            selectedScenario = null;
            scenarioList.SelectedItem = null;
            runStatusLabel.Text = "Scénario supprimé.";
        }
        catch (Exception)
        {
            runStatusLabel.Text = "Le scénario n’a pas pu être supprimé.";
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        busyIndicator.IsVisible = value;
        busyIndicator.IsRunning = value;
        saveButton.IsEnabled = !value && currentMedia is not null;
        runButton.IsEnabled = !value && selectedScenario is not null;
        deleteButton.IsEnabled = !value && selectedScenario is not null;
    }

    private static string BuildScenarioSubtitle(
        MediaActivityDefinition activity,
        IReadOnlyList<PreparationOption> preparations,
        IReadOnlyList<TargetOption> targets)
    {
        var preparation = activity.PreparationActivityId is { } id
            ? preparations.FirstOrDefault(x => x.Id == id)?.Label ?? "Préparation supprimée"
            : "Sans préparation";
        var target = activity.TargetMode == MediaActivityTargetMode.SelectedTarget
            ? "Cible actuelle"
            : targets.FirstOrDefault(x => x.Target.Id == activity.TargetId)?.Label ?? "Cible indisponible";
        return $"{activity.MediaTitle} • {preparation} • {target}";
    }

    private sealed record PreparationOption(Guid? Id, string Label);
    private sealed record TargetOption(IPlaybackTarget Target, bool CanLaunch, string Label);

    private sealed class ScenarioRow
    {
        public MediaActivityDefinition Definition { get; }
        public string Name => Definition.Name;
        public string Subtitle { get; }

        public ScenarioRow(MediaActivityDefinition definition, string subtitle)
        {
            Definition = definition;
            Subtitle = subtitle;
        }
    }
}
