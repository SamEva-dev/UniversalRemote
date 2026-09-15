# UniversalRemote.Hub.Ble

REMOTE-049 adds Bluetooth Low Energy as a replaceable transport for the external UniversalRemote infrared hub.

The package remains platform-neutral. It owns the UniversalRemote BLE GATT profile, identity/connection model, discovery/connector flow, transmit framing and conservative delivery semantics. Actual Bluetooth scanning and GATT I/O are supplied through `IBleInfraredHubRadio`; Android implements that radio in `UniversalRemote.Platform.Android`.

## GATT profile v1

- service: `7d9f1000-7c3a-4d55-a1b8-6f7a1b0c0001`
- info characteristic (read): `7d9f1001-7c3a-4d55-a1b8-6f7a1b0c0001`
- transmit characteristic (write-with-response): `7d9f1002-7c3a-4d55-a1b8-6f7a1b0c0001`
- acknowledgement characteristic (read): `7d9f1003-7c3a-4d55-a1b8-6f7a1b0c0001`
- learning characteristic is reserved for REMOTE-050.

Advertisements expose service-data v1 containing a stable 128-bit hub identifier. Product-facing models never expose the BLE device address.

A transmit request is one logical request even though the Android radio can split it into multiple GATT writes according to the negotiated MTU. Before the first characteristic write starts, failure can be `FailedBeforeSend`. Once any frame write has been initiated, a timeout/disconnect/write failure or untrusted acknowledgement is `Unknown`; the request is never replayed automatically.

`LearnAsync` remains `Unsupported` until REMOTE-050.
