namespace UniversalRemote.Abstractions;

/// <summary>One ordered step of a multi-device activity.</summary>
public abstract record ActivityStep
{
    public int Position { get; }

    protected ActivityStep(int position)
    {
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position), "Activity step position must be zero or greater.");
        Position = position;
    }
}

/// <summary>A normalized device command inside an Activity. Provider payloads and credentials never belong here.</summary>
public sealed record RemoteActionActivityStep : ActivityStep
{
    public Guid DeviceId { get; }
    public RemoteAction Action { get; }

    public RemoteActionActivityStep(int position, Guid deviceId, RemoteAction action) : base(position)
    {
        if (deviceId == Guid.Empty) throw new ArgumentException("Activity device ID must not be empty.", nameof(deviceId));
        ArgumentNullException.ThrowIfNull(action);
        DeviceId = deviceId;
        Action = action;
    }
}

/// <summary>Explicit pause between commands. Delays are bounded to keep local macros understandable and testable.</summary>
public sealed record DelayActivityStep : ActivityStep
{
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(60);

    public TimeSpan Duration { get; }

    public DelayActivityStep(int position, TimeSpan duration) : base(position)
    {
        if (duration < MinimumDuration || duration > MaximumDuration)
            throw new ArgumentOutOfRangeException(nameof(duration), $"Activity delay must be between {MinimumDuration.TotalMilliseconds:0} ms and {MaximumDuration.TotalSeconds:0} s.");
        Duration = duration;
    }
}

/// <summary>Persisted user scenario. Execution semantics are deliberately implemented by the later Activity runner.</summary>
public sealed class Activity
{
    public const int MaximumSteps = 100;

    public Guid Id { get; }
    public string Name { get; }
    public Guid? RoomId { get; }
    public IReadOnlyList<ActivityStep> Steps { get; }

    public Activity(Guid id, string name, Guid? roomId = null, IEnumerable<ActivityStep>? steps = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Activity ID must not be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (roomId == Guid.Empty) throw new ArgumentException("Activity room ID must not be empty when specified.", nameof(roomId));

        var normalizedName = name.Trim();
        if (normalizedName.Length > 80) throw new ArgumentOutOfRangeException(nameof(name), "Activity name must be 80 characters or fewer.");

        var snapshot = (steps ?? Array.Empty<ActivityStep>()).ToArray();
        if (snapshot.Length > MaximumSteps) throw new ArgumentOutOfRangeException(nameof(steps), $"An activity may contain at most {MaximumSteps} steps.");
        if (snapshot.Any(x => x is null)) throw new ArgumentException("Activity steps must not contain null values.", nameof(steps));
        if (!snapshot.Select(x => x.Position).SequenceEqual(Enumerable.Range(0, snapshot.Length)))
            throw new ArgumentException("Activity step positions must be contiguous, ordered and start at zero.", nameof(steps));

        Id = id;
        Name = normalizedName;
        RoomId = roomId;
        Steps = Array.AsReadOnly(snapshot);
    }
}

/// <summary>Local activity persistence. Saving replaces the complete ordered step list atomically.</summary>
public interface IActivityRepository
{
    Task<Activity?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Activity>> ListAsync(CancellationToken cancellationToken = default);
    Task<Activity> SaveAsync(Activity activity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
