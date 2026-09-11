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

## Option A - steamcmd (automated)

steamcmd is installed at `G:\omp works\.tooling\steamcmd\steamcmd.exe`.

Run (replace YOUR_STEAM_LOGIN; it will prompt for password and Steam Guard code):

```
cd G:\omp works\.tooling\steamcmd
steamcmd.exe +login YOUR_STEAM_LOGIN +workshop_build_item "G:\omp works\AutoAnthonyRelics\workshop\workshop_upload.vdf" +quit
```

Notes:
- Workshop upload REQUIRES a non-anonymous account.
- Steam Guard: first login on a new device emails/sends a code; enter it when prompted,
  then steamcmd caches the sentry in `G:\omp works\.tooling\steamcmd\config\` for later runs.
- The account must own Slay the Spire 2 (app 2868840) and satisfy Steam's community
  posting requirements (spent USD 5 or similar).
- After the first publish, note the printed `Published file id ...` and write it into
  `workshop_upload.vdf` -> `publishedfileid` so future runs UPDATE instead of duplicating.

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
  `content/`, edit `changenote` in the VDF, rerun the steamcmd command.
