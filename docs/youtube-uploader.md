# YouTube uploader

This is what the uploader does with a replay on the develop line. The next game on the Twitch stream is chosen by the Heroes Profile download queue and by Twitch requests. The score below does not pick that game, and it does not pick which pending file uploads next.

Production is `HEROES_REPLAY_ENV=prod` (`DESKTOP-8SJEK72`). The environment decides, not the machine name. Configuration is `appsettings.json`, then `appsettings.secrets.json`, then `appsettings.prod.json`, then any `HEROES_REPLAY_` environment variable. A later layer wins for one key. `HEROES_REPLAY_ReplayMedia__MaxPublicPerDay` overrides `ReplayMedia:MaxPublicPerDay`.

## Production settings

`appsettings.prod.json` is the production policy. It records every spectated replay and offers every eligible recording to YouTube, then the caps below space the public videos.

```json
"YouTube": {
  "Enabled": true,
  "DryRun": false,
  "PrivacyStatus": "public"
},
"ReplayMedia": {
  "Version": "1",
  "RecordingMode": "All",
  "PublicationMode": "AllEligible",
  "MaxPublicPerDay": 6,
  "MaxPublicPerWeek": 30,
  "MinimumPublicInterval": "02:00:00",
  "OrdinaryCandidateMaxAge": "3.00:00:00",
  "MapCooldown": "08:00:00",
  "RankCooldown": "08:00:00",
  "FeaturedHeroCooldown": "08:00:00",
  "MaxSharedHeroes": 4,
  "ReservedRequestSlotsPerDay": 2,
  "MaxInsertsPerQuotaDay": 80
}
```

`OrdinaryCandidateMaxAge` of `3.00:00:00` is 72 hours. `72:00:00` is 72 days, because that is how .NET reads a time span.

The base `appsettings.json` has the same ten algorithm keys and does not select a recording mode or a publication mode, so a process with no overlay records nothing and publishes nothing. Dev (`appsettings.dev.json`) uses the same modes as production, with `YouTube:DryRun` true, `PrivacyStatus` private, and a `[TEST]` title. Dev inherits the caps from the base file.

`PublicationMode` `AllEligible` is the live channel: a paced archive of the Storm League games the stream already plays. `Curated` is the other content policy. It sends a paid request, a pentakill or team wipe, or a high-skill game, and it does not send an ordinary game. High skill does nothing until `MinimumHighSkillRank` or `MinimumHighSkillMmr` is set. Production does not set either, so no replay is high-skill.

A highlights-only channel would replace the two mode lines and add a floor:

```json
"RecordingMode": "All",
"PublicationMode": "Curated",
"MinimumHighSkillRank": "Master",
"MinimumHighSkillMmr": 2800
```

Rank is the ladder name. Division is ignored, so `Master 1` and `Master` are the same step. A replay qualifies when its rank is at least that ladder or its average MMR is at least the floor. Notable still wins over high-skill: a Bronze pentakill is notable, not high-skill.

An invalid value, including a mode written as a number, fails closed. Nothing is recorded and nothing is sent.

## What gets recorded

Before launch, the replay file is classified. OBS starts recording when the loading screen or the match clock is visible, and only when the recording decision allows it. A replay id that is already on YouTube is not recorded again. The lookup is `Data\youtube-replay-ids.txt`, then the context receipt, then the channel's uploads index.
Before launch, the replay file is classified. OBS starts recording when the loading screen or the match clock is visible, and only when the recording decision allows it. A replay id that is already on YouTube is not recorded again. The spectator checks `Data\youtube-replay-ids.txt`, then the context receipt. Both are local files. The spectator never calls YouTube.

The uploader process keeps that catalog current. It adds a replay id when its insert succeeds, and its library pass (below) adds every replay id it finds on the channel: a title's id part, a `Replay ID:` line, or a Heroes Profile `replayID=` link. The earlier per-replay `search.list` check cost 100 units for every replay and had its own daily search limit.

