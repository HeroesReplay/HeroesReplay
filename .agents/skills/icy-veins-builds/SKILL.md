---
name: icy-veins-builds
description: >
  Refresh the talent-build reference (build names, talents, guide links) from the Icy Veins
  Heroes of the Storm guides with a real browser, then check and write it with
  `heroesreplay builds import`. Use after a patch changes talents, when guides change, or for
  /icy-veins-builds.
---

# Icy Veins talent builds

The YouTube talent-build lines (#412) name the guide build each player's talents fit and link the guide. They read `src/HeroesReplay.Core/Builds/Reference/icy-veins-builds.json`, embedded in Core. The product never fetches a guide; this skill refreshes the file by hand, about once per patch.

## What may be stored

Only facts: each hero's guide link and "last updated" day, each build's name and tag (Recommended, Advanced, Situational, Beginner, ARAM, Not recommended), its calculator link, and per tier the talents it recommends or calls situational, as catalog talent ids. Never guide text, descriptions, or tips. Every description that names a build credits Icy Veins and links the guide.

## Rules

- A real browser only: the app's built-in browser or Claude in Chrome. Icy Veins answers plain HTTP clients (curl, WebFetch) with a Cloudflare 403. Never work around that.
- If Cloudflare shows an interactive "verify you are human" check, stop and ask the operator to complete it. Never solve it.
- One guide every 2 seconds at most. `extract.js` already waits. About 4 minutes for all 91.
- Read-only on the site: no sign-in, no forms, no comments.

## Steps

1. Open `https://www.icy-veins.com/heroes/bruiser-hero-guides` in the browser. Any role index page links all 91 guides.
2. Run `extract.js` (this folder) in that tab. It returns the guide count at once and keeps reading in the background.
3. Poll `window.__icyVeinsExtract.done` until it is true. `window.__icyVeinsExtract.errors` lists any guide that failed.
4. Copy `window.__icyVeinsExtract.json` into a file outside the repo, for example `%TEMP%\icy-veins-extract.json`. It is about 60 KB, so read it in slices (`.json.slice(0, 40000)`, then the rest) if the tool caps its output.
5. Import it:

   ```powershell
   heroesreplay builds import --input $env:TEMP\icy-veins-extract.json --reference src/HeroesReplay.Core/Builds/Reference/icy-veins-builds.json --checked-at 2026-10-12
   ```

   The import checks every tier against the heroes-data2 catalog. A tier's marks are the game's talent columns, left to right. A tier whose mark count differs from the catalog tier predates a talent change, so it is kept as stale and never scored (warning). A recommended talent whose name differs from the talent in that column is a warning; the 2026-10-12 read had 19, all punctuation (`c-thuns-gift`, `CThun's Gift`). A guide that names no catalog hero, a build without seven tiers, or a mark that is not `y`, `s`, or `n` refuses the import (exit 2) and writes nothing.
6. Spot-check with `heroesreplay builds match --replay <file>` on a recent replay, then open a PR with the reference diff.

## Slugs

A hero's slug is its English name in lower case, with accents, apostrophes and dots dropped and spaces as hyphens (`li-ming`, `kaelthas`, `sgt-hammer`, `lucio`). The exceptions are in `IcyVeins` (`dva`, `e-t-c`, `kel-thuzad`). Cho and Gall have separate guides. A new hero whose slug breaks the rule fails the import with "names no hero in the catalog"; add it to the exceptions.

## The 2026-10-12 read

91 guides, 296 builds: Situational 100, Recommended 91, ARAM 67, Advanced 31, Beginner 6, Not recommended 1. Every tier matched the 2.57.0.98304 talents. Guide dates ran from 2020 (Li-Ming) to 2026; 276 builds were updated in 2025 or 2026. 291 builds have a calculator link (Deckard's guide has none). Chromie's tiers are labelled 1, 2, 5, 8, 11, 14, 18, so tiers are matched by order, never by level.
