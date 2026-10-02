# YouTube uploader

This is what the uploader does with a replay on the develop line. The next game on the Twitch stream is chosen by the Heroes Profile download queue and by Twitch requests. The score below does not pick that game, and it does not pick which pending file uploads next.

Production is `HEROES_REPLAY_ENV=prod` (`DESKTOP-8SJEK72`). The environment decides, not the machine name. Configuration is `appsettings.json`, then `appsettings.secrets.json`, then `appsettings.prod.json`, then any `HEROES_REPLAY_` environment variable. A later layer wins for one key. `HEROES_REPLAY_ReplayMedia__MaxPublicPerDay` overrides `ReplayMedia:MaxPublicPerDay`.

## Production settings

`appsettings.prod.json` is the production policy. It records every spectated replay and uploads every eligible recording as soon as the YouTube quota allows. The caps below decide when each video goes public, not whether it is uploaded.

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
  "MaxPublishAhead": "7.00:00:00",
  "OrdinaryCandidateMaxAge": "3.00:00:00",
  "MapCooldown": "08:00:00",
  "RankCooldown": "08:00:00",
  "FeaturedHeroCooldown": "08:00:00",
  "MaxSharedHeroes": 4,
  "ReservedRequestSlotsPerDay": 2,
  "MaxInsertsPerQuotaDay": 80
}
```

`OrdinaryCandidateMaxAge` of `3.00:00:00` is 72 hours. `72:00:00` is 72 days, because that is how .NET reads a time span. `MaxPublishAhead` of `7.00:00:00` is 7 days. That keeps every scheduled video among the channel's newest 50 uploads when it goes public (at most 6 uploads a day for 7 days), so the library pass sees it on the first listing page. Raise it only together with that page in mind.

The base `appsettings.json` has the same eleven algorithm keys and does not select a recording mode or a publication mode, so a process with no overlay records nothing and publishes nothing. Dev (`appsettings.dev.json`) uses the same modes as production, with `YouTube:DryRun` true, `PrivacyStatus` private, and a `[TEST]` title. Dev inherits the caps from the base file.

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
| `AllEligible` | Written for every class that is still inside its age, including when the day is already full. The uploader schedules it for a later day. |

The day count at this step is `Data`'s publication ledger of videos already observed public. The send step below uses its own reservation file, and that file is what spaces the publish times.

## When a video is sent

The uploader watches `Data\Contexts` for an mp4. It also retries pending files every 5 minutes. A pass sends a viewer request first (`Requested` in the entry), then the oldest recording first. A dry-run process does not retry on that timer. It writes `youtube-dry-run.json` and does not call YouTube.

A recording stays on disk only for the YouTube quota or for the media rules. These lines decide whether it is sent now. The first refusal wins.

1. Configuration is valid.
2. The replay is not already published, incomplete, or uncorrelated.
3. The mode allows this class. `Disabled` refuses everyone. `RequestedOnly` refuses anything except a request. `Curated` refuses ordinary. `AllEligible` allows every class.
4. The day's quota has room. `Data\youtube-quota-units.json` has at least 1600 units (one `videos.insert`) left under `YouTube:DailyQuotaUnits`, library spend included, and no quota response from an upload paused uploads. `videos.insert` calls today are under `MaxInsertsPerQuotaDay`. The quota day starts at midnight Pacific.
5. When `YouTube:PrivacyStatus` is public, an ordinary replay's game time is inside `OrdinaryCandidateMaxAge`. A request, a notable replay, and a high-skill replay do not use this age at send time. They already expired by their own windows above. An ordinary replay that is too old is not retried, and its recording is deleted.
6. When `YouTube:PrivacyStatus` is public, a publish time inside `MaxPublishAhead` (7 days) keeps every rule below. With none, the reason is `horizon` and the recording waits for a later pass.

The rules below no longer hold a recording back. They choose its publish time: the earliest time from now that keeps all of them. Each rule looks both ways, at videos already public and at slots already scheduled, so a later replay can take a free time between two earlier ones.

- Week. No rolling 7 days holds more than `MaxPublicPerWeek` videos.
- Day. No rolling 24 hours holds more than `MaxPublicPerDay` videos.
- Reserved request room. With 6 and 2, a non-request may not join a rolling 24 hours that already holds 4 videos, requests included. A request may use the last 2, unless 2 requests already sit in that 24 hours and the ordinary room is full. A request does not skip the day cap, the week cap, or the interval.
- Interval. Every other publish time is at least `MinimumPublicInterval` away.
- Map. No slot within `MapCooldown` has the same map. Comparison ignores case and surrounding spaces.
- Rank. No slot within `RankCooldown` has the same tier. Division is ignored, so Diamond 3 and Diamond 1 are the same tier. An unrecognized rank is compared as written. MMR is not part of this check.
- Heroes. No slot within `FeaturedHeroCooldown` has the same focus hero. Separately, fewer than `MaxSharedHeroes` heroes from this replay (4 unless configured otherwise, and 0 turns this roster check off) appear in slots within that window. One shared hero is fine.

A paid request skips the map, rank, and hero rules and may use the reserved room, so it gets the earliest time. Its slot still records the map, the rank, the focus hero, and the roster, so later ordinary replays plan around them. Nothing sorts the queue by a score.

The log names the rule that pushed the time later (`interval`, `day`, `reserved`, `week`, `map`, `rank`, or `hero`), or `ready` when the time is now.

A private listing (`YouTube:PrivacyStatus` private, as in `appsettings.dev.json`) still stops at the quota. It has no publish time and no other rule. The listing stays private and the title carries `YouTube:TitlePrefix` (`[TEST]` in dev).

One replay id takes one publication slot, stored in `Data\publication-reservations.txt`. The slot's time is the publish time, which can be days after the upload. A retry of that same id does not take a second slot and uses the same time. A clip shares its replay's slot, so it publishes with the full match. An older line with only the time, the request flag, and the replay id still counts for the day, the week, and the interval.

The insert is private with `publishAt` set to the slot's time. A time that has already passed by the end of the upload publishes the video right away. The entry is renamed to `youtube-entry-uploaded.json` when YouTube's insert response is already public. A response that is still private keeps `youtube-entry.json` with the video id and the publish time. This process does not later ask YouTube whether `publishAt` has fired. The library pass below sees it go public.

A granted send that fails still keeps its slot. The retry is allowed through that slot and spends another `videos.insert` only if a new upload starts. A quota response from YouTube during an upload pauses new uploads and the library pass until the next Pacific quota day.

A dry run plans in `Data\publication-reservations-dry-run.txt`, so its times never take a live slot. `youtube-dry-run.json` records the plan: the insert privacy, the desired privacy, `PublishAtUtc`, the schedule result and its reason (for example `granted` and `interval`, or `refused` and `horizon`), `SelfDeclaredMadeForKids`, and the category. A dry run never deletes a recording.

## How often

The spectator plays the next queued replay as soon as the previous session ends. YouTube does not follow that clock.

A recording is uploaded as soon as the quota allows. With 10000 units a day and 1600 per insert, that is at most 6 uploads per Pacific day, shared by full matches and clips, and fewer when the library pass already spent units. `MaxInsertsPerQuotaDay` (80) is a second cap above that.

With the production settings the videos go public at most every 2 hours, at most 6 in any rolling 24 hours (a non-request only while fewer than 4 are in it), and at most 30 in any rolling 7 days. The same map, the same rank tier, or a roster that shares 4 or more heroes with a video 8 hours either side moves the time later.

Eight ordinary games that end at 10:00, 10:30, and every half hour to 13:30 UTC, on eight maps and four rank tiers, on a quota day with nothing spent yet:

| Game | Uploaded | Publishes | Why not earlier |
| --- | --- | --- | --- |
| 1 | 10:00 | 10:00 | |
| 2 | 10:30 | 12:00 | interval |
| 3 | 11:00 | 14:00 | interval |
| 4 | 11:30 | 16:00 | interval |
| 5 | 12:00 | next day 10:00 | reserved: 4 non-requests are in every 24 hours until game 1 leaves |
| 6 | 12:30 | next day 12:00 | reserved |
| 7 | after the quota day turns (07:00 or 08:00 UTC) | next day 14:00 | quota held the upload, then reserved |
| 8 | after the quota day turns | next day 16:00 | quota held the upload, then reserved |

A paid request that ends at 14:00 that day publishes at 18:00, the first time 2 hours from every other video. A ninth ordinary game publishes on the third day at 10:00. The recordings of games 1 to 6 go on the retention sweep that follows each upload. Only games 7 and 8 wait on disk, for the quota.

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

Every `videos.insert`, full match or clip, is built by `UploadBody` with the `snippet,status` parts:

- Audience: `status.selfDeclaredMadeForKids` is false, which is Studio's "No, it's not made for kids".
- Age: nothing sets `contentDetails.contentRating.ytRating`, so no video is restricted to viewers over 18.
- `status.privacyStatus` private, with `status.publishAt` set to the slot's time when the listing is public.
- `status.containsSyntheticMedia` false (Studio's altered content question), `status.embeddable` true, and `status.license` `youtube`.
- `snippet.categoryId` from the entry, or `20` (Gaming) when the entry has none, as an older clip entry can.
- `snippet.defaultLanguage` and `snippet.defaultAudioLanguage` `en`.
- The title, the description lines joined by line breaks, and the tags.

Videos uploaded before this template can still have BattleTag numbers in the roster lines, or a viewer's name in the title. `tools/youtube-fix-descriptions.cs` is a one-off script that rewrites those videos on YouTube. It only prints the changes unless it is given `--apply`, and it changes titles only with `--titles`.

Pentakill and team-wipe clips are separate full-frame cuts under the context `clips` folder, 12 seconds before the streak and 8 seconds after it on the match clock. `clips.json` in that context lists each cut with the hero and the killing blows (`second` and `victim`). The hero name is the English catalog name when the catalog has that hero. Each clip has its own `youtube-entry.json` and can be inserted as its own video. It uses the parent replay's class and the parent replay's one publication slot. Each insert still counts toward the quota. Clip titles look like `Li-Ming - pentakill - Alterac Pass - 65550001`.

After a successful upload, retention deletes the mp4 on the next sweep, which the uploader runs right after the insert. The video may still be waiting for its `publishAt`. The context folder itself lasts `VideoKeepDays` (3 in production), and an entry that waits for `publishAt` no longer keeps it.

## The library record

Every successful `videos.insert`, full match or clip, appends one line to `Data\youtube-library.jsonl`: the video id, the replay id, `full` or `clip`, the English map, the mode, the rank, the build, the privacy, and the upload time. A full match also keeps `Draft`, the description's `Draft:` note (`Blue no tank, Red double healer`), and `FocusHero`, the description's `Featured:` hero. Only a viewer request that named a player (`{replayId},{slot}` on the ReplayId reward, or `spectate file --player`) writes `Featured:`. Both keys are left out of the line when the video has neither. The file sits in `Data`, not in a context folder, so retention never deletes it. A later line for the same video wins. A clip is recorded without a mode, a build, a draft note, or a named player.

## The library pass

The uploader process owns every YouTube call. Besides uploads, it runs one library pass at most every `YouTube:LibraryInterval` (1 hour). The time of the last pass is kept in `Data\youtube-uploads-index.json`, so a restart does not run it again early. `heroesreplay youtube library --once` runs the same pass at once. Only one process runs it at a time (`Data\youtube-library.lock`). A pass does three things.

1. **List the channel.** `channels.list` once for the uploads playlist id (1 unit), then `playlistItems.list` with `snippet,status`, 50 videos and 1 unit per page, newest first. Every replay id found goes into `Data\youtube-replay-ids.txt`. A video in the record whose privacy changed (a scheduled upload that went public) gets a new line. Until a listing has once reached the last page, every page is read (7 units for 338 videos). After that a listing stops at the first page with no new video, so a pass usually costs 1 or 2 units. When the pass learns to read a new fact (`YouTubeVideoFacts.Version`, now 2 for the draft note and the named player), the next listing reads every page once more, and a video already in the record gets a new line with the draft note or named player its description shows. Nothing the record already has is replaced.
2. **Backfill.** A channel video missing from the record is read from its title and description. The current template has `Map:`, `Mode:`, `Rank:`, and `Build:` lines, plus `Draft:` and `Featured:` when they apply. Older uploads (`Sky Temple - 65269475 - Platinum` with only `Game type:` and `Rank:` lines) have no build, and a few have a localized map name. Whatever is missing comes from Heroes Profile by replay id, with the rank looked up from player MMR only when a Storm League video has none. At most `YouTube:LibraryLookupsPerPass` (50) lookups run per pass. A video that resolves is recorded and never looked up again. One that does not stays in the index as unresolved and is tried again 6 hours later, then 12, 24, and so on up to every 7 days. An inserted clip is resolved the same way to get its build.
3. **File.** The record's resolved public videos, plus any public `youtube-entry-uploaded.json` still under `Data\Contexts` that the record does not have, are planned with the playlist groups below. The newest upload is planned first, so a new video is filed before the backlog of older ones. Each video id is filed once per playlist (`Data\youtube-playlists.json`). The channel's playlists are listed once per pass when a title is not cached (1 unit per 50), a missing playlist is created (50 units), and each video insert costs 50 units. Three failures in a row stop the filing for that pass. Nothing is removed from a playlist.

A scheduled upload is filed once it is public, not at insert. The insert appends it to the record as private. The first pass after its `publishAt` lists it public, appends a new line, and files it, so it lands on its playlists within `LibraryInterval` (1 hour) of going public. That pass reads the first page of uploads every time and an older page only while the page before it held a new video, so it sees the change while the video is among the newest 50 uploads. `MaxPublishAhead` of 7 days keeps it there. Filing at insert is not possible with the current code: the upload consent is the `youtube.upload` scope, which cannot call `playlistItems.insert`, and the planner files only public videos so a playlist never lists a video viewers cannot open yet.

Upload OAuth is the `youtube.upload` scope. The library pass (listing, playlist create, and insert) uses a separate consent, the full `youtube` scope, stored for `{ChannelId}:library`. That consent also lists private and scheduled uploads. Channel id in the base file is `UCpf5rn5UlJTUZF9n98HXS5A`.

`YouTube:DryRun` true never calls YouTube or Heroes Profile. It writes `Data\youtube-library-dry-run.json` with the playlist inserts it would make from the record and the contexts (`Items`), the video count per playlist (`Playlists`), their units (`InsertUnits`, 50 each, playlist creates not counted), the unresolved videos it would look up, and the day's units.

Renaming or retitling published videos is not part of the pass. That stays in `tools/youtube-fix-descriptions.cs`.

## Playlists

`YouTube:Playlists` lists the groups, one switch each. A playlist is created public the first time a video needs it. Its title is its key: the same facts always give the same title, at most 150 characters (YouTube's limit). A video goes into each playlist at most once, even when two groups give the same title.

```json
"Playlists": {
  "Map": true,
  "Mode": true,
  "Rank": true,
  "Draft": true,
  "ViewerReview": true,
  "Patch": true,
  "MapMode": false
}
```

| Group | Title | Filed when |
| --- | --- | --- |
| `Map` | `Alterac Pass` | A full match with a filed mode. Every mode shares one playlist per map. The map is the English catalog name. |
| `Mode` | `Storm League`, `Quick Match`, `ARAM`, `Unranked Draft` | A full match in one of those modes. Other modes (brawls, custom) are not filed in any group except the patch. |
| `Rank` | `Storm League - Diamond` | A Storm League game with a known league: Grandmaster, Master, Diamond, Platinum, Gold, Silver, or Bronze, without division. Unranked Storm League, Quick Match, and ARAM have no rank playlist. |
| `Draft` | `Unusual drafts - Double healer` | The description has a `Draft:` note. One playlist per note, without the team: `Blue no tank, Red double healer` goes into `Unusual drafts - No tank` and `Unusual drafts - Double healer`. |
| `ViewerReview` | `Viewer requested reviews` | The description has a `Featured:` hero, which only a request that named a player writes. A paid upload that named no player is not a review. |
| `Patch` | `Patch 2.57`, `YouTube:SeasonName`, `Patch 2.55 archive`, `Unknown patch` | Every public video, clips included. The current line of `Spectate:MinimumGameVersion` uses `SeasonName` when that is set. An older line is an archive. A record video waits for its build. A context entry with no build uses `Unknown patch`. Nothing is deleted when the patch rolls. |
| `MapMode` | `Alterac Pass - Storm League - Diamond`, `Alterac Pass - Quick Match` | Off by default. The earlier combined playlist. |

A clip goes into the patch playlist only. Every other group needs a full match in a filed mode. Only a public video is filed.

The draft notes are the title's notes (see "What the video contains"): no tank or healer, no healer, double or triple healer, double bruiser without a tank, no tank, double tank, triple bruiser, double support, and no ranged assassin, with counts above three written as a number (`4 healers`). That is about ten playlists in practice. One playlist per note, rather than a single `Unusual drafts` list, lets a viewer open "every double healer game". It costs the same insert for a video with one note. Only a match where the two teams have different notes costs a second insert.

`MapMode` is off because it is one playlist per map and tier: 15 maps times 8 Storm League shapes, plus Quick Match and ARAM, is well over 100 playlists of a few videos each, and every one of them repeats what the map and rank playlists already show. Turning it off stops new inserts. The combined playlists already on the channel keep their videos and are not filled any further. Delete them in YouTube Studio if they are not wanted, or set `MapMode` true to keep them growing.

Old videos. The first template (`Sky Temple - 65269475 - Platinum`, with a `Game type:` line) has no `Draft:` or `Featured:` line, and Heroes Profile does not know about a draft note or a Twitch request. Those videos go into the map, mode, rank, and patch playlists only. A current-template video gets the draft note only when `YouTube:Titles:DraftNotes` was on at upload.

Cost per video. Each group that applies is one `playlistItems.insert`, 50 units:

| Video | Inserts with the defaults | Units |
| --- | --- | --- |
| Ranked Storm League (map, mode, rank, patch) | 4 | 200 |
| Unranked Storm League, Quick Match, or ARAM (map, mode, patch) | 3 | 150 |
| Plus an unusual draft | +1 per note (usually 1, at most 2) | +50 to +100 |
| Plus a viewer review | +1 | +50 |
| Clip (patch) | 1 | 50 |

The earlier rules cost 2 inserts (100 units) per full match.

## Quota units

The uploader process and `youtube library` share one count of quota units per Pacific quota day in `Data\youtube-quota-units.json`. Each change takes `youtube-quota-units.json.lock`, so two processes cannot both spend the same room.

| Call | Units |
| --- | --- |
| `videos.insert` | 1600 |
| `playlistItems.insert` | 50 |
| `playlists.insert` | 50 |
| Any list call | 1 |

Uploads go first. An insert is counted when it is sent. A new upload starts only while the day has room for one more insert (1600 units) under `YouTube:DailyQuotaUnits`, library spend included. Otherwise the recording stays on disk until the next Pacific quota day. `MaxInsertsPerQuotaDay` still caps uploads as before. The library pass reserves each call's units before it makes the call, and it stops when either limit is reached:

- `YouTube:LibraryUnitsPerDay` (3000) for the pass in one day.
- `YouTube:DailyQuotaUnits` (10000) minus `YouTube:QuotaReserveUnits` (1600) for the whole day, uploads included.

A quota response from YouTube during the pass pauses the pass until the next Pacific quota day, and uploads continue. A quota response during an upload pauses both the pass and new uploads until then (`UploadsPausedUntil`). That upload itself is ambiguous and waits for an operator retry, like any other upload that stopped mid-send.

Backfilling the 338 videos on the channel today (4 already on the current template) costs about 7 units of listing (and 7 more once, for the full re-listing that reads the draft note and named player), 334 Heroes Profile lookups (the older template has no build) spread over 7 passes, and the playlist inserts. Each group that is on adds one insert per video: 338 × 50 = 16,900 units, about 6 days at 3000 units a day. With the defaults a ranked Storm League video is 4 inserts (map, mode, rank, patch), so the backlog is about 1,352 × 50 = 67,600 units, plus 50 for each playlist that does not exist yet (about 30: one per map, mode, league, patch line, draft note, and the review playlist). At 3000 units a day that is about 23 days. New uploads share that room (200 units each, so about 1,200 on a day with 6 uploads), which makes it closer to 5 to 6 weeks, and longer on days when uploads leave less room under the ceiling. An insert an earlier pass already made (the patch playlist) is not made again. To shorten the backlog, turn `Mode` off first: almost every video is Storm League, so the `Storm League` playlist is close to the whole channel.

## The score

The score is stored on the attempt and written to the log. It is not configurable, and nothing orders the upload queue by it.

| Class | Weight |
| --- | --- |
| Requested | 400000 |
| Notable | 300000 |
| High-skill | 200000 |
| Ordinary | 100000 |

Added to the weight, each capped at 20000: one point per minute newer than 14 days, 100 per pentakill, 60 per team wipe, 100 per ladder step above the high-skill rank floor, and one point per MMR point above the MMR floor. The replay id and the game time are tie-breaks only.