| `RecordingMode` | What is recorded |
| --- | --- |
| `Disabled` | Nothing |
| `RequestedOnly` | A paid `RecordAndUpload` request |
| `Selected` | A request or a notable replay, when it is on patch, dated, and inside its age. When publication is `Curated` or `AllEligible`, ordinary and high-skill replays are recorded on those same terms. |
| `All` | Every spectated replay, including one that is too old to publish |

`All` still requires OBS `RecordingEnabled` (true in production). A viewer request that is spectate-only, with no `RecordAndUpload`, is not a publication.

## Why a replay is one class

The first matching class wins.

1. **Requested.** The Twitch reward is `RecordAndUpload`. A spectate-only reward is not requested.
2. **Notable.** The replay has a pentakill or a team wipe. Both come from one player. A pentakill is five or more killing blows by that player, each within 12 seconds of the previous blow. A team wipe is five unique enemy heroes killed by that same player inside that streak. The blow has to be one of that player's own hero units, and the victim has to be an enemy player's hero unit. Lost Vikings and Rexxar with Misha count as that one player. A summon, a structure, a suicide, or a wipe split across several players is not a clip.
3. **High-skill.** Rank or MMR meets the configured floor. With no floor, this class never matches.
4. **Ordinary.** Everything else.

Each class expires from the game's UTC time. Ordinary expires at `OrdinaryCandidateMaxAge` (72 hours in production). Notable and high-skill expire after 7 days. A request expires after 14 days. Those three ages are not in `appsettings.json` today. `RecordingMode` `All` still records an expired replay. Publication does not offer an expired replay.

A request may ignore `MinimumGameVersion` when `RequestsBypassPatchRequirement` is true, which is the default. Production does not set `RequireCurrentPatch`, so the patch floor is not a publication gate. The downloader still lists Storm League only (`HeroesProfileApi:GameTypes`).

The match also has to be a verified complete game, and the recording has to be finalized and correlated with that replay. A missing manifest, an unfinished recording, or a local clock fails closed.

## When the entry is written

After a recording that can be published, a second check decides whether `youtube-entry.json` is written next to the mp4.

| Mode | Entry |
| --- | --- |
| `Disabled` | Not written |
| `RequestedOnly` | Written for a request |
| `Curated` | Written for a request. Written for notable and high-skill while fewer than `MaxPublicPerDay` videos were observed public in the last 24 hours. An ordinary replay stays local. |
| `AllEligible` | Written for every class that is still inside its age, including when the day is already full. The uploader waits. |

The day count at this step is `Data`'s publication ledger of videos already observed public. The send step below uses its own reservation file, and that file is what spaces the uploads.

## When a video is sent

The uploader watches `Data\Contexts` for an mp4. It also retries pending files every 5 minutes. A dry-run process does not retry on that timer. It writes `youtube-dry-run.json` and does not call YouTube.

When `YouTube:PrivacyStatus` is public (production), a verified replay is sent only when every line below allows it. The first refusal wins.

1. Configuration is valid.
2. The replay is not already published, incomplete, or uncorrelated.
3. The mode allows this class. `Disabled` refuses everyone. `RequestedOnly` refuses anything except a request. `Curated` refuses ordinary. `AllEligible` allows every class through to the caps.
4. `videos.insert` calls today are under `MaxInsertsPerQuotaDay`. The quota day starts at midnight Pacific.
5. Public videos in the last 7 days are under `MaxPublicPerWeek`.
6. Public videos in the last 24 hours are under `MaxPublicPerDay`.
7. Reserved request room. With 6 and 2, a non-request stops once 4 videos are in the day. A request may use the last 2, and it stops once 2 requests are already in the day and the ordinary room is full. A request does not skip the day cap, the week cap, the quota, or the interval.
8. The previous public video is at least `MinimumPublicInterval` ago.
9. An ordinary replay's game time is inside `OrdinaryCandidateMaxAge`. A request, a notable replay, and a high-skill replay do not use this age at send time. They already expired by their own windows above.
10. The map was not already reserved inside `MapCooldown`. The check looks at every reserved slot in the window, not only the latest video. Comparison ignores case and surrounding spaces. One earlier upload of that map is enough. The reason is `map`.
11. The rank tier was not already reserved inside `RankCooldown`. Division is ignored, so Diamond 3 and Diamond 1 are the same tier. An unrecognized rank is compared as written. MMR is not part of this check. The reason is `rank`.
12. Heroes. A focus hero that was the focus of a reserved upload inside `FeaturedHeroCooldown` waits. Separately, when at least `MaxSharedHeroes` heroes from this replay (4 unless configured otherwise, and 0 turns this roster check off) appear anywhere in reserved uploads inside that window, the replay waits. One shared hero does not wait. The reason is `hero`.

