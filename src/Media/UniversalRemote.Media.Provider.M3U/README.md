# UniversalRemote.Media.Provider.M3U

M3U/M3U8 catalogue and playback resolver for authorized user sources.

Responsibilities:
- downloads remote HTTP(S) playlists with a bounded payload size;
- parses `#EXTINF`, `tvg-id`, `tvg-name`, `tvg-logo`, `group-title` and common EPG header attributes;
- resolves relative stream/logo/EPG URIs against the playlist URI;
- normalizes entries into `MediaItem` objects without exposing stream URLs or source credentials;
- rejects HLS playback manifests when they are accidentally configured as catalogue playlists;
- resolves the selected stream URL only at playback time through `M3uStreamResolver` / `IStreamResolver`;
- emits sanitized parser/load errors that do not contain playlist URLs or tokens.

The secure credential referenced by `MediaSource.CredentialReference` contains the M3U playlist HTTP(S) URI.
