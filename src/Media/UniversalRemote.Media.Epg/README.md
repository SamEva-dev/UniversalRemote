# UniversalRemote.Media.Epg

Provider-independent EPG subsystem for UniversalRemote.

- XMLTV import over HTTP(S)
- credentials kept behind `IMediaCredentialStore`
- exact `GuideId` matching first, normalized channel-name fallback second
- bounded download and bounded programme count
- provider-independent guide model
- optional local JSON cache through `FileEpgCache`

The package does not provide channels or media subscriptions. It only associates programme metadata with live channels already exposed by an authorized media source.
