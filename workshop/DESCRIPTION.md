[Steam Workshop item description - paste into the workshop upload form]

Title:
AutoAnthony - Relics (Chaos Relic Generator / Dongni Algorithm - Relics)

Tags: Relics, Gameplay, Balanced

English:

AutoAnthony - Relics: every run, 60 chaos relics are generated from the run seed with the Anthony algorithm. Each carries 3-15 random entries (combat start / turn start / on card play / passive / on victory hooks), with per-slot rarity, name, and a dynamic entry-list description.

How it works
- 60 relic slots (Chaos Relic 1-60) injected into the shared relic pool: they show up in elite/combat rewards and shops like any vanilla relic.
- Rarity spread: 20 Common / 20 Uncommon / 20 Rare per run, regenerated per seed.
- Entry counts scale like AutoAnthony cards: 3 x (1 + weighted rank), clamped 3-15.
- Same seed = same relics. Different seed = completely different entries for the same slot.
- Seed is captured at run setup (before the reward pool is populated) so rarity resolution matches the generator exactly.
- Ancient / Neow relic pools stay untouched (vanilla-only per design).
- Multiplayer-safe random targeting (uses the run's CombatTargets RNG channel).

Requires: BaseLib (3.4.5+)

Configuration (Settings -> Mod Settings):
- EnableChaosRelics: master switch (default on)
- ChaosRelicMultiplier: entry-count multiplier (default 3)

Console verification (optional): in a run, open the dev console and use
  relic add AUTOANTHONYRELICS-CHAOS_RELIC005
to grant a chaos relic immediately; check the top-bar relic icons.

Compat: works alongside AutoAnthony (cards) but does NOT require it.

Chinese (Simplified):

AutoAnthony - Relics (Dongni Algorithm - Relics): each run generates 60 chaos relics from the run seed using the Anthony algorithm. Each relic carries 3-15 random entries (combat start / turn start / on card play / passive / on victory hooks), with independent rarity, name, and a dynamically generated entry-list description.

Mechanics
- The 60 relic slots (Chaos Relic 1-60) enter the shared relic pool and appear in elite/combat rewards and shops just like vanilla relics.
- Rarity distribution: 20 Common / 20 Uncommon / 20 Rare per run, regenerated with the seed.
- Entry counts follow the AutoAnthony card algorithm: 3 x (1 + weighted rank), capped at 3-15.
- Same seed = same relics; different seed = completely different entries for the same slot.
- The seed is captured at run setup (before the reward pool is populated), so rarity resolution fully matches the generator.
- Ancient / Neow relic pools are unaffected (kept vanilla by design).
- Multiplayer-safe random targeting (uses the run's CombatTargets random channel).

Requires: BaseLib (3.4.5+)

Configuration (Settings -> Mod Settings):
- EnableChaosRelics: master switch (default on)
- ChaosRelicMultiplier: entry-count multiplier (default 3)

Console verification (optional): inside a run, open the dev console and run
  relic add AUTOANTHONYRELICS-CHAOS_RELIC005
to directly obtain a chaos relic; check the relic icons in the top bar.

Compatibility: can be used alongside AutoAnthony (card side) but does not depend on it.

Change log
v0.3.0 (2026-09-08)
- FIX: chaos relics now actually enter the reward pool (engine SharedRelicPool injection; previously they only appeared in the compendium).
- FIX: run seed is captured before the reward pool is populated, so rarities resolve correctly (20/20/20 spread) instead of all-Common.
- FIX: split the dual-target Harmony patch class (only the last target was being patched).
- NEW: 60 distinct procedurally-generated placeholder icons + outlines + big icons.
- MP: random targeting now uses the seeded CombatTargets RNG channel.
v0.2.0 - entry generator, dynamic localization, config options.
v0.1.0 - initial slot scaffold.
