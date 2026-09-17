# UniversalRemote.Media.Playback

Provider-independent local playback orchestration introduced by MEDIA 3.

- Resolves stream URLs only at playback time through `IStreamResolver`.
- Keeps resolved URLs out of `PlaybackSession`, logs and persisted checkpoints.
- Coordinates play / pause / seek / stop through `ILocalPlaybackEngine`.
- Stores only resumable position metadata.
- Remote targets remain out of scope until MEDIA 6.
