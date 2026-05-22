<h1 align="center">Jellyfin SIMKL Plugin</h1>
<h3 align="center">Made with real time scrobble</h3>

###

## Repo for Jellyfin
https://raw.githubusercontent.com/romskidd/jellyfin-plugin-simkl/master/manifest.json

###

## Current features
- Multi-user support
- Real-time scrobbling: reports `start` / `pause` / `stop` to Simkl as you watch, so
  titles appear in your live "Watching now" banner and resume across devices
- Automatic watched-marking when you stop past 80% (decided server-side, per the
  [Simkl scrobble guide](https://api.simkl.org/guides/scrobble.md))
- Events are sent only on real player transitions (start, pause, resume, stop) — never
  polled on a timer — to respect Simkl's rate limits and 20-second per-user lock
- Per-user toggles for Movies, TV Shows and a minimum runtime filter
- Easy login using pin
- If the provider ids don't resolve, the item is matched by filename via Simkl's API
  and scrobbled anyway

## Future features
- Sync all watch status with Simkl
- Cross-device "Continue Watching" discovery via `/sync/playback`
