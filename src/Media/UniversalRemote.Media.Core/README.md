# UniversalRemote.Media.Core

Provider-independent Media orchestration for UniversalRemote.

MEDIA 0 provides:
- `MediaProviderResolver` for stable provider routing.
- `MediaCatalog` for normalized catalogue dispatch.
- `InMemoryMediaSourceRepository` as a non-secret fallback store for tests and samples.
- `AddUniversalRemoteMediaCore()` for dependency injection.

Concrete playlist providers and platform playback services are intentionally not part of this package.
