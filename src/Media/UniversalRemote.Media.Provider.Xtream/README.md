# UniversalRemote.Media.Provider.Xtream

Provider catalogue for Xtream-compatible **Player API** servers (`player_api.php`). It is intended only for media sources the user is authorized to access.

## Supported in MEDIA-2

- authentication/status validation;
- Live TV categories + channels;
- VOD categories + movies;
- series categories + series;
- lazy series detail loading with seasons and episodes;
- provider-independent `MediaItem`, `MediaSeriesDetails`, `MediaSeason` and `MediaEpisode` normalization;
- secure credential indirection through `IMediaCredentialStore`;
- redacted HTTP/JSON errors and bounded response sizes.

The provider never places username/password or playback URLs inside normalized media-domain models. Actual stream URL resolution belongs to the playback sprint.

`http://` servers are accepted for compatibility with existing installations, but HTTPS should be preferred because Player API credentials are transmitted with each request.
