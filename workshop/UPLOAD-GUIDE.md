# Steam Workshop Upload Guide - Qurious Crafting - Relics

Prepared 2026-09-08. Everything is staged; only credentials/2FA are needed to publish.

## What's staged (this folder)

- `content/QuriousCraftingRelics/` - the mod payload exactly as the game loads it:
  `QuriousCraftingRelics.dll`, `QuriousCraftingRelics.pck`, `QuriousCraftingRelics.json`.
  This mirrors the structure of installed workshop items (e.g. BaseLib's item folder).
- `preview.png` - 512x512 preview (4x4 fan of slot icons on dark gradient).
- `DESCRIPTION.md` - full bilingual description text to paste into the workshop page.
- `workshop_upload.vdf` - steamcmd build script. `publishedfileid` is empty = creates a NEW
  item on first run; after creation, put the returned item id into `publishedfileid` for updates.

## Option A - guarded push (recommended)

Publish through the controlled entry. It refreshes the staged payload, scans the staged bytes
for held-back layers, runs the VDF quote/backslash, byte-size, description-drift and Markdown
guards, applies the login throttle, and verifies the upload afterwards:

```
powershell -NoProfile -ExecutionPolicy Bypass -File "G:\omp works\.tooling\workshop-push-all.ps1" -Only Qurious --2FACode XXXXX
```

The per-repo shell `G:\omp works\Sts\AutoAnthonyRelics\workshop\workshop-push.ps1` is a thin
forwarding shell for that same entry: it carries this repo's VDF and its `-Only Qurious` target
as literals, refuses a `-Vdf` that is not this repo's (exit 11, before any process starts), and
passes the entry's exit code back unchanged.

```
powershell -File "G:\omp works\Sts\AutoAnthonyRelics\workshop\workshop-push.ps1" -Vdf "G:\omp works\Sts\AutoAnthonyRelics\workshop\workshop_upload.vdf"
```

Notes:
- Workshop upload REQUIRES a non-anonymous account.
- The 2FA code must be supplied AT LAUNCH via `--2FACode`; the entry never prompts for it.
  No `ssfn*` sentry is cached under `G:\omp works\.tooling\steamcmd\config\`, so every run
  needs a fresh code (or a phone confirmation). Do not rely on caching.
- The account must own Slay the Spire 2 (app 2868840) and satisfy Steam's community
  posting requirements (spent USD 5 or similar).
- After the first publish, note the printed `Published file id ...` and write it into
  `workshop_upload.vdf` -> `publishedfileid` so future runs UPDATE instead of duplicating.
- Do NOT call `steamcmd.exe +workshop_build_item` on this VDF directly: that skips every guard
  above. (An earlier revision of this guide recommended exactly that.)

## Option B - manual (no credentials needed from tools)

Steam does not provide web upload for third-party workshop items, so manual publishing
still needs a one-time steamcmd login (Option A). If avoiding steamcmd entirely:

1. Start the game -> Mods screen -> the game can consume local mods only; the workshop
   item must still be created through Steamworks tooling (Option A) - there is no
   in-game upload UI (NModdingScreen only links to the workshop site).

## After publishing

- Verify the item page loads: https://steamcommunity.com/sharedfiles/filedetails/?id=<publishedfileid>
- Subscribe from another machine/profile (or same) -> Steam downloads to
  `G:\steam\steamapps\workshop\content\2868840\<item id>\QuriousCraftingRelics\` -> game
  picks it up in the Mods screen.
- Unsubscribe test: confirm removal works.
- Update flow: bump `version` in `mod/QuriousCraftingRelics.json`, rebuild, re-stage
  `content/`, edit `changenote` in the VDF, rerun the push command.
