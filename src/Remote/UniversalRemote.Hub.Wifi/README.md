# UniversalRemote.Hub.Wifi

REMOTE-047 adds the first concrete external-hub transport while keeping hardware and firmware replaceable.

The package discovers `_universalremote-ir._tcp.local` through the existing mDNS pipeline, accepts only private/local literal IP addresses, validates the versioned `/api/v1/info` response, verifies that the returned hub ID matches the discovered/stored ID, and persists connection metadata only after successful validation.

The product-facing `InfraredHubInfo` never exposes IP addresses or connection records. MAUI supplies a SecureStorage-backed `IWifiInfraredHubConnectionStore`; desktop/test consumers fall back to an in-memory store.

REMOTE-047 intentionally does not send IR frames or start learning captures yet. `TransmitAsync` therefore returns `FailedBeforeSend` after a connection exists, which proves that no ambiguous delivery occurred. REMOTE-048 will add the versioned transmit request on this same transport; learning follows afterwards.
