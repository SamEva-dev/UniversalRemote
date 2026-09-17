using DomainRelay.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Presentation;

namespace UniversalRemote.Maui.Activities;

public sealed record ActivityStepDraft(
    ActivityStepInputKind Kind,
    Guid? DeviceId = null,
    string? ActionId = null,
    int? DelayMilliseconds = null);

public sealed record ActivityActionOption(string Id, string Label);

public sealed class ActivitiesViewModel(IMediator mediator) : INotifyPropertyChanged
{
    private IReadOnlyList<ActivitySummary> activities = Array.Empty<ActivitySummary>();
    private IReadOnlyList<RoomSummary> rooms = Array.Empty<RoomSummary>();
    private IReadOnlyList<DeviceSummary> devices = Array.Empty<DeviceSummary>();
    private IReadOnlyList<ActivityStepDraft> draftSteps = Array.Empty<ActivityStepDraft>();
    private Guid? editingActivityId;
    private bool isEditing;
    private string editorName = string.Empty;
    private Guid? editorRoomId;
    private ActivityRunReport? lastRunReport;
    private string status = RemoteLabels.Text(
        "Créez une activité pour piloter plusieurs appareils en une seule action.",
        "Create an activity to control several devices with one action.");
    private bool isBusy;
    private bool isRunning;
    private Guid? runningActivityId;
    private CancellationTokenSource? runCancellation;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ActivitySummary> Activities { get => activities; private set => Set(ref activities, value); }
    public IReadOnlyList<RoomSummary> Rooms { get => rooms; private set => Set(ref rooms, value); }
    public IReadOnlyList<DeviceSummary> Devices { get => devices; private set => Set(ref devices, value); }
    public IReadOnlyList<ActivityStepDraft> DraftSteps { get => draftSteps; private set => Set(ref draftSteps, value); }
    public Guid? EditingActivityId { get => editingActivityId; private set => Set(ref editingActivityId, value); }
    public bool IsEditing { get => isEditing; private set => Set(ref isEditing, value); }
    public string EditorName { get => editorName; private set => Set(ref editorName, value); }
    public Guid? EditorRoomId { get => editorRoomId; private set => Set(ref editorRoomId, value); }
    public ActivityRunReport? LastRunReport { get => lastRunReport; private set => Set(ref lastRunReport, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }
    public bool IsRunning { get => isRunning; private set => Set(ref isRunning, value); }
    public Guid? RunningActivityId { get => runningActivityId; private set => Set(ref runningActivityId, value); }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || IsRunning) return;
        try
        {
            IsBusy = true;
            Activities = await mediator.Send(new ListActivities(), cancellationToken);
            Rooms = await mediator.Send(new ListRooms(), cancellationToken);
            Devices = await mediator.Send(new ListDevices(), cancellationToken);
            Status = Activities.Count == 0
                ? RemoteLabels.Text("Aucune activité. Créez par exemple « Regarder la TV ».", "No activities yet. Create one such as 'Watch TV'.")
                : RemoteLabels.Text($"{Activities.Count} activité(s) prête(s).", $"{Activities.Count} activity/activities ready.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            Status = RemoteLabels.Text("Impossible de charger les activités.", "Unable to load activities.");
        }
        finally { IsBusy = false; }
    }

    public void BeginNew()
    {
        EnsureNotRunning();
        EditingActivityId = null;
        EditorName = string.Empty;
        EditorRoomId = null;
        DraftSteps = Array.Empty<ActivityStepDraft>();
        IsEditing = true;
        Status = RemoteLabels.Text("Nouvelle activité : ajoutez des commandes et des délais.", "New activity: add commands and delays.");
    }

