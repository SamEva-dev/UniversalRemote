namespace UniversalRemote.Remote.Hub.Wifi;

/// <summary>Versioned discovery/API constants for the UniversalRemote IR hub firmware contract.</summary>
public static class WifiInfraredHubProtocol
{
    public const int ApiVersion = 1;
    public const string MdnsServiceType = "_universalremote-ir._tcp.local";
    public const string InfoPath = "/api/v1/info";
    public const string TransmitPath = "/api/v1/transmit";
    public const string LearnPath = "/api/v1/learn";
    public const int MaxInfoPayloadBytes = 16 * 1024;
    public const int MaxTransmitRequestBytes = 64 * 1024;
    public const int MaxTransmitResponseBytes = 8 * 1024;
    public const int MaxLearnRequestBytes = 4 * 1024;
    public const int MaxLearnResponseBytes = 64 * 1024;
}
