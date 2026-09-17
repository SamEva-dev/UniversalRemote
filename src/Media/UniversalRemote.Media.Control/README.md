# UniversalRemote.Media.Control

Provider-independent bridge between a selected media playback target and the existing UniversalRemote device-control engine.

The package never contains manufacturer checks. Remote buttons are exposed only when the linked `Device` advertises the matching `RemoteAction` capability. Local playback stays in `IPlaybackService`; physical-device commands stay in `IRemoteControl`.