A paid request skips the map, rank, and hero checks. Its slot still records the map, the rank, the focus hero, and the roster, so the next ordinary replay sees them. A refusal for `map`, `rank`, or `hero` stays pending. The uploader tries the next pending file on the same pass. Nothing sorts the queue by a score.

A private listing (`YouTube:PrivacyStatus` private, as in `appsettings.dev.json`) still stops at the quota. It does not apply the day cap, the week cap, the interval, or the map, rank, and hero checks. The listing stays private and the title carries `YouTube:TitlePrefix` (`[TEST]` in dev).

One replay id takes one publication slot, stored in `Data\publication-reservations.txt`. A retry of that same id does not take a second slot. An older line with only the time, the request flag, and the replay id still counts for the day, the week, and the interval. An ordinary replay that is too old is not retried. Any other refusal stays pending until a later pass.

The insert is private. When the desired privacy is public, the video also gets `publishAt`: now, or the previous public time plus `MinimumPublicInterval` when that is later. The entry is renamed to `youtube-entry-uploaded.json` when YouTube's insert response is already public. A response that is still private leaves the entry pending. This process does not later ask YouTube whether `publishAt` has fired. The reservation written at send time is what the next replay's day, week, and interval checks see.

A granted send that fails still keeps its slot. The retry is allowed through that slot and spends another `videos.insert` only if a new upload starts.

## How often

The spectator plays the next queued replay as soon as the previous session ends. YouTube does not follow that clock.

With the production settings a new replay is sent at most every 2 hours, at most 6 in any rolling 24 hours, and at most 30 in any rolling 7 days. The same map, the same rank tier, or a roster that shares 4 or more heroes with uploads from the last 8 hours waits, and the next different pending file is tried instead. Non-requests stop once 4 of those 6 are used. A paid request can take a remaining slot until 2 requests have already been sent in that day, and it is not held for map, rank, or heroes. The quota cap is 80 inserts per Pacific day, shared by full matches and clips.

## What the video contains

A full match title is one line of at most 100 characters. It does not name a pentakill, a team wipe, a date, or an MMR. Those events are not a title, because one streak does not describe the match.

- Ordinary: `Volskaya Foundry - Storm League - Diamond - 65389750`.
- A reward that names a player: `Illidan focus - Dragon Shire - Storm League - Diamond 3 - 65550001`. The title never names the Twitch viewer.
- A paid upload that does not name a player has the ordinary title. The requestor is credited in the description only.
- A new hero in the match: `Ft. Xal'atath - Volskaya Foundry - Storm League - Diamond - 65389750`. Ft. means featuring. One hero only. When the reward already leads with that hero, the title does not say her twice.

There is no parsed MVP hero, so a title does not say MVP. The heroes in the title come from the parsed replay: each player's character, or the attribute id when the character is blank.

A draft note is added before the replay id when the current hero-select roles are not one tank, one bruiser, one healer, and a ranged assassin. Those roles are Tank, Bruiser, Melee Assassin, Ranged Assassin, Healer, and Support. Johanna plus Chen is a tank and a bruiser, so that draft is left alone. The note is the whole match when both teams share it (`Cursed Hollow - Storm League - Diamond - No healer - 65550001`) and names the team when they differ (`Blue no tank, Red double healer`). A hero the catalog cannot match, or a hero with no current role, suppresses that team's note. A normal draft adds nothing.

