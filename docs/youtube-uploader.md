# YouTube uploader

This is what the uploader does with a replay on the develop line. The next game on the Twitch stream is chosen by the Heroes Profile download queue and by Twitch requests. The score below does not pick that game, and it does not pick which pending file uploads next.

Production is `HEROES_REPLAY_ENV=prod` on `DESKTOP-8SJE72`. Configuration is `appsettings.json`, then `appsettings.secrets.json`, then `appsettings.prod.json`, then any `HEROES_REPLAY_` environment variable. A later layer wins for one key. `HEROES_REPLAY_ReplayMedia__MaxPublicPerDay` overrides `ReplayMedia:MaxPublicPerDay`.

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
  "FeaturedHeroCooldown": "08:00:00",
  "ReservedRequestSlotsPerDay": 2,
  "MaxInsertsPerQuotaDay": 80
}
```

`OrdinaryCandidateMaxAge` of `3.00:00:00` is 72 hours. `72:00:00` is 72 days, because that is how .NET reads a time span.

The base `appsettings.json` has the same eight caps and does not select a recording mode or a publication mode, so a process with no overlay records nothing and publishes nothing. Dev (`appsettings.dev.json`) uses the same modes as production, with `YouTube:DryRun` true, `PrivacyStatus` private, and a `[TEST]` title. Dev inherits the caps from the base file.

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

Before launch, the replay file is classified. OBS starts recording when the loading screen or the match clock is visible, and only when the recording decision allows it. A replay id that is already on YouTube is not recorded again. The lookup is `Data\youtube-replay-ids.txt`, then the context receipt, then a channel search for that replay id.

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
2. **Notable.** The replay has a pentakill or a team wipe. A pentakill is five or more kills by one hero inside 12 seconds. A team wipe is five unique enemy heroes killed by that same hero inside that window. A wipe split across several killers is not one wipe.
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

On `DESKTOP-8SJE72`, a verified replay is sent only when every line below allows it. The first refusal wins.

1. Configuration is valid.
2. The replay is not already published, incomplete, or uncorrelated.
3. The mode allows this class. `Disabled` refuses everyone. `RequestedOnly` refuses anything except a request. `Curated` refuses ordinary. `AllEligible` allows every class through to the caps.
4. `videos.insert` calls today are under `MaxInsertsPerQuotaDay`. The quota day starts at midnight Pacific.
5. Public videos in the last 7 days are under `MaxPublicPerWeek`.
6. Public videos in the last 24 hours are under `MaxPublicPerDay`.
7. Reserved request room. With 6 and 2, a non-request stops once 4 videos are in the day. A request may use the last 2, and it stops once 2 requests are already in the day and the ordinary room is full. A request does not skip the day cap, the week cap, the quota, or the interval.
8. The previous public video is at least `MinimumPublicInterval` ago.
9. An ordinary replay's game time is inside `OrdinaryCandidateMaxAge`. A request, a notable replay, and a high-skill replay do not use this age at send time. They already expired by their own windows above.

Any other machine, including `ASA-SERVER`, still stops at the quota. It does not apply the day cap, the week cap, or the interval. Its title is marked `[TEST]` unless `YouTube:TitlePrefix` is set, and the listing stays private.

`MapCooldown` and `FeaturedHeroCooldown` do not block a send. A repeat of the same map or featured hero inside the cooldown is still uploaded. The decision is marked cooldown. Nothing sorts the queue by that mark.

One replay id takes one publication slot, stored in `Data\publication-reservations.txt`. A retry of that same id does not take a second slot. An ordinary replay that is too old is not retried. Any other refusal stays pending until a later pass.

The insert is private. When the desired privacy is public, the video also gets `publishAt`: now, or the previous public time plus `MinimumPublicInterval` when that is later. The entry is renamed to `youtube-entry-uploaded.json` when YouTube's insert response is already public. A response that is still private leaves the entry pending. This process does not later ask YouTube whether `publishAt` has fired. The reservation written at send time is what the next replay's day, week, and interval checks see.

A granted send that fails still keeps its slot. The retry is allowed through that slot and spends another `videos.insert` only if a new upload starts.

## How often

The spectator plays the next queued replay as soon as the previous session ends. YouTube does not follow that clock.

On the production host a new replay is sent at most every 2 hours, at most 6 in any rolling 24 hours, and at most 30 in any rolling 7 days. Non-requests stop once 4 of those 6 are used. A paid request can take a remaining slot until 2 requests have already been sent in that day. The quota cap is 80 inserts per Pacific day, shared by full matches and clips.

## What the video contains

A full match title is built from the pieces that fit in 100 characters, in this order: a prefix, hero on map, mode, rank, average MMR, UTC date, replay id. The prefix is `Full match`, plus `Requested` and `pentakill` or `team wipe` when those are true. Example shape: `Full match: Pentakill - Li-Ming on Alterac Pass - Storm League - Diamond - 2800 MMR - 2026-10-01 - 65550001`.

The description starts with `Twitch: http://twitch.tv/saltysadism`, then `Full match.`, the replay id, the Heroes Profile match link, date, build, map, mode, rank, average MMR, featured hero, the pentakill or team wipe, and the requestor when it was a paid upload. The winner is not included. Category id is `20`. Tags come from the map, mode, rank, hero, and those events.

