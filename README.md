<h1 align="center">Simkl Scrobbler for Jellyfin</h1>
<h3 align="center">Real-time Simkl scrobbling</h3>

> [!WARNING]
> **Installed a version ≤ 9.0.0.4 of this plugin (before 2026-08-30)?**
> The plugin changed its id in **9.1.0.0** and is now fully independent from the
> official Simkl plugin — old installs will **not** receive updates anymore.
> One-time fix: uninstall the old "Simkl" plugin, then install **"Simkl Scrobbler"**
> from the same repo URL below. Your Simkl login and settings are kept.
> Details in [the pinned issue](https://github.com/romskidd/jellyfin-plugin-simkl-scrobbler/issues).

###

## About this plugin

This started as a fork of the official [jellyfin-plugin-simkl](https://github.com/jellyfin/jellyfin-plugin-simkl).
The official plugin only marks items as watched **after** playback (via Simkl's
`/sync/history`). This plugin adds **real-time scrobbling** on top of it: the
`start` / `pause` / `stop` lifecycle, the live "Watching now" banner on simkl.com,
and automatic watched-marking at 80%.

Since version **9.1.0.0** it is a fully independent plugin ("Simkl Scrobbler",
maintained by [romskidd](https://github.com/romskidd)) with its own plugin id:

- It can never be overwritten by an update of the official plugin, and its
  updates come only from this repository (add the repo URL below to your
  Jellyfin plugin catalogs to receive them).
- Don't run it together with the official Simkl plugin — both would scrobble
  the same playbacks twice. Install one or the other.
- If you installed a version **≤ 9.0.0.4** of this fork (which still shared the
  official plugin id): uninstall it, then install "Simkl Scrobbler" from this
  repo. Your Simkl login and settings are kept (the configuration file is
  unchanged).

## Repo for Jellyfin
Dashboard → Plugins → Repositories → add:

```
https://raw.githubusercontent.com/romskidd/jellyfin-plugin-simkl-scrobbler/master/manifest.json
```

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
- Manual check marks sync too: ticking an episode, a season or a movie as played
  adds it to your Simkl history (batched into a single request), and optionally
  unticking removes it (off by default)
- The settings page shows your Simkl watch statistics and the result of the
  last scrobble
- **Self-service linking**: every user can connect their own Simkl account from
  a page of their own, without dashboard access — share the link shown in the
  settings page (and it also appears in the sidebar if the optional Plugin Pages
  plugin happens to be installed)
- Watches that Simkl doesn't confirm are queued and replayed for up to 24 hours,
  so a network blip on the final event doesn't silently lose an episode
- Per-user library exclusions, to keep home videos or kids' content off Simkl
- An expired Simkl login is reported instead of failing silently
- Easy login using pin
- If the provider ids don't resolve, the item is matched by filename via Simkl's API
  and scrobbled anyway

## Version history

| Version | Date | Changes |
|---|---|---|
| **9.3.0.0** | 2026-08-31 | Self-service linking: any user can connect their own Simkl account from a standalone page, no dashboard access needed (optional Plugin Pages integration adds it to the sidebar; no dependency either way). Watches Simkl doesn't confirm are queued and replayed for up to 24h. Per-user library exclusions. An expired Simkl login is now reported instead of failing silently. The plugin also appears in the dashboard sidebar and the settings page was redesigned. |
| **9.2.0.2** | 2026-08-31 | Fixes over 9.2.0.0: Simkl returns an empty body for URLs with a trailing slash + query parameters, which broke the settings page (infinite spinner) and the filename fallback — URLs are now normalized. Watch statistics use the correct endpoint (`POST /users/{id}/stats`) and the page shows your account type. The profile selector now opens on the logged-in user, so Save/Log In always target the profile shown. The page no longer blocks if a Simkl call fails. (9.2.0.1 was an unreleased intermediate build.) |
| 9.2.0.0 | 2026-08-31 | **Broken — use 9.2.0.2.** Manual "mark played" (and optional unmark) now syncs to Simkl, batched per season. Settings page shows Simkl watch stats and the last scrobble result. Every API request now identifies the app (`app-name`/`app-version`), required by Simkl since April 2026. |
| **9.1.0.0** | 2026-08-30 | New independent identity: own plugin id, name "Simkl Scrobbler", owner romskidd. No functional changes over 9.0.0.4. |
| **9.0.0.4** | 2026-08-30 | Fix: episodes were scrobbled with the *episode's* own provider ids (e.g. the episode IMDB id) in the `show` object, which Simkl often can't resolve. The parent series is now looked up through the Jellyfin library and its series-level IMDB/TMDB/TVDB ids are sent instead. Fixes tracking for shows whose episodes carry their own IMDB ids (e.g. Euphoria US, For All Mankind) and avoids the flaky filename-search fallback. |
| **9.0.0.3** | 2026-06-01 | Pin `Jellyfin.Controller` to exactly 10.11.8 so the plugin loads on all Jellyfin 10.11.x servers. First stable release of the log-spam fix below. |
| 9.0.0.2 | *(never released)* | Same as 9.0.0.1 but accidentally built against Jellyfin.Controller 10.11.10 — failed to load on older 10.11.x servers. Superseded by 9.0.0.3. |
| 9.0.0.1 | *(tag only)* | Fix: stop the per-second "user not logged in" log spam. Sessions that can't be scrobbled are evaluated once and then skipped silently. |
| **9.0.0.0** | 2026-05-22 | First fork release: real-time scrobbling (`start`/`pause`/`stop` lifecycle, "Watching now" banner, server-side watched-marking at 80%), settings toggles, filename fallback. |
| ≤ 8.0.0.0 | — | Upstream history: see the [official plugin releases](https://github.com/jellyfin/jellyfin-plugin-simkl/releases). |

## Future features
- Sync all watch status with Simkl
- Cross-device "Continue Watching" discovery via `/sync/playback`