    public async Task BeginEditAsync(Guid activityId, CancellationToken cancellationToken = default)
    {
        EnsureNotRunning();
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var activity = await mediator.Send(new GetActivity(activityId), cancellationToken)
                ?? throw new KeyNotFoundException("Activity not found.");
            EditingActivityId = activity.Id;
            EditorName = activity.Name;
            EditorRoomId = activity.RoomId;
            DraftSteps = activity.Steps.Select(ToDraft).ToArray();
            IsEditing = true;
            Status = RemoteLabels.Text("Activité chargée dans l’éditeur.", "Activity loaded in the editor.");
        }
        finally { IsBusy = false; }
    }

    public void CancelEditing()
    {
        if (IsRunning) return;
        IsEditing = false;
        EditingActivityId = null;
        EditorName = string.Empty;
        EditorRoomId = null;
        DraftSteps = Array.Empty<ActivityStepDraft>();
        Status = RemoteLabels.Text("Modification annulée.", "Editing cancelled.");
    }

    public async Task<IReadOnlyList<ActivityActionOption>> GetActionsAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var model = await mediator.Send(new GetRemoteUiModel(deviceId), cancellationToken);
        if (model is null) return Array.Empty<ActivityActionOption>();
        return model.Controls
            .Select(control => new ActivityActionOption(control.Action.Id, RemoteLabels.Action(control)))
            .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public void AddCommandStep(Guid deviceId, string actionId)
    {
        EnsureEditable();
        if (deviceId == Guid.Empty) throw new ArgumentException("Device ID is required.", nameof(deviceId));
        _ = new RemoteAction(actionId);
        DraftSteps = DraftSteps.Append(new ActivityStepDraft(ActivityStepInputKind.RemoteAction, deviceId, actionId)).ToArray();
    }

    public void AddDelayStep(int milliseconds)
    {
        EnsureEditable();
        if (milliseconds < DelayActivityStep.MinimumDuration.TotalMilliseconds
            || milliseconds > DelayActivityStep.MaximumDuration.TotalMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "Delay must be between 50 and 60000 milliseconds.");
        DraftSteps = DraftSteps.Append(new ActivityStepDraft(ActivityStepInputKind.Delay, DelayMilliseconds: milliseconds)).ToArray();
    }

    public void MoveStep(int index, int delta)
    {
        EnsureEditable();
        var target = index + delta;
        if (index < 0 || index >= DraftSteps.Count || target < 0 || target >= DraftSteps.Count) return;
        var items = DraftSteps.ToList();
        (items[index], items[target]) = (items[target], items[index]);
        DraftSteps = items;
    }

    public void RemoveStep(int index)
    {
        EnsureEditable();
        if (index < 0 || index >= DraftSteps.Count) return;
        var items = DraftSteps.ToList();
        items.RemoveAt(index);
        DraftSteps = items;
    }

    public async Task<ActivitySummary> SaveEditingAsync(
        string name,
        Guid? roomId,
        CancellationToken cancellationToken = default)
    {
        EnsureEditable();
        if (IsBusy) throw new InvalidOperationException("An operation is already running.");
        ActivitySummary? created = null;
        try
        {
            IsBusy = true;
            var activityId = EditingActivityId;
            if (activityId is null)
            {
                created = await mediator.Send(new CreateActivity(name, roomId), cancellationToken);
                activityId = created.Id;
            }

            var inputs = DraftSteps.Select((step, position) => new ActivityStepInput(
                position,
                step.Kind,
                step.DeviceId,
                step.ActionId,
                step.DelayMilliseconds)).ToArray();

            var saved = await mediator.Send(new SaveActivity(activityId.Value, name, roomId, inputs), cancellationToken);
            EditingActivityId = saved.Id;
            EditorName = saved.Name;
            EditorRoomId = saved.RoomId;
            DraftSteps = saved.Steps.Select(ToDraft).ToArray();
            IsEditing = false;
            Activities = await mediator.Send(new ListActivities(), cancellationToken);
            Status = RemoteLabels.Text("Activité enregistrée.", "Activity saved.");
            return saved;
        }
        catch
        {
            if (created is not null)
            {
                try { await mediator.Send(new DeleteActivity(created.Id), CancellationToken.None); }
                catch { /* Best-effort rollback of a newly-created empty draft. */ }
            }
            throw;
        }
        finally { IsBusy = false; }
    }

    public async Task DeleteAsync(Guid activityId, CancellationToken cancellationToken = default)
    {
        EnsureNotRunning();
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            await mediator.Send(new DeleteActivity(activityId), cancellationToken);
            Activities = await mediator.Send(new ListActivities(), cancellationToken);
            if (EditingActivityId == activityId) CancelEditing();
            Status = RemoteLabels.Text("Activité supprimée.", "Activity deleted.");
        }
        finally { IsBusy = false; }
    }

    public async Task<ActivityRunReport?> RunAsync(
        Guid activityId,
        ActivityFailurePolicy failurePolicy,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning || IsBusy) return null;
        runCancellation?.Dispose();
        runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            IsRunning = true;
            RunningActivityId = activityId;
            LastRunReport = null;
            Status = RemoteLabels.Text("Exécution en cours…", "Running activity…");
            var report = await mediator.Send(new RunActivity(activityId, failurePolicy), runCancellation.Token);
            LastRunReport = report;
            Status = StatusText(report.Status);
            return report;
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            Status = RemoteLabels.Text("Exécution annulée.", "Activity cancelled.");
            return null;
        }
        catch
        {
            Status = RemoteLabels.Text("L’activité n’a pas pu être exécutée.", "The activity could not be run.");
            throw;
        }
        finally
        {
            IsRunning = false;
            RunningActivityId = null;
            runCancellation.Dispose();
            runCancellation = null;
        }
    }

    public void CancelRun()
    {
        if (!IsRunning) return;
        Status = RemoteLabels.Text("Annulation demandée…", "Cancelling…");
        runCancellation?.Cancel();
    }

    public string RoomName(Guid? roomId)
        => roomId is null
            ? RemoteLabels.Text("Sans pièce", "No room")
            : Rooms.FirstOrDefault(x => x.Id == roomId.Value)?.Name
                ?? RemoteLabels.Text("Pièce supprimée", "Deleted room");

    public string DeviceName(Guid? deviceId)
        => deviceId is null
            ? RemoteLabels.Text("Appareil inconnu", "Unknown device")
            : Devices.FirstOrDefault(x => x.Id == deviceId.Value)?.DisplayName
                ?? RemoteLabels.Text("Appareil indisponible", "Unavailable device");

    public static string ActionLabel(string? actionId) => actionId switch
    {
        "power.toggle" => RemoteLabels.Text("Marche / arrêt", "Power"),
        "volume.up" => "Volume +",
        "volume.down" => "Volume −",
        "audio.mute.toggle" => RemoteLabels.Text("Muet", "Mute"),
        "navigation.up" => RemoteLabels.Text("Haut", "Up"),
        "navigation.down" => RemoteLabels.Text("Bas", "Down"),
        "navigation.left" => RemoteLabels.Text("Gauche", "Left"),
        "navigation.right" => RemoteLabels.Text("Droite", "Right"),
        "navigation.ok" => "OK",
        "navigation.back" => RemoteLabels.Text("Retour", "Back"),
        "navigation.home" => RemoteLabels.Text("Accueil", "Home"),
        null => RemoteLabels.Text("Commande inconnue", "Unknown command"),
        _ => actionId
    };

    public static string StatusText(ActivityRunStatus runStatus) => runStatus switch
    {
        ActivityRunStatus.Completed => RemoteLabels.Text("Activité terminée.", "Activity completed."),
        ActivityRunStatus.CompletedWithIssues => RemoteLabels.Text("Activité terminée avec incident(s).", "Activity completed with issue(s)."),
        ActivityRunStatus.StoppedOnNonAccepted => RemoteLabels.Text("Activité arrêtée sur un résultat non accepté ou incertain.", "Activity stopped on a failed or uncertain result."),
        ActivityRunStatus.Cancelled => RemoteLabels.Text("Activité annulée.", "Activity cancelled."),
        _ => runStatus.ToString()
    };

    public static string StepStatusText(ActivityStepRunStatus stepStatus) => stepStatus switch
    {
        ActivityStepRunStatus.Accepted => RemoteLabels.Text("Acceptée", "Accepted"),
        ActivityStepRunStatus.DelayCompleted => RemoteLabels.Text("Délai terminé", "Delay completed"),
        ActivityStepRunStatus.Failed => RemoteLabels.Text("Échec", "Failed"),
        ActivityStepRunStatus.Unknown => RemoteLabels.Text("Résultat incertain", "Unknown result"),
        ActivityStepRunStatus.Cancelled => RemoteLabels.Text("Annulée", "Cancelled"),
        ActivityStepRunStatus.NotRun => RemoteLabels.Text("Non exécutée", "Not run"),
        _ => stepStatus.ToString()
    };

    private static ActivityStepDraft ToDraft(ActivityStepSummary step)
        => new(step.Kind, step.DeviceId, step.ActionId, step.DelayMilliseconds);

    private void EnsureEditable()
    {
        EnsureNotRunning();
        if (!IsEditing) throw new InvalidOperationException("No Activity is being edited.");
    }

    private void EnsureNotRunning()
    {
        if (IsRunning) throw new InvalidOperationException("An Activity is currently running.");
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