Pentakill and team-wipe clips are separate full-frame cuts under the context `clips` folder, 12 seconds before the streak and 8 seconds after it on the match clock. Each clip has its own `youtube-entry.json` and can be inserted as its own video. It uses the parent replay's class and the parent replay's one publication slot. Each insert still counts toward the quota. Clip titles look like `Li-Ming - pentakill - Alterac Pass - 65550001`.

After a successful upload, retention deletes the mp4 on the next sweep. The context folder itself lasts `VideoKeepDays` (3 in production).

## Playlists

Playlist filing runs at uploader startup and after each live drain, and on `heroesreplay youtube library`. It reads `youtube-entry-uploaded.json` files that are still under `Data\Contexts`. It does not remove a video from a playlist.

Two playlists are created when missing, both public:

- Map and mode. `Alterac Pass - Storm League - Diamond`. The league is Grandmaster, Master, Diamond, Platinum, Gold, Silver, or Bronze, without division. Unranked Storm League is `Alterac Pass - Storm League`. Quick Match, ARAM, and Unranked Draft use `Alterac Pass - Quick Match` and the same shape. Other modes are skipped.
- Patch. The current patch line of `Spectate:MinimumGameVersion` uses `YouTube:SeasonName` when that is set, otherwise `Patch 2.57`. An older line uses `Patch 2.55 archive`. A video with no build uses `Unknown patch`. Only a public listing is filed. Nothing is deleted when the patch rolls.

The cache is `Data\youtube-playlists.json`. Upload OAuth is the `youtube.upload` scope. Playlist create and insert use a separate consent, the full `youtube` scope, stored for `{ChannelId}:library`. Channel id in the base file is `UCpf5rn5UlJTUZF9n98HXS5A`. A clip entry has a map and no mode, so it is not filed on a map playlist. With no build it can land on `Unknown patch` once its receipt exists.

`YouTube:DryRun` true writes `youtube-library-dry-run.json` and does not call YouTube.

## The score

The score is stored on the attempt and written to the log. It is not configurable, and nothing orders the upload queue by it.

| Class | Weight |
| --- | --- |
| Requested | 400000 |
| Notable | 300000 |
| High-skill | 200000 |
| Ordinary | 100000 |

Added to the weight, each capped at 20000: one point per minute newer than 14 days, 100 per pentakill, 60 per team wipe, 100 per ladder step above the high-skill rank floor, and one point per MMR point above the MMR floor. The replay id and the game time are tie-breaks only.
