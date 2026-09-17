# UniversalRemote.Media.Abstractions

Provider-independent contracts for the UniversalRemote Media domain.

MEDIA 0 guarantees:
- `MediaSource` is distinct from a physical `Device`.
- `MediaItem` never carries playback URLs or credentials.
- secrets are referenced through `CredentialReference` and accessed through `IMediaCredentialStore`.
- playback destinations are represented by `IPlaybackTarget` / `PlaybackTarget` and their capabilities.
- concrete M3U, Xtream-compatible, EPG and playback implementations live in later packages.
