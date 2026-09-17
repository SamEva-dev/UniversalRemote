# UniversalRemote.Media.Activities

MEDIA 8 composes the existing device `Activity` engine with provider-independent media playback.

A media scenario stores only an existing preparation Activity ID, a normalized `MediaReference`, a playback-target selector and a failure policy. It never stores stream URLs, playlist credentials or provider payloads.

Typical scenario:

1. run an existing preparation Activity (`Power`, `HDMI 1`, delays, etc.);
2. stop or continue according to `ActivityFailurePolicy`;
3. resolve the media item only at execution time;
4. verify that the selected playback target can really launch content;
5. start playback through `IPlaybackService`.

No implicit command retry is added. In particular, `PowerToggle` is never generated automatically because a toggle cannot safely infer the current physical power state.
