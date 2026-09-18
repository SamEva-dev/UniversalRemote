# UniversalRemote.Telemetry

Internal reference-app telemetry for REMOTE-053. Collection is **off by default**, local-only, bounded to coarse operation outcomes/duration buckets and contains no arbitrary tag bag.

The package contains no network client and is intentionally `IsPackable=false` for the first public NuGet wave. MAUI may let the user manually export the local JSON diagnostic file; nothing is uploaded automatically.