`YouTube:Titles` in `appsettings.json` and `appsettings.prod.json` turns each form on or off: `DraftNotes`, `NamedPlayerTitles`, and `FeatureNewHeroes`. Each draft note has its own switch (`NoTankOrHealer`, `NoHealer`, `DoubleHealer`, `TripleHealer`, `DoubleBruiserWithoutTank`, `NoTank`, `DoubleTank`, `TripleBruiser`, `DoubleSupport`, `NoRangedAssassin`). The six role labels are in the same section. Turning a form off leaves the map, mode, rank, and replay id.

A hero is featured when its name is in `RecentHeroes`, or when the catalog `releaseDate` is within `RecentHeroDays` (60) of the match. `RecentHeroDays` of 0 or less uses the name list only. The newest release date wins. A name on the list is still featured when the local catalog does not have that hero yet. Xal'atath is on the list. The description adds `Featuring: Xal'atath` when the title does. Clips are unchanged.

The description starts with `Twitch: https://twitch.tv/saltysadism`, then `Full match.` when the recording completed, the replay id, the Heroes Profile match link, date, build, map, mode, rank, the featured hero when one was named, the draft note, `Featuring:` when a new hero is in the title, the pentakill or team wipe as a highlight, and the requestor when it was a paid upload. The Blue and Red roster lines name each player without the BattleTag number, because YouTube turns `#1234` into a hashtag. Average MMR is not written. The winner is not included. Category id is `20`. Tags come from the map, mode, rank, hero, and those events. The entry records `TemplateVersion` 5.

Videos uploaded before this template can still have BattleTag numbers in the roster lines, or a viewer's name in the title. `tools/youtube-fix-descriptions.cs` is a one-off script that rewrites those videos on YouTube. It only prints the changes unless it is given `--apply`, and it changes titles only with `--titles`.

Pentakill and team-wipe clips are separate full-frame cuts under the context `clips` folder, 12 seconds before the streak and 8 seconds after it on the match clock. `clips.json` in that context lists each cut with the hero and the killing blows (`second` and `victim`). The hero name is the English catalog name when the catalog has that hero. Each clip has its own `youtube-entry.json` and can be inserted as its own video. It uses the parent replay's class and the parent replay's one publication slot. Each insert still counts toward the quota. Clip titles look like `Li-Ming - pentakill - Alterac Pass - 65550001`.

After a successful upload, retention deletes the mp4 on the next sweep. The context folder itself lasts `VideoKeepDays` (3 in production).

## The library record

Every successful `videos.insert`, full match or clip, appends one line to `Data\youtube-library.jsonl`: the video id, the replay id, `full` or `clip`, the English map, the mode, the rank, the build, the privacy, and the upload time. The file sits in `Data`, not in a context folder, so retention never deletes it. A later line for the same video wins. A clip is recorded without a mode and without a build.

## The library pass

The uploader process owns every YouTube call. Besides uploads, it runs one library pass at most every `YouTube:LibraryInterval` (1 hour). The time of the last pass is kept in `Data\youtube-uploads-index.json`, so a restart does not run it again early. `heroesreplay youtube library --once` runs the same pass at once. Only one process runs it at a time (`Data\youtube-library.lock`). A pass does three things.

1. **List the channel.** `channels.list` once for the uploads playlist id (1 unit), then `playlistItems.list` with `snippet,status`, 50 videos and 1 unit per page, newest first. Every replay id found goes into `Data\youtube-replay-ids.txt`. A video in the record whose privacy changed (a scheduled upload that went public) gets a new line. Until a listing has once reached the last page, every page is read (7 units for 338 videos). After that a listing stops at the first page with no new video, so a pass usually costs 1 or 2 units.
2. **Backfill.** A channel video missing from the record is read from its title and description. The current template has `Map:`, `Mode:`, `Rank:`, and `Build:` lines. Older uploads (`Sky Temple - 65269475 - Platinum` with only `Game type:` and `Rank:` lines) have no build, and a few have a localized map name. Whatever is missing comes from Heroes Profile by replay id, with the rank looked up from player MMR only when a Storm League video has none. At most `YouTube:LibraryLookupsPerPass` (50) lookups run per pass. A video that resolves is recorded and never looked up again. One that does not stays in the index as unresolved and is tried again 6 hours later, then 12, 24, and so on up to every 7 days. An inserted clip is resolved the same way to get its build.
3. **File.** The record's resolved public videos, plus any `youtube-entry-uploaded.json` still under `Data\Contexts` that the record does not have, are planned with the playlist rules below. Each video id is filed once per playlist (`Data\youtube-playlists.json`). The channel's playlists are listed once per pass when a title is not cached (1 unit per 50), a missing playlist is created (50 units), and each video insert costs 50 units. Three failures in a row stop the filing for that pass. Nothing is removed from a playlist.

