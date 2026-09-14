<p align="center"><img src="assets/banner-1280x640.png" alt="RK Simkl Scrobbler — real-time Simkl scrobbling for Jellyfin" width="820"></p>
<p align="center">
  <a href="https://github.com/romskidd/jellyfin-plugin-simkl-scrobbler/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/romskidd/jellyfin-plugin-simkl-scrobbler?label=release"></a>
  <a href="https://github.com/romskidd/jellyfin-plugin-simkl-scrobbler/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/romskidd/jellyfin-plugin-simkl-scrobbler/total"></a>
  <img alt="Jellyfin 10.11 and 12" src="https://img.shields.io/badge/Jellyfin-10.11.x%20%26%2012.0-00A4DC">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-GPL--3.0-blue"></a>
</p>
<p align="center"><em>Unofficial plugin, not affiliated with or endorsed by Simkl or Jellyfin. Simkl and Jellyfin are trademarks of their respective owners; this is an independent community project.</em></p>

What you watch in Jellyfin shows up on [Simkl](https://simkl.com) as you watch it: the
title appears in your "Watching now" banner the moment playback starts, pauses are
tracked, and the item is marked watched once you stop past 80%. Manual check marks,
rewatches and per-user settings are covered too. Each Jellyfin user links their own
Simkl account.

> [!WARNING]
> **Do not run this plugin and the official Simkl plugin at the same time.** Both would
> scrobble every playback and send each watched item to Simkl twice. Keep one of the two:
> uninstall the other from Dashboard → Plugins before you link an account here.

## Why this one

The official [Simkl plugin](https://github.com/jellyfin/jellyfin-plugin-simkl) is
maintained and scrobbles in real time since its version 9. This fork exists for what it
does not cover:

- **Users link their own Simkl account themselves.** The official plugin already keeps a
  separate Simkl login per Jellyfin profile, but only an administrator can set each one up
  from the dashboard. Here every user has an **RK Simkl Scrobbler** entry in their own
  menu and links their own account from there, with their own settings — no dashboard
  access, nothing else to install.
- **The history syncs both ways.** Bring a Simkl history into a fresh Jellyfin library,
  send what Jellyfin already knows to Simkl, or keep the two aligned from then on — with
  a preview before every write and a 7-day undo. *(Requested upstream in
  [#22](https://github.com/jellyfin/jellyfin-plugin-simkl/issues/22) and
  [#31](https://github.com/jellyfin/jellyfin-plugin-simkl/issues/31).)*
- **It runs on Jellyfin 10.11.x and on 12.0.** The official version 9 targets 12.0 only.

Plus rewatch sessions, per-user library exclusions, and a Logs tab that builds a
diagnostic report with no tokens in it.

## What it looks like

<p align="center"><img src="docs/screenshot-plugin-page.png" alt="The plugin's admin page, on the Import / Export & sync tab" width="900"></p>

The **Import / Export & sync** tab: the guided box walks through the three steps, each
button unlocking once the previous one has run, and the counters report what each pass
did. Below it, the Import and Export cards let either direction run on its own.

## Installation

1. In Jellyfin: **Dashboard → Plugins → Repositories → Add**, with this URL:

   ```
   https://raw.githubusercontent.com/romskidd/jellyfin-plugin-simkl-scrobbler/master/manifest.json
   ```
2. Open the **Catalog** tab, install **RK Simkl Scrobbler**, and restart Jellyfin.
3. The plugin appears in the dashboard sidebar, under the plugins section.

**Beta channel.** Beta builds are published through a separate repository, so they
never reach servers on the URL above. To try them, add this URL instead (or as well;
Jellyfin then offers the newest version of the two):

```
https://raw.githubusercontent.com/romskidd/jellyfin-plugin-simkl-scrobbler/master/manifest-beta.json
```

The beta channel currently carries the same build as the stable one; it is where the
next round of changes will appear first.

Runs on Jellyfin 10.11.x and on Jellyfin 12. A free Simkl account is enough; rewatch tracking needs
Simkl Pro or VIP, as Simkl only offers it there.

## Linking a Simkl account

- **Administrators**: open the plugin page in the dashboard, pick a Jellyfin profile,
  click **Log In**, enter the code at simkl.com/pin. The page updates on its own once
  Simkl accepts the code.
- **Everyone else**: every user finds an **RK Simkl Scrobbler** entry in their own
  menu (the avatar menu on Jellyfin 12, the side drawer on 10.11), leading to a
  self-service page where they link their own account and set their options. No
  dashboard access, nothing else to install. The administrator can also share the
  page's link, shown on the plugin page, and can turn the menu entry off there.

Each profile keeps its own Simkl login and settings.

## Features

**Scrobbling**
- Real-time `start` / `pause` / `stop` events, so titles appear in your live
  "Watching now" banner and paused positions follow you across Simkl-connected devices
- Marked watched when you stop past 80% (decided by Simkl, per its scrobble guide)
- Events follow real player actions; while playing, the position is also sent again
  every 5 minutes and right after a skip or a speed change, so the banner keeps up
  (can be turned off per profile)
- Items that Simkl can't match by IMDb/TMDb/TVDB ids are matched by filename

**Marking and rewatches**
- Ticking an episode, a season or a movie as played in Jellyfin adds it to your
  Simkl history in one batched request; unticking can remove it (off by default)
- **Rewatches** (Simkl Pro / VIP): finishing something you had already watched is
  filed by Simkl as a rewatch session, and later episodes of the same show join it.
  Off by default. The plugin only offers a stop as a rewatch when at least half of
  the item played in that session, so resuming near the end never counts; Simkl
  applies its own rules on top (item already watched, two days between viewings)

**Simkl sync** (off by default)
- Set up in three steps from the **Import / Export & sync** tab, guided by a status box (each button unlocks after the previous one): **1. Simkl to Jellyfin** marks
  as played what your Simkl history lists (watch date and play count included, only
  items present in your libraries), **2. Jellyfin to Simkl** sends what Jellyfin has
  as played and Simkl doesn't have yet, **3. Keep in sync** then follows the Simkl
  history after each playback and library scan, at most once an hour
- Every step shows a preview before anything is written; both directions can be
  undone for 7 days; a pass that would change more than 200 items waits for your
  confirmation; excluded libraries are left alone; anime is covered, its seasons
  matched through Simkl's own TVDB season mapping
- Items Simkl lists that Jellyfin can't match are counted and retried after each
  library scan
- Each linked profile sets it up on its own page (admin page or self-service page)

**Per user**
- Movies and shows on or off, minimum runtime filter
- Library exclusions, to keep home videos or kids' content off Simkl
- Watch statistics, the last scrobble with an **Open on Simkl** link, and the last
  rewatch, on both the admin page and the self-service page

**Reliability**
- A stop that Simkl doesn't confirm is queued and replayed for up to 24 hours, so a
  network blip on the final event doesn't lose an episode
- Finished watches are kept while a Simkl link is expired and sent once the account
  is linked again (up to 30 days)
- A **Logs** tab on the admin page builds a diagnostic report (versions, a settings
  summary without tokens, the plugin's recent log lines) to paste into a bug report
- An expired or rejected Simkl login is detected at startup and reported as
  "Link expired" instead of looking connected while nothing scrobbles
- Requests follow Simkl's API guidelines: one write per second per user, settings
  and statistics re-read only when Simkl reports a change, PIN status polled at the
  interval Simkl asks for

## Upgrade notes

> [!NOTE]
> **9.7.0.0** brings the Simkl sync to the stable channel, and one build now covers both
> Jellyfin 10.11.x and Jellyfin 12. The sync stays off until you set it up, and the
> scrobbling side is unchanged. Please report anything odd in the
> [issues](https://github.com/romskidd/jellyfin-plugin-simkl-scrobbler/issues), with the
> report from the **Logs** tab.

> [!IMPORTANT]
> **Coming from 9.3.0.0 or earlier?** Since 9.4.0.0 the plugin runs as its own Simkl
> application, and Simkl tokens are bound to the application that issued them. Every
> user has to **link their Simkl account again, once**. The plugin shows "Link expired"
> with the usual PIN button; all settings are kept.

> [!NOTE]
> **Renamed in 9.5.0.0.** "Simkl Scrobbler" became **RK Simkl Scrobbler** at Simkl's
> request, to avoid confusion with Simkl's own apps and the official Jellyfin plugin.
> The plugin id is unchanged, so updates continue as before; nothing to do.

> [!WARNING]
> **Installed a version ≤ 9.0.0.4 (before 2026-08-30)?** Those builds still shared the
> official plugin's id and no longer receive updates. Uninstall the old "Simkl" plugin,
> then install RK Simkl Scrobbler from the repository above. Your Simkl login and
> settings are kept.

Don't run this plugin together with the official Simkl plugin: both would scrobble the
same playbacks twice.

## Privacy and Simkl API usage

The plugin talks only to Simkl's API, on behalf of each linked user, with that user's
own token. It sends what is needed to identify the title (IMDb/TMDb/TVDB ids, title,
year, season and episode numbers) plus playback progress. Nothing is sent to the
plugin author or anyone else. The plugin identifies itself to Simkl as
`rk-simkl-scrobbler` and was reviewed against Simkl's API guidelines with the Simkl
team's feedback.

## Troubleshooting

- **Nothing scrobbles**: check that the profile is linked, that the item's library is
  not excluded, and that the item is longer than the minimum runtime. The server log
  has one line per scrobble event under `Jellyfin.Plugin.Simkl`.
- **"Link expired"**: Simkl no longer accepts the stored token (typically after the
  9.4.0.0 update). Link again with the PIN button.
- **No rewatch recorded**: rewatches need Simkl Pro or VIP, the option must be on for
  that profile, at least half of the item must have played in that session, and Simkl
  ignores a second viewing of the same episode within two days.
- **Two scrobbles per playback**: the official Simkl plugin is installed alongside.
  Keep one of the two.
- **Import or Export stays greyed out**: run the **Preview** of that step first; the
  button unlocks once you have seen what would change.
- **Turn on sync stays greyed out**: steps 1 and 2 have to be done first, on that profile. It acts at once, nothing to save.
- **Reporting a bug**: open the **Logs** tab, click **Copy report** and paste it in
  the issue. The report contains no tokens.

## About

This project started as a fork of the official
[jellyfin-plugin-simkl](https://github.com/jellyfin/jellyfin-plugin-simkl), at a time
when it marked items watched only after playback. Real-time scrobbling was added on top,
then per-user accounts and the two-way sync. The official plugin is still maintained and
has since added live scrobbling of its own; this fork is not a replacement for it, and
the scrobbling core still owes it a lot. Since 9.1.0.0 this plugin is independent, with
its own plugin id, maintained by
[romskidd](https://github.com/romskidd). Thanks to the Simkl team for their API and
their review. Feedback, bug reports and ideas are welcome in the
[issues](https://github.com/romskidd/jellyfin-plugin-simkl-scrobbler/issues).

## What's new

- **9.7.2.0** (2026-09-14) — no duplicate menu entry when upgrading with Plugin Pages installed; warning against running this plugin and the official one together.
- **9.7.1.0** (2026-09-12) — an **RK Simkl Scrobbler** entry in every user's menu, with nothing else to install; Settings tab; Plugin Pages integration retired.
- **9.7.0.0** (2026-09-12) — the two-way Simkl sync reaches the stable channel; one build for Jellyfin 10.11.x and 12; Logs tab; logo.

The full history is in [CHANGELOG.md](CHANGELOG.md), and each release has its own notes on the
[releases page](https://github.com/romskidd/jellyfin-plugin-simkl-scrobbler/releases).

## License

See [LICENSE](LICENSE).
