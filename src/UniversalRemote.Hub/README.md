# UniversalRemote.Hub

Hardware-independent external infrared hub primitives for UniversalRemote.

REMOTE-046 deliberately does not choose an ESP32 board, Wi-Fi protocol, BLE GATT profile or discovery mechanism. It provides:

- `IInfraredHubTransport` for replaceable Wi-Fi/BLE transports;
- `InfraredHubClient` as the transport-agnostic hub facade;
- `HubInfraredTransmitter`, which adapts a selected external hub to the existing `IInfraredTransmitter` contract used by Generic IR;
- conservative delivery semantics: an ambiguous transmission is `Unknown` and is never retried automatically by this layer.

Transport endpoints, BLE addresses, pairing credentials and secrets must stay inside future transport implementations and secure platform storage. They must not be exposed through `InfraredHubInfo` or serialized into layouts.