Two playlists are created when missing, both public:

- Map and mode. `Alterac Pass - Storm League - Diamond`. The league is Grandmaster, Master, Diamond, Platinum, Gold, Silver, or Bronze, without division. Unranked Storm League is `Alterac Pass - Storm League`. Quick Match, ARAM, and Unranked Draft use `Alterac Pass - Quick Match` and the same shape. Other modes are skipped. A clip has no mode, so it is not filed on a map playlist.
- Patch. The current patch line of `Spectate:MinimumGameVersion` uses `YouTube:SeasonName` when that is set, otherwise `Patch 2.57`. An older line uses `Patch 2.55 archive`. A record video waits for its build before it is filed. A context entry with no build uses `Unknown patch`. Only a public video is filed. Nothing is deleted when the patch rolls.

Upload OAuth is the `youtube.upload` scope. The library pass (listing, playlist create, and insert) uses a separate consent, the full `youtube` scope, stored for `{ChannelId}:library`. That consent also lists private and scheduled uploads. Channel id in the base file is `UCpf5rn5UlJTUZF9n98HXS5A`.

`YouTube:DryRun` true never calls YouTube or Heroes Profile. It writes `Data\youtube-library-dry-run.json` with the playlist inserts it would make from the record and the contexts, the unresolved videos it would look up, and the day's units.

Renaming or retitling published videos is not part of the pass. That stays in `tools/youtube-fix-descriptions.cs`.

## Quota units

The uploader process and `youtube library` share one count of quota units per Pacific quota day in `Data\youtube-quota-units.json`. Each change takes `youtube-quota-units.json.lock`, so two processes cannot both spend the same room.

| Call | Units |
| --- | --- |
| `videos.insert` | 1600 |
| `playlistItems.insert` | 50 |
| `playlists.insert` | 50 |
| Any list call | 1 |

Uploads go first. An insert is counted when it is sent, and the units never refuse an upload. `MaxInsertsPerQuotaDay` still caps uploads as before. The library pass reserves each call's units before it makes the call, and it stops when either limit is reached:

- `YouTube:LibraryUnitsPerDay` (3000) for the pass in one day.
- `YouTube:DailyQuotaUnits` (10000) minus `YouTube:QuotaReserveUnits` (1600) for the whole day, uploads included.

A quota response from YouTube, during the pass or an upload, pauses the pass until the next Pacific quota day. Uploads continue.

Backfilling the 338 videos on the channel today (4 already on the current template) costs about 7 units of listing, 334 Heroes Profile lookups (the older template has no build) spread over 7 passes, and two playlist inserts per public video: about 676 × 50 = 33,800 units, plus 50 for each playlist that does not exist yet (roughly one per map and league seen, plus one per patch line). At 3000 units a day that is about 12 to 13 days, longer on days when uploads leave less room under the ceiling.

## The score

The score is stored on the attempt and written to the log. It is not configurable, and nothing orders the upload queue by it.

| Class | Weight |
| --- | --- |
| Requested | 400000 |
| Notable | 300000 |
| High-skill | 200000 |
| Ordinary | 100000 |

Added to the weight, each capped at 20000: one point per minute newer than 14 days, 100 per pentakill, 60 per team wipe, 100 per ladder step above the high-skill rank floor, and one point per MMR point above the MMR floor. The replay id and the game time are tie-breaks only.
