using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Hub;

/// <summary>Repeatable acceptance recipe for REMOTE-050. Passing it does not automatically promote every hub/device.</summary>
public sealed class InfraredHubPhysicalRecipe
{
    public IReadOnlyList<RemoteAction> RequiredActions { get; }
    public bool RequiresPhoneWithoutNativeIr { get; }
    public bool RequiresLearningOrImport { get; }
    public bool RequiresActivityExecution { get; }
    public int TargetP95LatencyMs { get; }

    public InfraredHubPhysicalRecipe(
        IEnumerable<RemoteAction> requiredActions,
        bool requiresPhoneWithoutNativeIr,
        bool requiresLearningOrImport,
        bool requiresActivityExecution,
        int targetP95LatencyMs)
    {
        ArgumentNullException.ThrowIfNull(requiredActions);
        var actions = requiredActions.Distinct().ToArray();
        if (actions.Length == 0) throw new ArgumentException("At least one action is required.", nameof(requiredActions));
        if (targetP95LatencyMs is <= 0 or > 10_000) throw new ArgumentOutOfRangeException(nameof(targetP95LatencyMs));
        RequiredActions = Array.AsReadOnly(actions);
        RequiresPhoneWithoutNativeIr = requiresPhoneWithoutNativeIr;
        RequiresLearningOrImport = requiresLearningOrImport;
        RequiresActivityExecution = requiresActivityExecution;
        TargetP95LatencyMs = targetP95LatencyMs;
    }
}

public static class BuiltInInfraredHubRecipes
{
    public static InfraredHubPhysicalRecipe Remote050 { get; } = new(
        [RemoteActions.PowerToggle, RemoteActions.VolumeUp, RemoteActions.VolumeDown],
        requiresPhoneWithoutNativeIr: true,
        requiresLearningOrImport: true,
        requiresActivityExecution: true,
        targetP95LatencyMs: 300);
}
