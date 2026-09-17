# UniversalRemote.Media.Targets

MEDIA 6 package responsible for playback destination discovery and selection.

- `LocalDevice` is always available and launchable.
- Paired Android TV devices are surfaced from the control-domain `IDeviceRepository`.
- Google Cast receivers are discovered through the existing mDNS discovery pipeline.
- A discovered target is not automatically marked launchable. `CanLaunch` only becomes true when UniversalRemote has a real transport capable of launching arbitrary media on that target. This prevents fake/optimistic casting behavior.
- No stream URL, source credential or pairing secret is stored in a target.
