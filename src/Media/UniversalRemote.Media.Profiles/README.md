# UniversalRemote.Media.Profiles

MEDIA 9 profile foundation.

- Adult / child / guest profiles.
- Active profile persisted separately from provider credentials.
- PIN-gated profile switching; PIN storage is delegated to `IMediaProfilePinStore`.
- Allowed media kinds, blocked categories/items, age ceiling and optional blocking of unrated content.
- Per-profile language, autoplay, subtitle and preferred-target preferences.
- `IMediaAccessPolicy` is enforced by catalogue/library and again before playback.

The profile JSON contains no provider username, password, token, stream URI or plaintext PIN.

Parental-control invariant: as soon as a child or guest profile exists, at least one adult profile must remain PIN-protected. Non-adult profiles cannot manage profile settings in the MAUI client; switching back to a protected adult requires its PIN.
