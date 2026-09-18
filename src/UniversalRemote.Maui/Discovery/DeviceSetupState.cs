using UniversalRemote.Remote.Application;

namespace UniversalRemote.Maui.Discovery;

/// <summary>Short-lived navigation state used immediately after a successful pairing.</summary>
public sealed class DeviceSetupState
{
    public Guid DeviceId { get; private set; }
    public string SuggestedName { get; private set; } = string.Empty;
    public bool HasPendingDevice => DeviceId != Guid.Empty;

    public void Begin(PairedDeviceSummary paired)
    {
        ArgumentNullException.ThrowIfNull(paired);
        DeviceId = paired.DeviceId;
        SuggestedName = paired.DisplayName;
    }

    public void Clear()
    {
        DeviceId = Guid.Empty;
        SuggestedName = string.Empty;
    }
}
