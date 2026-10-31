# YouTube uploader

This is what the uploader does with a replay on the develop line. The next game on the Twitch stream is chosen by the Heroes Profile download queue and by Twitch requests. The score below does not pick that game, and it does not pick which pending file uploads next.

Production is `HEROES_REPLAY_ENV=prod` (`DESKTOP-8SJEK72`). The environment decides, not the machine name. Configuration is `appsettings.json`, then `appsettings.secrets.json`, then `appsettings.prod.json`, then any `HEROES_REPLAY_` environment variable. A later layer wins for one key. `HEROES_REPLAY_ReplayMedia__MaxPublicPerDay` overrides `ReplayMedia:MaxPublicPerDay`.

## Production settings

`appsettings.prod.json` is the production policy. It records a spectated replay only while the pipeline can still publish it (the recording cap, below), and uploads every eligible recording as soon as the upload bucket allows. The caps below decide when each video goes public; the recording cap keeps recordings in line with them.

```json
"YouTube": {
  "Enabled": true,
  "DryRun": false,
  "PrivacyStatus": "public",
  "DailyUploadCalls": 100,
  "UploadCallReserve": 5,
  "DailyQuotaUnits": 10000,
  "QuotaReserveUnits": 500,
  "LibraryUnitsPerDay": 9500,
  "Playlists": { "Mode": false }
},
"Disk": {
  "WarnWhenPendingBytesAtLeast": 107374182400,
  "StopWhenPendingBytesAtLeast": 128849018880
},
"ReplayMedia": {
  "Version": "1",
  "RecordingMode": "Selected",
  "PublicationMode": "AllEligible",
  "MaxPublicPerDay": 72,
  "MaxPublicPerWeek": 504,
  "MinimumPublicInterval": "00:20:00",
  "MaxPublishAhead": "3.00:00:00",
  "OrdinaryCandidateMaxAge": "3.00:00:00",
  "MapCooldown": "02:00:00",
  "RankCooldown": "01:00:00",
  "FeaturedHeroCooldown": "04:00:00",
  "MaxSharedHeroes": 4,
  "ReservedRequestSlotsPerDay": 2,
  "MaxInsertsPerQuotaDay": 95,
  "MaxWaitingRecordings": 6,
  "CapRecordingToPublication": true
}
```

### Why these numbers (#507)

The pace follows what the stream can play, and stays inside what YouTube gives the project. One stream plays one game at a time. A recording, from the loading screen to the MVP screen, averages 20.6 minutes (median 19.8, 90% under 26.6, on the channel's 200 newest videos on 2026-10-22). So a stream that runs 24/7 plays at most about 70 games a day with no gap between replays, and about 55 to 60 with the report and the next load between them. Nothing past that can be uploaded, whatever the quota allows:

- **Uploads.** The Video Uploads bucket is 100 calls a day and 5 are held back, so `MaxInsertsPerQuotaDay` is 95: the app's cap no longer stops uploads before YouTube does. The stream fills about 55 to 60 of them. It cannot fill the bucket.
- **Publication.** One video every 20 minutes is at most 72 a day, 70 of them not requests: just above the 70 the stream could play with no gaps, so publication cannot hold the stream back. The map (2 h), rank (1 h) and hero (4 h) cooldowns are short enough that the channel's mix of 12 maps and 6 tiers does not push a day of games back. A video still never goes further ahead than `MaxPublishAhead` (3 days). The recording cap stops recording once the window is full, so the schedule cannot grow into a backlog of old games.
- **Playlists.** Without the `Storm League` mode playlist (every video is Storm League, so it repeated the channel's uploads), a video costs about 130 units of playlist inserts (map and rank, plus a draft note on 43% and a focus hero on 17% of videos). 72 a day is about 9,400 units, inside `LibraryUnitsPerDay` 9,500. The pool's reserve is 500.
- **Disk.** `MaxWaitingRecordings` 6 lets about 15 GB wait for an upload. When uploads stall, new ordinary recordings stop after a few games instead of piling up a day of uploads.

Before #507 the stream PC published 22 videos a day an hour apart (24 a day, 168 a week, 8 h map and hero cooldowns, 30 inserts). Google's own count for Oct 1 to 22 showed at most 39 of 100 upload calls a day, no quota response, and the pool at the 8,000 the library pass was allowed. The 3-day window stayed full, the recording cap kept 30 recordings (70.8 GiB) waiting and skipped about half the games, and the channel's 200 newest videos went public a median of 88 to 137 hours after their game. A simulation of `PublicationSchedule.Plan` and `RecordingCap.Decide` over those videos' maps, ranks and rosters reproduces that (21.6 a day, 30 waiting). At these settings it records and publishes 60 games a day with nothing waiting and a median lag of under 5 hours, at most 15. With 24 hours of no uploads, 6 recordings wait and the rest of that day's ordinary games are not recorded.

`OrdinaryCandidateMaxAge` of `3.00:00:00` is 72 hours. `72:00:00` is 72 days, because that is how .NET reads a time span. `MaxPublishAhead` of `3.00:00:00` is 3 days (the code default stays 14). The library pass checks a scheduled video by id once its publish time has passed (#154), so how far back it sits in the uploads listing does not matter.

The uploader logs the effective quota settings once when it starts (`YouTube quota: uploads 100 videos.insert calls a day ...`), and one warning for each way the settings cannot all hold: `MaxInsertsPerQuotaDay` above the usable upload calls, `LibraryUnitsPerDay` above the pool, no pool left after the reserve, a library budget too small to file a day of public videos into playlists, request room that takes the whole day, or a week cap below the day cap.

The base `appsettings.json` has the same eleven algorithm keys and does not select a recording mode or a publication mode, so a process with no overlay records nothing and publishes nothing. Dev (`appsettings.dev.json`) uses the same modes as production, with `YouTube:DryRun` true, `PrivacyStatus` private, and a `[TEST]` title. Dev inherits the caps from the base file.

`PublicationMode` `AllEligible` is the live channel: a paced archive of the Storm League games the stream already plays. `Curated` is the other content policy. It sends a paid request, a pentakill, or a high-skill game, and it does not send an ordinary game. High skill does nothing until `MinimumHighSkillRank` or `MinimumHighSkillMmr` is set. Production does not set either, so no replay is high-skill.

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

Before launch, the replay file is classified. OBS starts recording when the loading screen or the match clock is visible, and only when the recording decision allows it. A replay id that is already on YouTube is not recorded again. The spectator checks `Data\youtube-replay-ids.txt`, then the context receipt. Both are local files. The spectator never calls YouTube.

The uploader process keeps that catalog current. It adds a replay id when its insert succeeds, and its library pass (below) adds every replay id it finds on the channel: a title's id part, a `Replay ID:` line, or a Heroes Profile `replayID=` link. The earlier per-replay `search.list` check cost 100 units for every replay and had its own daily search limit.

| `RecordingMode` | What is recorded |
| --- | --- |
| `Disabled` | Nothing |
| `RequestedOnly` | A paid `RecordAndUpload` request |
| `Selected` | A request or a notable replay, when it is on patch, dated, and inside its age. When publication is `Curated` or `AllEligible`, ordinary and high-skill replays are recorded on those same terms. |
| `All` | Every spectated replay, including one that is too old to publish |

Production and dev use `Selected` (#204): a replay that no publication window can still reach is spectated without an mp4. The spectator writes `youtube-entry.json` when the session ends, so a recording with no entry that OBS has not written for `Retention:UnpublishedGrace` (1 hour) can never be published, and the next retention sweep deletes it with a warning that names the reason (`reason: no-entry`).

### The recording cap

The mode says what is worth recording. The recording cap (`ReplayMedia:CapRecordingToPublication`, on by default, #250, #370) says whether the pipeline can still take one more: whether this replay's recording would be uploaded before the replay stops being a candidate. Before launch it counts two things:

- **Waiting for upload.** Recordings on disk that still wait for their `videos.insert` and need a publication slot. A recording whose send failed after it reserved a slot is counted with the slots instead, so it is not counted twice.
- **Scheduled.** The slots in `Data\publication-reservations.txt` whose publish time is still ahead. These are mostly uploads that only wait for their `publishAt`. They need no disk and no upload. They count only because they fill the publication window.

The publication window holds what the pacing rules publish within `MaxPublishAhead` (at least one day): `min(MaxPublicPerDay - ReservedRequestSlotsPerDay, MaxPublicPerWeek / 7)` a day, and no more than a day of upload calls. While the window has a free slot, a new recording is sent at once. When it is full, a recording waits on disk until the window slides far enough to reach a free time. That happens at the same pace, behind every recording already waiting. The uploader deletes an ordinary recording that is still waiting once its game is older than `OrdinaryCandidateMaxAge`. So the wait has to end, with a day to spare (`RecordingCap.SendSlack`), before the replay's candidate expiry: its game time plus `OrdinaryCandidateMaxAge` for an ordinary replay, or plus 7 days for a notable or high-skill one.

The first matching line decides, and the spectator logs it as `recording cap ... (reason)`, with how many wait for upload, how many are scheduled, and the expected wait:

| Reason | Records | When |
| --- | --- | --- |
| `cap-requested` | yes | A paid `RecordAndUpload` request. Always recorded. |
| `cap-off` | yes | `CapRecordingToPublication` is false. |
| `cap-not-live` | yes | YouTube is disabled or a dry run (dev): nothing piles up for YouTube. |
| `cap-upload-backlog` | no | The recordings waiting for upload reach the waiting limit: `MaxWaitingRecordings` (6 in production, #507), and never more than a day of upload calls, `min(DailyUploadCalls - UploadCallReserve, MaxInsertsPerQuotaDay)`. Zero or unset is a day of upload calls. |
| `cap-room` | yes | A private listing, or the window has a free slot: waiting plus scheduled is under what it holds. |
| `cap-queued` | yes | The window is full, and the slots that must open first (waiting plus scheduled plus this one, less what the window holds), at the window's daily pace, still leave a day before the replay expires. |
| `cap-publication-full` | no | The window is full, and this replay would wait until less than a day before it expires, or its game time is unknown. |

With the production settings since #507 the window holds 70 a day over 3 days, so 210 slots, and the stream's 60 or so games a day do not fill it: a recording waits on disk only for the uploads ahead of it, and at most `MaxWaitingRecordings` (6) wait before ordinary replays stop being recorded. From 2026-10-13 to #507 the window held 22 a day, so 66 slots, and a full window opened about one slot an hour (until 2026-10-13 it held 10 a day, so 30 slots, one every 2.4 hours). On 2026-10-08 the stream PC had 10 recordings waiting for upload and 29 uploads scheduled (#370). Before #370 the cap stopped at 30 in flight, so it recorded nothing for about a day, although a replay played 6 hours earlier would have waited one day and still had 1.75 days of its 3 left. Now that replay is recorded (`cap-queued`). One played 2 days earlier is not. Until #507 the waiting recordings were limited only by a day of upload calls (30), and on 2026-10-22 30 recordings (70.8 GiB) waited while a video went public 4 to 5 days after its game. Without the queue only about one waited, so a slot whose map, rank, or hero cooldown ruled out that one recording went unused. The publication pace is the same either way.

Notable and high-skill replays are capped like ordinary ones. Only a request goes past the cap. A capped replay is still spectated and streamed, without an mp4. Before the cap, production recorded about 25 replays a day while 4 a day could go public, and the rest waited on disk until they were too old to publish (#250).

### The disk gates

After the cap, the spectator checks the disk (`Disk`, #279). The log line names the gate and says whether the replay is a request.

| Gate | Settings | Holds back |
| --- | --- | --- |
| `free-space` | `WarnWhenFreeBytesBelow`, `StopWhenFreeBytesBelow` | Every recording, a request too. |
| `pending-bytes` | `WarnWhenPendingBytesAtLeast`, `StopWhenPendingBytesAtLeast` (bytes of recordings still waiting for their `videos.insert`; zero with `YouTube:DryRun` on) | Every replay that is not a request. A request is recorded while the free-space gate allows it. |
| `disk-check` | Invalid watermarks, or a reading that makes no sense | Every recording. |

Most pending bytes are ordinary recordings waiting for a publish time. When the `pending-bytes` stop watermark trips, the spectator first deletes the ordinary recordings that can never be published, then measures the disk again. A recording goes only when the uploader would delete it as stale itself: YouTube is live with a public listing, its stored media decision is an eligible `Ordinary` replay (not a request, notable, or high-skill), its game is older than `OrdinaryCandidateMaxAge`, it holds no slot in `Data\publication-reservations.txt`, no send has started, and it is not the replay about to launch. Each deletion logs `Removed recording that was eligible but never uploaded ... (reason: stale ...)`.

With `YouTube:DryRun` on (dev), no recording is ever sent, so none counts as pending bytes and the `pending-bytes` gate never holds back a dev recording (#317). Before this, dev proof recordings piled up as pending bytes until ASA-SERVER skipped ordinary recordings. With `DryRun` off, every recording that waits for its insert counts as before, one an earlier dry run planned too: a live uploader opens a new attempt for it and sends it.

A replay that is spectated again after a session that did not finish (a crash, a stop, a load timeout) decides its publication again from the new session. It used to reuse the first session's `incomplete`, so the second recording got no entry and retention deleted it. A recording whose publication is withheld (expired during the match, already published, already in the outbox) is deleted when the session ends, with a warning that names the reason, instead of waiting for retention.

Every mode still requires OBS `RecordingEnabled` (true in production). Both ReplayId rewards, `ReplayId` and `ReplayId + YouTube`, are recorded and uploaded as requests (#165). A map, rank, or random reward with no `RecordAndUpload` is spectate-only and is not a publication.

## Why a replay is one class

The first matching class wins.

1. **Requested.** The viewer typed a replay id (either ReplayId reward), or the Twitch reward is `RecordAndUpload`. A spectate-only map, rank, or random reward is not requested.
2. **Notable.** The replay has an individual pentakill: five or more killing blows by one player, each within 12 seconds of the previous blow. The blow has to be one of that player's own hero units, and the victim has to be an enemy player's hero unit. Lost Vikings and Rexxar with Misha count as that one player. A summon, a structure, a suicide, or a wipe split across several players is not a pentakill. When the five victims are five different enemy heroes, that pentakill also wiped the team (`TeamKillClip.WipedTeam`). That is a fact about the one pentakill, not a second event or a second clip (#369).
3. **High-skill.** Rank or MMR meets the configured floor. With no floor, this class never matches.
4. **Ordinary.** Everything else.

**Play order.** Waiting replays play in this order:
1. viewer requests;
2. Notable replays still inside their window (#510), oldest game first;
3. the focus plan;
4. the rest, newest first.

The queue knows a waiting replay is Notable from its focus card (`FocusCard.Notable`). The download role builds those cards while `Focus:Enabled` is on (dev and prod). The class itself is still decided at launch. Playing first does not by itself guarantee a recording: when the upload backlog is full, the recording cap treats a Notable replay like an ordinary one.

Each class expires from the game's UTC time. Ordinary expires at `OrdinaryCandidateMaxAge` (72 hours in production). Notable and high-skill expire after 7 days. A request expires after 14 days. Those three ages are not in `appsettings.json` today. `RecordingMode` `All` still records an expired replay. Publication does not offer an expired replay.

A request may ignore `MinimumGameVersion` when `RequestsBypassPatchRequirement` is true, which is the default. Production does not set `RequireCurrentPatch`, so the patch floor is not a publication gate. The downloader still lists Storm League only (`HeroesProfileApi:GameTypes`).

The match also has to be a verified complete game, and the recording has to be finalized and correlated with that replay. A missing manifest, an unfinished recording, or a local clock fails closed.

## When the entry is written

After a recording that can be published, a second check decides whether `youtube-entry.json` is written next to the mp4.

| Mode | Entry |
| --- | --- |
| `Disabled` | Not written |
| `RequestedOnly` | Written for a request |
| `Curated` | Written for a request, and for notable and high-skill. A full day does not drop them: the uploader schedules them for a later slot (#136). Only with `MaxPublishAhead` 0 are they written just while fewer than `MaxPublicPerDay` videos were observed public in the last 24 hours. An ordinary replay stays local. |
| `AllEligible` | Written for every class that is still inside its age, including when the day is already full. The uploader schedules it for a later day. |

The day count at this step is `Data`'s publication ledger of videos already observed public. The send step below uses its own reservation file, and that file is what spaces the publish times.

## When a video is sent

The uploader watches `Data\Contexts` for an mp4. It also retries pending files every 5 minutes. A pass sends a viewer request first (`Requested` in the entry), then the oldest recording first. A request never waits behind ordinary recordings for the quota: an ordinary upload leaves one of the day's remaining inserts for each request still waiting for its insert (#161). A dry-run process does not retry on that timer. It writes `youtube-dry-run.json` and does not call YouTube.

A recording stays on disk only for the YouTube quota or for the media rules. These lines decide whether it is sent now. The first refusal wins.

1. Configuration is valid.
2. The replay is not already published, incomplete, or uncorrelated.
3. The mode allows this class. `Disabled` refuses everyone. `RequestedOnly` refuses anything except a request. `Curated` refuses ordinary. `AllEligible` allows every class.
4. The upload bucket has room. `Data\youtube-quota-units.json` counts `videos.insert` calls today, and they stay under `YouTube:DailyUploadCalls` (100) less `YouTube:UploadCallReserve` (5). Library spend is a different bucket and never holds an upload. No quota response from an upload paused uploads, and `videos.insert` calls today are under `MaxInsertsPerQuotaDay`. The quota day starts at midnight Pacific. A held upload logs when uploads resume: the pause a quota response set, or the next Pacific midnight. `MaxInsertsPerQuotaDay` (95 in production since #507, the usable bucket) is this app's own cap, not YouTube's bucket, and its reason is `insert-cap` (it was `quota`, which read like YouTube's quota). When the bucket or the insert cap is closed, the pass does not open each recording: it logs one line (`N recording(s) wait: the daily insert cap is reached (20 of ReplayMedia:MaxInsertsPerQuotaDay 20 ...). Uploads resume at ...`) when the gate closes, and only an interrupted upload (below) still goes on, because resuming its session spends no new insert.
5. When `YouTube:PrivacyStatus` is public, an ordinary replay's game time is inside `OrdinaryCandidateMaxAge`. A request, a notable replay, and a high-skill replay do not use this age at send time. They already expired by their own windows above. An ordinary replay that is too old is not retried, and its recording is deleted with a warning (`Removed recording that was eligible but never uploaded ... stale`). The recording cap exists so this does not happen.
6. When `YouTube:PrivacyStatus` is public, a publish time inside `MaxPublishAhead` (3 days in production; the code default is 14) keeps every rule below. With none, the reason is `publication-full` (it was `horizon`): every time in that window breaks a pacing rule for this replay, and the recording waits for a later pass. Another replay with a different map, rank, or heroes can still find a time in the same window.

A paid request is not paced. It publishes as soon as it is uploaded, whatever the rules below say. The rules below no longer hold an ordinary, notable, or high-skill recording back. They choose its publish time: the earliest time from now that keeps all of them. Each rule looks both ways, at videos already public and at slots already scheduled, so a later replay can take a free time between two earlier ones.

- Week. No rolling 7 days holds more than `MaxPublicPerWeek` videos.
- Day. No rolling 24 hours holds more than `MaxPublicPerDay` videos.
- Reserved request room. With production's 12 and 2, a non-request may not join a rolling 24 hours that already holds 10 videos, requests included, so the last 2 stay free for requests.
- Interval. Every other publish time is at least `MinimumPublicInterval` away.
- Map. No slot within `MapCooldown` has the same map. Comparison ignores case and surrounding spaces.
- Rank. No slot within `RankCooldown` has the same tier. Division is ignored, so Diamond 3 and Diamond 1 are the same tier. An unrecognized rank is compared as written. MMR is not part of this check.
- Heroes. No slot within `FeaturedHeroCooldown` has the same focus hero. Separately, fewer than `MaxSharedHeroes` heroes from this replay (4 unless configured otherwise, and 0 turns this roster check off) appear in slots within that window. One shared hero is fine.

A paid request publishes now. Its slot still records the map, the rank, the focus hero, and the roster, so later ordinary replays plan around them. Nothing sorts the queue by a score.

The log names the rule that pushed the time later (`interval`, `day`, `reserved`, `week`, `map`, `rank`, or `hero`), or `ready` when the time is now.

A private listing (`YouTube:PrivacyStatus` private, as in `appsettings.dev.json`) still stops at the quota. It has no publish time and no other rule. The listing stays private and the title carries `YouTube:TitlePrefix` (`[TEST]` in dev).

One replay id takes one publication slot, stored in `Data\publication-reservations.txt`. The slot's time is the publish time, which can be days after the upload. A retry of that same id does not take a second slot and uses the same time. A clip shares its replay's slot, so it publishes with the full match. An older line with only the time, the request flag, and the replay id still counts for the day, the week, and the interval.

The insert is private with `publishAt` set to the slot's time. A time that has already passed by the end of the upload publishes the video right away. The entry is renamed to `youtube-entry-uploaded.json` when YouTube's insert response is already public. A response that is still private keeps `youtube-entry.json` with the video id and the publish time; retention removes the mp4 at once and the context after `VideoKeepDays`, so these entries do not pile up. This process does not ask YouTube itself whether `publishAt` has fired. The library pass below confirms it public.

A granted send that fails still keeps its slot. The retry is allowed through that slot (the day's insert cap and the ordinary age do not hold it back again) and spends another `videos.insert` only if a new upload starts. When the slot's time has already passed before a new insert, the slot is rescheduled first: the pacing rules pick the next valid time around every other slot, the log says `Replay ... missed its publish time ... It now publishes at ... (rescheduled-<rule>)`, and the insert carries that time, not a past `publishAt`. With no free time inside `MaxPublishAhead` the old slot stays and the recording waits (`publication-full`). A request is rescheduled to now. A replay already on the channel (a clip of an uploaded match) keeps the slot its video has. A daily quota response from YouTube during an upload (`quotaExceeded`, `uploadLimitExceeded`, or a "per day" limit) pauses new uploads until the next Pacific quota day. The library pass spends the other bucket and goes on.

### Interrupted uploads

A send that stops part way (a release restart, `services stop`, a network failure) is retried by the uploader itself. Nobody has to act after a release.

- Each recording has its own attempt under `Data\upload-attempts` (#524). It is named `replay-<id>-<yyyyMMddHHmmss>` from its dispatch, and a second recording of the replay in that same second gets `-2`, `-3` and so on. A dry run plans a full match and its clips within milliseconds. Before #524 a clip found the match's settled attempt and was skipped as `already DryRunSimulated`; that happened to 66249756's pentakill clip on 2026-10-25. An open (uploading or ambiguous) attempt is picked up again only for the same recording, never for another clip of the replay.
- The resumable session URI is saved on the attempt before the first byte is sent (`InitiateSessionAsync`, then the media), so every interrupted send can be checked. YouTube creates the video only when it has every byte, so an incomplete session has no video.
- The saved URI never holds the API key (#368). Google.Apis starts the session with `key=<YouTube:ApiKey>`, and YouTube's session URI repeats it, so the `key` parameter is taken out before the attempt is written (`UploadSessionUri`) and the configured key is added back only for the send and the status query, which are then the same requests as before. An attempt saved before #368 is rewritten without the key the next time it is read, atomically under its lock, and the uploader reads every saved attempt once when it starts (`Removed the API key from N saved upload sessions.`). The role logs, the heartbeat `lastError`, and CLI output mask `key=` and any Google API key.
- On a stop the send ends at the next chunk boundary (10 MB chunks), so YouTube keeps every byte sent; a chunk that has not finished after 10 seconds (`YouTubeUploader.StopGrace`) is cut off, inside the 20 seconds `services stop` waits. The log says `Upload of ... paused for the service stop after N of M bytes. YouTube keeps the upload session; the next start resumes it.`
- The next pass asks the saved session how much YouTube holds (`PUT` with `Content-Range: bytes */size`, no quota): a finished session (the send was cut off after its last chunk, before the response) is recorded with its video id and never inserted again; an incomplete one is resumed from YouTube's offset while its publish time is still ahead. When its publish time has passed, the slot is rescheduled (above): if the new time is now the session is resumed, otherwise the old session is dropped (it has no video) and a new insert carries the new time. A session YouTube no longer knows (404 or 410) may have finished, so it is left for an operator. A failed status query is asked again next pass.
- An attempt with no saved session never sent any media, so it is started again, unless its replay id is already in `youtube-replay-ids.txt`, in which case it is left for an operator.
- Each retry logs `Upload of ... was interrupted. Automatic retry N of M: ...`. `YouTube:InterruptedUploadRetries` (5) bounds them per attempt, counted in `Data\youtube-upload-retries.json` across restarts. After that the recording logs once that it waits for an operator; removing its attempt's line from that file allows the retries again.

### Publication health

After each pending pass the uploader writes `Data\publication-status.txt` and logs `YouTube publication health pending ... uploaded ... deferred ... published-day ... published-week ... scheduled ... stuck-private ... limit ... policy .... Waiting: insert-cap 17, publication-full 3.` at most every 5 minutes (and after a pass that sent something). `deferred` is the number of recordings waiting now, and `limit` names what holds most of them: `upload-bucket`, `insert-cap`, `publication-full`, or `none`. A waiting recording logs its own line once, when it starts waiting and when its reason changes, not on every pass. A recording the pass finds already has its entry, so it is not watched for 10 seconds before its turn; only a new mp4 from the watcher waits until its size holds still. The published, scheduled, and stuck counts come from `Data\youtube-library.jsonl`, which the library pass updates when YouTube reports a scheduled video public (#250):

- `published-day` and `published-week`: videos reported public whose publish time is in the last 24 hours or 7 days.
- `scheduled`: uploads not public yet whose publish time is ahead, or passed less than two library passes ago (at least 2 hours).
- `stuck-private`: uploads still not public later than that, within the last 30 days.

Before #250, `published-*` counted only inserts whose response was already public, which a scheduled upload never is, so it stayed 0. `stuck-private` added one for every scheduled upload and never went down, so it grew by 6 a day (the production pace then) while the videos did go public on schedule.

When a scheduled video is still not public after that grace, the library pass logs one warning with each video's `status` from YouTube: privacy, `uploadStatus`, `publishAt`, and any `rejectionReason` or `failureReason`. A video that stays private with no rejection is the sign of a Google API project that is not audited: YouTube keeps every `videos.insert` from such a project private. The code cannot fix that; the project needs the YouTube API audit.

`services status` reports the youtube role `degraded` with a cause code (#250):

- `youtube.quota_blocked`: recordings wait and the upload bucket, a quota pause, or `MaxInsertsPerQuotaDay` holds new uploads. The cause says which: the YouTube upload quota, or this app's daily insert cap.
- `youtube.not_publishing`: uploads are past their publish time and no video was confirmed public in the last 24 hours (or ever). Either the library pass is not running (no pool room, no `{ChannelId}-library` consent; the cause names a pass that was skipped for want of consent) or YouTube keeps the uploads private.

Both clear on their own once the cause is gone. A dry run and a private listing are never degraded for publishing.

The role also probes its upload consent (#305, `docs/service-split.md` Dependency probes): before it reports ready and then every 10 minutes (2 while it fails), it refreshes the stored upload token at Google's token endpoint. That is not a YouTube Data API call, so it spends neither the 10,000-unit pool nor the upload bucket, and the store is not written. Not in a dry run.

- `youtube.oauth_invalid`: Google refused the stored refresh token (`invalid_grant`: revoked, expired after 7 days for an OAuth app in Testing, or a changed `client_secrets.json`). Grant the consent again at the machine: stop the stack, delete `%APPDATA%\Google.Apis.Auth\Google.Apis.Auth.OAuth2.Responses.TokenResponse-{ChannelId}`, run `heroesreplay youtube uploader` by hand and sign in, then start the stack.
- `youtube.oauth_missing`: no upload consent is stored for the channel (the uploader would wait for a browser sign-in nobody sees), or `client_secrets.json` is missing.
- `youtube.oauth_unreachable`: the token endpoint did not answer. Clears when a probe passes.

The supervisor does not restart the role for any of them; recordings wait on disk.

Without a concern, the role is `degraded` when it did no healthy work for 30 minutes (`No successful upload pass ...`). Healthy work is a successful upload (each one, so a long pass of large uploads keeps the role ready), a dry-run receipt, a library pass that ran or skipped for a routine reason (not due, another process has it, the day's units are spent, a quota pause), and an upload pass where every recording was sent or correctly waits (the upload bucket, the insert cap, `publication-full`, its entry not written yet), including a pass with nothing pending. Between passes the minute poll keeps that state. A pass with a failed send, an unreadable entry, or an interrupted upload that waits for an operator is not work. Before this, the work was recorded only after a whole pass and the library pass, so a startup pass that uploaded 13 videos over an hour left `lastSuccessfulWorkAt` null and the role degraded.

A dry run plans in `Data\publication-reservations-dry-run.txt`, so its times never take a live slot. `youtube-dry-run.json` records the plan: the insert privacy, the desired privacy, `PublishAtUtc`, the schedule result and its reason (for example `granted` and `interval`, or `refused` and `publication-full`), `SelfDeclaredMadeForKids`, and the category. A dry run does not delete a recording when it plans it. Its entry stays `youtube-entry.json`, so the ordinary retention sweep keeps the context as one that waits for its insert.

`Retention:DryRunRecordingMaxAge` clears those recordings (#317). Dev sets `2.00:00:00`; the base file and production set `00:00:00`, which is off. The youtube role deletes each mp4 at the top of a context whose `youtube-dry-run.json` was written after it, once the mp4 is older than that age, and logs `Removed N dry-run recording(s) ...`. It runs with the uploader's retention sweep, after each dry-run plan, and at the end of each upload pass (in a dry run, once when the role starts). The `clips` folder, `end.png`, the `.StormReplay`, `youtube-entry.json`, and the plan stay. A recording with no plan yet, or one recorded after the plan (the replay spectated again), stays. With `DryRun` off the sweep removes nothing, even where an earlier dry run left a plan.

## How often

The spectator plays the next queued replay as soon as the previous session ends. YouTube does not follow that clock.

A recording is uploaded as soon as the upload bucket and a publish slot allow. YouTube's Video Uploads bucket is 100 calls a day; 5 are held back, and production's `MaxInsertsPerQuotaDay` is the other 95 (#507; it was 30, which held uploads below the bucket). Full matches and clips share it, and library spend never takes from it. Before #250 the app charged each insert 1600 units from the 10,000-unit pool, so it stopped at 6 uploads a day and starved the library pass, while Google's console showed 6 of 100 upload calls and 4 of 10,000 pool units used.

With the production settings (#507) the videos go public at most every 20 minutes (`MinimumPublicInterval`), at most 72 in any rolling 24 hours (`MaxPublicPerDay`; a non-request only while fewer than 70 are in it, because `ReservedRequestSlotsPerDay` keeps 2 for requests), and at most 504 in any rolling 7 days (`MaxPublicPerWeek`). The same map 2 hours either side, the same rank tier 1 hour either side, or the same featured hero or a roster that shares 4 or more heroes 4 hours either side moves the time later. The stream plays about 60 games a day, a game every 24 minutes, so in practice each video publishes within a few hours of its upload, most at once.

From 2026-10-13 to #507 production ran 24 a day, an hour apart, with 8-hour map and hero cooldowns and a 2-hour rank cooldown (4 hours until 2026-10-17): 22 ordinary videos a day, which kept the 3-day window full, so 30 recordings waited on disk, about half the games were not recorded, and a video went public 4 to 5 days after its game. Until 2026-10-13 it ran 12 a day, 2 hours apart, with an 8-hour rank cooldown: 10 ordinary videos a day, which filled the 3-day window, stopped new recordings (`cap-publication-full`) and let eligible recordings expire unpublished.

Twelve ordinary games that end at 10:00, 10:30, and every half hour to 15:30 UTC, on a quota day with nothing spent yet. No map repeats within 8 hours, no rank tier within 4, and no two rosters share 4 heroes. At the production settings each one publishes as it is uploaded: 30 minutes apart is more than the 20-minute interval. `PublicationSchedule.Plan` with the settings before #507 (an hour apart) gave:

| Game | Uploaded | Publishes | Why not earlier |
| --- | --- | --- | --- |
| 1 | 10:00 | 10:00 | |
| 2 | 10:30 | 11:00 | interval |
| 3 | 11:00 | 12:00 | interval |
| 4 | 11:30 | 13:00 | interval |
| 5 | 12:00 | 14:00 | interval |
| 6 | 12:30 | 15:00 | interval |
| 7 | 13:00 | 16:00 | interval |
| 8 | 13:30 | 17:00 | interval |
| 9 | 14:00 | 18:00 | interval |
| 10 | 14:30 | 19:00 | interval |
| 11 | 15:00 | 20:00 | interval |
| 12 | 15:30 | 21:00 | interval |

A paid request that ends at 16:00 that day publishes at 16:00, as soon as it is uploaded, at the same time as game 7: a request is not paced. All twelve uploads stay under `MaxInsertsPerQuotaDay`, so each recording goes on the retention sweep that follows its upload. With `MaxPublishAhead` 3 days and 70 non-request videos a day, the publication window holds 210 slots, more than three days of the stream's games, so it fills only when the stream plays more than 70 games a day. Once it is full, the recording cap still records an ordinary game while the slots ahead of it leave at least a day before the game is 3 days old (`cap-queued`, see [The recording cap](#the-recording-cap)), and no more than `MaxWaitingRecordings` wait on disk. A video then goes public at most 3 days after its upload.

## What the video contains

A full match title is one line of at most 100 characters. It never names a date, an MMR, or who won. Without match highlights (below) it does not name a pentakill or a team wipe either. With them, one hero's own pentakill can lead the title, never a wipe shared across players.

- Ordinary: `Volskaya Foundry - Storm League - Diamond - 65389750`.
- A reward that names a player: `Illidan focus - Dragon Shire - Storm League - Diamond 3 - 65550001`. The title never names the Twitch viewer. The hero, here and on the description's `Featured:` line, is the catalog's English name of the hero that player played (below), not the replay's character name, which is in the uploader's game language (a Russian client writes `Диабло` for Diablo). Videos uploaded before that keep their old `Featured:` text.
- A paid upload that does not name a player has the ordinary title. The requestor is credited in the description only.
- A new hero in the match: `Ft. Xal'atath - Volskaya Foundry - Storm League - Diamond - 65389750`. Ft. means featuring. One hero only. When the reward already leads with that hero, the title does not say her twice.

The ordinary title does not say MVP. With match highlights on, the game's own MVP award, read from the replay, can lead it, whichever team the MVP was on. The heroes in the title come from the parsed replay, in the catalog's English name: the hero unit the game spawned for each player (`HeroDemonHunter` is Valla), then the lobby hero attribute outside ARAM, then the character name. In ARAM the lobby attribute is the hero the player had selected before the game assigned one, so it is never used there (#348).

A draft note is added before the replay id when the current hero-select roles are not one tank, one bruiser, one healer, and a ranged assassin. Those roles are Tank, Bruiser, Melee Assassin, Ranged Assassin, Healer, and Support. Johanna plus Chen is a tank and a bruiser, so that draft is left alone. A tank talent overrides the catalog role: Varian is a Bruiser in hero select, but Varian with Taunt (`VarianTaunt`, level 4) counts as a tank, so Muradin plus Taunt Varian is a double tank and Taunt Varian plus Chen is left alone (issue #187). The talent comes from the replay's tracker events. The note is the whole match when both teams share it (`Cursed Hollow - Storm League - Diamond - No healer - 65550001`) and names the team when they differ (`Blue no tank, Red double healer`). A hero the catalog cannot match, or a hero with no current role, suppresses that team's note. A normal draft adds nothing.

`YouTube:Titles` in `appsettings.json` and `appsettings.prod.json` turns each form on or off: `DraftNotes`, `NamedPlayerTitles`, and `FeatureNewHeroes`. Each draft note has its own switch (`NoTankOrHealer`, `NoHealer`, `DoubleHealer`, `TripleHealer`, `DoubleBruiserWithoutTank`, `NoTank`, `DoubleTank`, `TripleBruiser`, `DoubleSupport`, `NoRangedAssassin`). The six role labels are in the same section. Turning a form off leaves the map, mode, rank, and replay id.

### Team compositions

A team can also get a composition label (issue #140). The rules read the heroes-data2 catalog: the hero-select role, the `playstyles` tags, `isMelee`, and the 1 to 10 ratings. A team with a hero the catalog cannot match, or with fewer than five heroes, gets no label.

| Label | Key | One team has | Games in the corpus |
| --- | --- | --- | --- |
| Split push | `SplitPush` | 3 or more `SoloLaner` heroes | 4.7% |
| Siege | `Siege` | 4 or more heroes that are `TowerPusher` or `WaveClearer` | 6.1% |
| Dive | `Dive` | 4 or more `Ganker` heroes and 4 or more `Escaper` heroes | 3.7% |
| Poke | `Poke` | 3 or more ranged `RoleCaster` heroes | 1.7% |
| Double melee assassin | `MeleeAssassins` | 2 or more Melee Assassins. Three is `Triple melee assassin` | 2.7% |
| All melee | `AllMelee` | 5 melee heroes | 0% |
| One ranged | `OneRanged` | 4 melee heroes | 6.4% |
| Triple sustain | `Sustain` | 3 or more heroes that heal or shield allies: the Healer role, `AllyHealer`, or `AllySustain` (Zarya). A `SelfHealer` tank or bruiser (E.T.C., Yrel) does not count (issue #247) | 2.4% |
| Triple specialist | `Specialists` | 3 or more `RoleSpecialist` heroes | 1.0% |
| Merc control | `MercControl` | 2 or more `MercKiller` heroes | 7.1% |
| Glass cannon | `GlassCannon` | Average survivability 4.4 or less and average damage 7 or more | 1.0% |

The corpus is 295 Storm League games on build 2.57.0.98304, replay ids 65635951 to 65645361, read by `heroesreplay calculators compositions`. A label is named only while its share of games in `YouTube:Titles:Compositions:Frequencies` is below `MaxFrequency` (0.1). The first run used the plan's thresholds and named double soak in 77% of games, dive in 71%, siege in 39%, two specialists in 17%, and one ranged in 10.2%, so those rules went up. A tower pusher that also clears waves is not counted as a soaker, because Kael'thas, Jaina, Sylvanas, and Sgt. Hammer have both tags. Two solo laners are still 45% of games (the offlaner plus one soaker), so the label needs three and is called Split push.

`HeroTagOverrides` changes a hero's tags before any rule counts them. The defaults:

| Hero | Change | Why |
| --- | --- | --- |
| Brightwing | Remove `SoloLaner` | A healer. She was her team's only healer in 76 of 78 corpus teams. |
| Cho | Remove `SoloLaner` | Cho and Gall share one body, so Cho does not hold a lane alone. |
| Hogger | Add `Ganker`, `SoloLaner`, `WaveClearer` | The catalog has no playstyles for him. Tagged like Sonya, the offlaner he is picked as. |
| The Lost Vikings | Add `RoleSpecialist`, `SoloLaner`, `WaveClearer` | No playstyles in the catalog. The three Vikings soak lanes apart. |
| Maiev | Add `Escaper`, `Ganker` | No playstyles in the catalog. Tagged like Illidan and Zeratul. |
| Abathur | `IsMelee` false | His 1-range attack makes him melee in the catalog, but he plays from behind the wall. He was in 13 of the 31 corpus teams with four melee heroes. |
| Zarya | Add `AllySustain` | A support with no `AllyHealer` tag. Shield Ally keeps allies alive, so she counts toward Triple sustain. |

`YouTube:Titles:Compositions` holds `Enabled` (true), `MaxFrequency`, one rule per label (`Enabled`, `Label`, `MinHeroes`, and `MinEscapers` for Dive, or `MaxSurvivability` and `MinDamage` for Glass cannon), `Frequencies`, and `HeroTagOverrides` (`Add`, `Remove`, `IsMelee`, and an unused `Reason`). A configured key replaces that one default. A key missing from `Frequencies` counts as rare.

The labels go into four places:

- **Title.** At most one composition label, the rarest by `Frequencies`, in the draft note's slot. The role notes keep the slot unless the label is rarer than each of them: `Blue double bruiser, Red dive` in the description is `Red dive` in the title, because dive is 3.7% of games and double bruiser 14.9%. A label both teams have has no team. The slot is still the first part dropped at 100 characters.
- **Description.** The `Draft:` line lists every role note and every named label for both teams: `Draft: Red double tank, Blue dive, Red triple sustain`.
- **Tags.** One tag per label (`Dive`, `Triple sustain`), after `Heroes of the Storm`, so the 500-character limit drops them first.
- **Playlists.** A label is a draft note to the library pass, so the video goes into `Unusual drafts`.

Titles from the corpus:

- `Cursed Hollow - Storm League - Gold 4 - Red split push - 65636323`
- `Alterac Pass - Storm League - Silver 2 - Blue dive - 65635953`
- `Sky Temple - Storm League - Master - Red triple sustain - 65641053`
- `Ft. Xal'atath - Alterac Pass - Storm League - Bronze 2 - Red glass cannon - 65644212`
- `Ft. Xal'atath - Haunted Mines - Storm League - Bronze 1 - Red triple specialist - 65636653`

To retune, download a fresh corpus into a scratch folder (not `Data\Standard`), run `heroesreplay calculators compositions --directory <folder> --output report.md`, change the thresholds, run it again, and copy each game share into `Frequencies` (or `TeamCompositionSettings.CorpusFrequencies`).

A hero is featured when its name is in `RecentHeroes`, or when the catalog `releaseDate` is within `RecentHeroDays` (60) of the match. `RecentHeroDays` of 0 or less uses the name list only. The newest release date wins. A name on the list is still featured when the local catalog does not have that hero yet. Xal'atath is on the list. The description adds `Featuring: Xal'atath` when the title does. Clips are unchanged.

### Statistics hooks

A title can also name a Heroes Profile statistic about the heroes in the match (issue #272). It is off everywhere (`YouTube:Titles:StatHooks:Enabled` false in the base, dev, and prod files) until it is proven. With it on, the download role keeps the statistics, and spectate reads them when it writes `youtube-entry.json`.

A hook takes the draft note's slot, just before the replay id, so it is the first part dropped at 100 characters. A draft note wins the slot: a match with a draft note gets no hook. There is at most one hook, and it never has a number in it. The first kind, in this order, with a match that clears its thresholds wins; within a kind the strongest match wins.

| Hook | Example | When (each threshold is a setting) |
| --- | --- | --- |
| Counter | `Valla counters Alexstrasza` | Opposite teams. A has at least 250 games against B (`CounterMinGames`) and wins at least 57% (`CounterMinWinRate`). That rate is at least 4 points (`CounterMinEdge`) over what their overall rates predict, `50 + (A - 50) - (B - 50)`, and the low end of its 95% Wilson interval is over both 50% and that prediction. Xal'atath beats Valla 65.3% of the time, but she wins 65.9% of all her games, so 65.7% is expected and there is no counter. |
| Duo | `Valla + Whitemane duo` | Same team, at least 300 games together (`DuoMinGames`), at least 56% (`DuoMinWinRate`), and the low end of the 95% interval at least 52% (`DuoMinLowerBound`). |
| Best map | `Braxis Holdout - Genji's best map - ...` | This map is the hero's highest win rate among its maps with at least 150 games (`MapMinGames`), at least 3 points (`MapMinDelta`) over the hero's overall rate. |
| Slips the ban | `Qhira slips the ban` | Banned in at least 40% (`BanMinRate`) of this map's games, with at least 150 games played on it (`BanMinGames`). |
| Worst map | `Cursed Hollow - Valla's worst map - ...` | The reverse of best map. |
| Underdog or powerhouse | `Underdog Medivh`, `Patch powerhouse Mal'Ganis` | Among the 3 (`ExtremesCount`) lowest or highest win rates of the patch, counting heroes with at least 500 games (`ExtremesMinGames`). |

A hero the title already names, as `X focus` or `Ft. X`, is skipped, and so is any pair with that hero. Heroes are matched to the statistics by the heroes-data2 `AttributeId`, which is Heroes Profile's `attribute_id`. Each kind has its own switch: `Counters`, `Duos`, `BestMap`, `SlipsTheBan`, `WorstMap`, `PatchExtremes`.

The numbers go on a `Stats:` line in the description, for example `Stats: Valla and Whitemane won 56.7% of 577 games together (Storm League, patch 2.57).` Heroes Profile's terms require the attribution on the same screen as its data, so whenever a hook is used the second description line, right after the Twitch link and inside what YouTube shows before "more", is `Data provided by Heroes Profile: https://www.heroesprofile.com/`. The `Stats:` label is not one the library pass reads (`Map:`, `Mode:`, `Rank:`, `Build:`, `Draft:`, `Featured:`), and a hook is never a bare number, so `TryReadTitleId` and the playlist filing read a hooked title the same way.

The statistics are one file per major patch and game type, `Data\HeroesProfile\hero-stats\2.57-sl.json`, keyed by `attribute_id`: each hero's wins and games on every map (and its ban rate there), against each enemy hero, and with each ally. The download role writes it, never spectate. While hooks are on it checks every `HeroesProfileApi:HeroStats:CheckInterval` (1 h) and fetches the newest major patch again when the file is older than `RefreshInterval` (24 h), for each `GameTypes` entry (Storm League). Files of patches older than the newest two are deleted. Spectate uses the file for the replay's own patch and game type only when it is younger than `MaxAge` (72 h); a missing, unreadable, or stale file leaves the title as before. `heroesreplay heroesprofile hero-stats` fetches it once by hand. The calls and their limits are in `docs/heroesprofile-api.md`.

### Match highlights

With `YouTube:Titles:Highlights:Enabled` (on in dev and prod, off in the base file), the title leads with one highlight read from the replay itself (issue #412). The highlights name heroes, builds and the shape of the match. They never say who won, because the MVP can be on either team and the video is the match. Struggling players and teams are never called out.

**Where the facts come from.**

- At session end, spectate reads the replay it already parsed into match insights (`Analysis/Insights`, `MatchInsightsRecorder`), together with the focus timeline it already followed. Nothing is parsed or calculated twice.
- It writes them next to the recording as `insights.json`.
- A failure is logged, and the entry is written without highlights.

**Title layout.** `[X focus] - highlight - [Ft. X] - Map - Mode - Rank - [draft note] - ReplayId`, for example `Deathless Teleport Li-Ming 12/0/9 - Garden of Terror - Storm League - Master - 65581797`.

- **Too long.** Over 100 characters, the draft note goes first, then the highlight's shorter wordings are tried (without the build, then without the score). If none fits, the title is built as before.
- **No bare number.** No part is a bare number, so the replay id stays the first all-digit part.
- **Named-player requests.** A request that named a player keeps its `X focus` lead, and takes only a highlight about no one hero.
- **Featured hero.** A highlight about the featured new hero replaces its `Ft.` part.

**What can lead.** The first kind in `Order` that the match has wins. The default order is `Pentakill`, `TripleKill`, `CoreRace`, `Comeback`, `Award`, `DeathlessCarry`, `KillLeader`, `ObjectiveStandout`, `SoloLane`, `Mvp`, `MostFollowed`: rarest first, with the objective standout after the kill standouts so it leads only when no kill feat does (#512). A name in `Order` that is no kind is ignored, so an older config that still lists the removed `CampDrought` starts as before. Every kind is a positive call-out about a hero or the match; a team is never called out for what it did not do, so a team's camp count stays on the neutral `Camps:` line of the description:

| Kind | Title | When | Measured on 545 local games |
| --- | --- | --- | --- |
| `Pentakill` | `Valla pentakill` | One hero's own pentakill (#369 rules) | rare |
| `CoreRace` | `Core race finish` | Both teams lost keeps, and 2+ heroes of the team whose core fell were at the other core in the last 60 s. Core health is not in the replay | 1% |
| `Comeback` | `Insane comeback` | The team whose core survived lost 3+ more towers, forts and keeps, or was 3+ levels behind at some point | 4% |
| `DeathlessCarry` | `Deathless Teleport Li-Ming 12/0/9` | 0 deaths, 8+ kills | 11% |
| `KillLeader` | `Valla 14/1/6` | 10+ kills, at most 1 death | 8% |
| `SoloLane` | `Solo lane Chen vs Thrall` | Each team's one side-lane soaker from gates open to the first level 10, at High confidence only: the other team's soaker was in the same lane | 32% of teams |
| `Mvp` | `MVP Orphea 7/1/5` | The game's MVP award | 100% |
| `MostFollowed` | `Valla 3/4/10` | The hero the focus timeline followed most | |
| `TripleKill` | `Kael'thas 2 triple kills` | One hero's triple kills (3 hero killing blows within 3 s, the clips' rule, #511), at least `TripleKillMinBursts` (2) of them. One triple kill is clipped but never titled | 2 of 1,975 SL games |
| `ObjectiveStandout` | `Valla 52% of Immortal damage` | The map objective's leader (`Insights:Objectives`): at least 40% of every player's total, at least twice the next player, and the game's own objective award. Battlefield of Eternity, Braxis Holdout, Cursed Hollow, Dragon Shire, Haunted Mines, Infernal Shrines, Sky Temple, Tomb of the Spider Queen and Towers of Doom; Alterac Pass, Garden of Terror and Volskaya Foundry have no usable value | 6.0% of SL games |
| `Award` | `Falstad hat trick`, `Rehgar 9 clutch heals` | The game's Hat Trick, or Clutch Healer with 8+ clutch heals, Avenger with 5+ vengeances, Escape Artist with 6+ escapes (`Insights:Awards`). The award alone is common: the game gives most players one | 2.2% of SL games |

The last three rows (#512) were measured on 1,975 Storm League games from 2026-10-25 with `calculators insights`.

**Builds in a title.** A player highlight names the hero's closest guide build only when all of these hold:

- the talents score at least `TitleBuildMinScore` (0.85) against it (1 per pick the build recommends, 0.5 per situational pick);
- no other build is within 0.10;
- the name is not a plain one (`GenericBuildNames`: Standard, Basic Attacks, Hybrid, ARAM and so on).

**Description lines.**

- **`Highlights:`** keeps the pentakills first, then adds the standouts (`Li-Ming 12/0/9 with no deaths`), the MVP (merged into a standout of the same player), each hero's triple kills (`Valla triple kill`, also a single one; not for a hero with a pentakill), the objective standout, the awards, the core race, and the comeback.
- **`Solo lanes: Chen (Blue) vs Thrall (Red)`** names High confidence only.
- **`Camps: Blue 9 (4 on Red's side), Red 3 (0 on Blue's side)`** counts each team's captures and those on the other team's side.
- **The talent-build block** follows the roster lines. Its heading credits Icy Veins and links its hero guides, and each line names one player's closest guide build and links that guide. A player is listed when their talents score at least `DescriptionBuildMinScore` (0.70): about 57% of players in the corpus. Example: `Li-Ming (Blue): Teleport Build, 6 of 7 talents as recommended - https://www.icy-veins.com/heroes/li-ming-build-guide`.

None of these lines starts with a label the library pass reads.

**Tags.** The highlight's tag (`Deathless`, `Core race`, `Comeback`, `Solo lane`, `MVP`, `Pentakill`, `Triple kill`, `Objective`, `Hat trick`, `Clutch healer`, `Avenger`, `Escape artist`) and the build with the hero (`Teleport Li-Ming`) come after the composition labels.

**The build reference.**

- The builds come from `src/HeroesReplay.Core/Builds/Reference/icy-veins-builds.json`, embedded in Core.
- It holds 91 heroes and 296 builds as build names, tags, talent ids per tier, and guide and calculator links, never guide text.
- It is refreshed by hand with the `icy-veins-builds` skill and `heroesreplay builds import`.
- `heroesreplay builds match --replay <file>` shows each player's best build.

**Previews and tuning.**

- `heroesreplay youtube preview --replay <file> [--insights]` prints the title, description and tags a replay would get, offline.
- `heroesreplay calculators insights --directory <folder> --recursive --output report.md` measures how often each insight fires on a corpus. The thresholds are in `Insights`.

The description starts with `Twitch: https://twitch.tv/saltysadism`, then the Heroes Profile attribution when a statistics hook was used, `Full match.` when the recording completed, the replay id, the Heroes Profile match link, date, build, map, mode, rank, the featured hero when one was named, the draft note with its composition labels, `Featuring:` when a new hero is in the title, the `Stats:` line of a hook, each pentakill as a highlight (`Highlights: Valla pentakill (team wipe)` when it wiped the team), and the requestor when it was a paid upload. The Blue and Red roster lines name each player without the BattleTag number, because YouTube turns `#1234` into a hashtag. Average MMR is not written. The winner is not included. Category id is `20`. Tags come from the map, mode, rank, hero, those events, and each composition label. The entry records `TemplateVersion` 10 (9 had no triple kill, objective or award highlights, 8 no focus-hero titles, 7 no match highlights, 6 no statistics hook).

Every `videos.insert`, full match or clip, is built by `UploadBody` with the `snippet,status` parts:

- Audience: `status.selfDeclaredMadeForKids` is false, which is Studio's "No, it's not made for kids".
- Age: nothing sets `contentDetails.contentRating.ytRating`, so no video is restricted to viewers over 18.
- `status.privacyStatus` private, with `status.publishAt` set to the slot's time when the listing is public.
- `status.containsSyntheticMedia` false (Studio's altered content question), `status.embeddable` true, and `status.license` `youtube`.
- `snippet.categoryId` from the entry, or `20` (Gaming) when the entry has none, as an older clip entry can.
- `snippet.defaultLanguage` and `snippet.defaultAudioLanguage` `en`.
- The title, the description lines joined by line breaks, and the tags.

Videos uploaded before this template can still have BattleTag numbers in the roster lines, or a viewer's name in the title. `tools/youtube-fix-descriptions.cs` is a one-off script that rewrites those videos on YouTube. It only prints the changes unless it is given `--apply`, and it changes titles only with `--titles`.

Clips are one player's own feats, one full-frame cut each under the context `clips` folder. A wipe shared across players is never cut (#369).
- **Pentakill.** A pentakill that wiped the whole team is still one clip.
- **Triple kill (#511).** A burst of 3 or more of one hero's killing blows within 3 s of the first, that is not part of a pentakill, is cut too, at most 2 a replay, earliest first. On 1,965 Storm League games one happened in 55 games (Kael'thas in 10). A triple kill never makes the replay Notable and adds nothing to the media score: only a pentakill does (owner decision 2026-10-25). Its title says `quadra kill` when the burst has 4 blows.
- **Boss steal and boss defended (#528).** The `Bosses` insight reads the fight at the pit before each boss capture (`JungleCampCapture` of a `Boss Camp`) from hero positions: the seconds up to the capture with a hero within `Insights:Bosses:Radius` (12) of the camp, gaps of up to 4 s allowed. A team *started* the boss when its group (3+ heroes) was there at least 5 s before any hero of the other team. A killed boss is captured by standing in its circle, and the replay records both who killed it and who captured it. A **steal** is a capture by the team that did not land the killing blow and had not started the boss, or by the team that did not start it while the starter's group stood there 8 s or more. **Defended** is a capture by the team that started it, after the other team landed the killing blow (a steal that failed at the circle), or after both teams' groups stood at the pit together for 6 s or more and 3 or more heroes died there. On 1,757 prod games (1,752 Storm League, 1,505 boss captures) a steal happened in 65 games (68 captures) and a defended boss in 30 (31). In 54 captures the other team landed the killing blow: 31 steals and 23 steals that failed at the circle. Pentakill/triple kill clips win: a boss clip whose window overlaps one is not cut. At most 2 boss clips a replay, steals first. The clip runs from the fight's start (at most 35 s before the capture) minus 3 s to 6 s after the capture, and is named after the hero whose unit killed the boss; a boss taken from the team that killed it names the capturing team (`Red team - boss steal - …`) and its entry names no hero. Boss clips never make the replay Notable.

- **Only what the camera showed.** A clip is cut from the recording, and the recording shows the camera, while the analyzers find moments anywhere on the map. So every clip, kill or boss, is checked against the focus timeline the spectator followed for that replay (requests and hero focus included, ClipCamera): a second is on camera when the followed hero is the clip's killer, or within Clips:CameraRadius (15) map units of the boss pit or of the killer. A clip is cut only when at least Clips:CameraShare (0.5) of its moment (first to last blow, or fight start to capture) and one of its last 3 seconds were on camera; otherwise spectate logs Not on camera: ... and skips it, and the next boss fight can take its place. Replay 66406295 (Cursed Hollow, 2026-10-28) is why: Raynor's team stole the boss at 13:25 while the camera followed The Lost Vikings across the map (0 of the fight's 36 s on camera), and the cut clip showed the Vikings.

Each cut starts 12 seconds before the streak and ends 8 seconds after it on the match clock. `clips.json` in that context lists each cut with the hero and the killing blows (`second` and `victim`). The hero name is the English catalog name when the catalog has that hero. Each clip has its own `youtube-entry.json` and can be inserted as its own video. It uses the parent replay's class and the parent replay's one publication slot. The uploader's file watcher tries a clip once, as soon as it is cut. A clip that has to wait then is sent by a later upload pass, which also looks in each context's `clips` folders (`PendingYouTubeUpload.FindClips`, #511). Reasons to wait include the parent's decision not being written yet, the insert cap, or a full publication window. Before #511 such a clip was never tried again, which is how prod's Malthael pentakill (65893649) lost its clip. Each insert still counts toward the quota. Clip titles look like `Li-Ming - pentakill - Alterac Pass - 65550001` , `Kael'thas - triple kill - Cursed Hollow - 65550002` or `Red team - boss steal - Sky Temple - 65550003`, with the English map name; the tags are the kind, the hero and the map.

After a successful upload, retention deletes the mp4 on the next sweep, which the uploader runs right after the insert. The video may still be waiting for its `publishAt`. The context folder itself lasts `VideoKeepDays` (3 in production), and an entry that waits for `publishAt` no longer keeps it.

A recording retired before upload (#489) is handled like this:
- **Retiring it.** The uploader deletes a recording past the publication window (a Terminal slot), and the spectator's pending-bytes gate clears one the uploader would delete. Either way the entry is renamed `youtube-entry-expired.json`. A recording that delete missed goes on the next sweep. The folder then lasts `VideoKeepDays` from the rename, like an upload.
- **Entries with no recording.** An entry with no video id and no recording left in the folder can never upload, for example from before entries were marked expired. Its folder goes once it has been untouched for `VideoKeepDays`. A recording that still waits for its insert is never removed for its age.
- **Dry runs.** While `YouTube:DryRun` is on, a dry-run context's recording goes after `VideoMaxAgeDays` (7), then its folder.
- **The Contexts root.** OBS writes an mp4 into the Contexts root itself when the replay's record directory could not be set. Such a file goes once quiet for `Retention:UnpublishedGrace`, with a `contexts-root` warning. The spectator no longer starts a recording when that directory fails.

A parked recording (#490) is an interrupted upload the uploader will not send again on its own. That happens when its automatic retries are used (`operator-retry`), when YouTube no longer knows its upload session (`session-gone`), or when its replay is already on the channel (`on-channel`).
- **The mark.** The uploader writes `youtube-parked.json` beside the recording, with the reason and when it was first parked. It removes the file as soon as the upload goes on, for example when an operator allows more retries.
- **The pending-bytes gate.** A parked recording is no longer counted towards it, because it waits for an operator, not the uploader.
- **How long it stays.** The housekeeping pass (`parked-recordings`) removes an `on-channel` recording at once, as a duplicate. Others go after `Retention:ParkedKeepDays` (7). Each removal marks the entry expired and keeps the attempt for the record.
- **Viewer requests.** A request is never removed. Past `ParkedKeepDays` it waits for an operator, and `check storage` warns `check.storage.parked_request` with the replay, requester and reward.
- **Where to see them.** `check storage` lists every parked recording: count, size, oldest, and reasons. A send in progress (`UploadPending`, `Uploading`) is never touched, whatever the mark says.

## The library record

Every successful `videos.insert`, full match or clip, appends one line to `Data\youtube-library.jsonl`: the video id, the replay id, `full` or `clip`, the English map, the mode, the rank, the build, the privacy, and the upload time. A full match also keeps `Draft`, the description's `Draft:` note with its composition labels (`Blue no tank, Red double healer, Red dive`), and `FocusHero`, the description's `Featured:` hero. Only a viewer request that named a player (`{replayId},{BattleTag}` on the ReplayId reward, or `spectate file --player Name#1234`) writes `Featured:`. Both keys are left out of the line when the video has neither. The file sits in `Data`, not in a context folder, so retention never deletes it. A later line for the same video wins. A clip is recorded without a mode, a build, a draft note, or a named player.

## The library pass

The uploader process owns every YouTube call. Besides uploads, it runs the library pass in its own loop, independent of the upload passes: `YouTube:LibraryStartupDelay` (2 minutes) after the process starts, whatever the last pass time was, then every `YouTube:LibraryInterval` (1 hour). Before 2026-10-07 the pass ran only after a whole upload pass, so a backlog of recordings (a startup pass that uploaded for an hour) kept it from running at all. The time of the last pass is kept in `Data\youtube-uploads-index.json`; a stop that arrives before a pass no longer stamps it. `heroesreplay youtube library --once` runs the same pass at once. Only one process runs it at a time (`Data\youtube-library.lock`).

Every pass logs one line: `YouTube library pass: ... new channel video(s), ... units.` when it ran, or `YouTube library pass skipped: <why>.` with the reason (not due yet and when it is, another process has it, a quota pause and until when, the day's library units spent and when the room returns, or no consent). The uploader's pass never asks for consent: when no `{ChannelId}-library` token with a refresh token is stored (`%APPDATA%\Google.Apis.Auth`), it is skipped with a warning, because the sign-in broker would open a browser on the stream PC and wait forever. Grant it once by running `heroesreplay youtube library --once` at that machine. Until then no scheduled upload is confirmed public, `published-*` stay 0, and `youtube.not_publishing` names the missing consent.

A pass does three things.

1. **List the channel.** `channels.list` once for the uploads playlist id (1 unit), then `playlistItems.list` with `snippet,status`, 50 videos and 1 unit per page, newest first. Every replay id found goes into `Data\youtube-replay-ids.txt`. A video in the record whose privacy changed (a scheduled upload that went public) gets a new line. Until a listing has once reached the last page, every page is read (7 units for 338 videos). After that a listing stops at the first page with no new video, so a pass usually costs 1 or 2 units. When the pass learns to read a new fact (`YouTubeVideoFacts.Version`, now 2 for the draft note and the named player), the next listing reads every page once more, and a video already in the record gets a new line with the draft note or named player its description shows. Nothing the record already has is replaced.
2. **Backfill.** A channel video missing from the record is read from its title and description. The current template has `Map:`, `Mode:`, `Rank:`, and `Build:` lines, plus `Draft:` and `Featured:` when they apply. Older uploads (`Sky Temple - 65269475 - Platinum` with only `Game type:` and `Rank:` lines) have no build, and a few have a localized map name. Whatever is missing comes from Heroes Profile by replay id, with the rank looked up from player MMR only when a Storm League video has none. At most `YouTube:LibraryLookupsPerPass` (50) lookups run per pass. A video that resolves is recorded and never looked up again. One that does not stays in the index as unresolved and is tried again 6 hours later, then 12, 24, and so on up to every 7 days. An inserted clip is resolved the same way to get its build.
3. **File.** The record's resolved public videos, plus any public `youtube-entry-uploaded.json` still under `Data\Contexts` that the record does not have, are planned with the playlist groups below. The newest upload is planned first, so a new video is filed before the backlog of older ones. Each video id is filed once per playlist (`Data\youtube-playlists.json`). Nothing is removed from a playlist. Filing runs in this order:
   1. **Look up.** When a planned title is not in `youtube-playlists.json`, the channel's playlists are listed once (`playlists.list`, `mine`, 1 unit per 50) before anything is created. A playlist already on the channel under that title is used, even when the cache never heard of it (a run whose cache write was lost, or one made by hand) and even when its case or spacing differs (`storm league -  Platinum`). So the pass never makes a second playlist of the same name. If the channel already has two playlists of one title, the pass logs `The YouTube channel has 2 playlists titled ... The library pass files into <id>; delete the others in YouTube Studio.`
   2. **Existing playlists first.** Every planned item whose playlist exists is filed (`playlistItems.insert`, 50 units), newest video first. A backlog is never held behind a playlist that still has to be created.
   3. **New playlists.** Then at most `YouTube:LibraryMaxNewPlaylistsPerPass` (3) playlists are created (`playlists.insert`, 50 units), each followed by its items. The playlist the most planned items wait for goes first; on a tie, the one holding the newest video. That way each create, the scarce call, files as many videos as it can. The rest wait for later passes: `<n> playlist insert(s) wait for <m> new playlist(s) past YouTube:LibraryMaxNewPlaylistsPerPass (3); later passes create them: Storm League - Diamond (12); Unusual drafts (3); ...`. 0 creates none and only files existing playlists.

   Every playlist write waits `YouTube:LibraryWriteSpacing` (5 s) after the one before, because YouTube throttles writes sent back to back. Three failures in a row stop the filing for that pass.

   YouTube also limits how many playlists a channel creates in a period, whatever quota is left (production on 2026-10-08: a manual pass created playlists and filed about 40 videos, and the next pass's first `playlists.insert` came back 429 `RATE_LIMIT_EXCEEDED` with 2055 of 8000 library units spent). A create refused that way stops the creates for this pass only. Nothing is cached for that title, and its items stay planned, so the next pass tries again. Filing into existing playlists is a different call and goes on. The pass logs one warning for all of them: `YouTube throttled playlist creation (playlists.insert): <error> <n> playlist insert(s) wait for <m> new playlist(s): <title> (<items>); ... Filing into existing playlists went on (<k> filed this pass). The next pass tries the creates again in 01:00:00.` A refused `playlistItems.insert` (429 or `rateLimitExceeded`) still ends the whole pass with `YouTube rate-limited the library pass ...`, and a daily quota response on any call still pauses the pass until the next Pacific quota day.

A scheduled upload is filed once it is public, not at insert. The insert appends it to the record as private. The first pass after its `publishAt` lists it public, appends a new line, and files it, so it lands on its playlists within `LibraryInterval` (1 hour) of going public. The pass also looks up every recorded video that is not public yet and whose `publishAt` passed within the last 30 days by id (`videos.list?part=status`, 50 ids and 1 unit per call), so it is filed wherever it sits in the uploads listing (#154). Filing at insert is not possible with the current code: the upload consent is the `youtube.upload` scope, which cannot call `playlistItems.insert`, and the planner files only public videos so a playlist never lists a video viewers cannot open yet.

Upload OAuth is the `youtube.upload` scope. The library pass (listing, playlist create, and insert) uses a separate consent, the full `youtube` scope, stored for `{ChannelId}-library`. That consent also lists private and scheduled uploads. Channel id in the base file is `UCpf5rn5UlJTUZF9n98HXS5A`.

`YouTube:DryRun` true never calls YouTube or Heroes Profile. It writes `Data\youtube-library-dry-run.json` with the playlist inserts it would make from the record and the contexts (`Items`), the video count per playlist (`Playlists`), their units (`InsertUnits`, 50 each, playlist creates not counted), the unresolved videos it would look up, and the day's units.

Renaming or retitling published videos is not part of the pass. That stays in `tools/youtube-fix-descriptions.cs`.

## Playlists

`YouTube:Playlists` lists the groups, one switch each. A playlist is created public the first time a video needs it, at most `YouTube:LibraryMaxNewPlaylistsPerPass` (3) per pass (see The library pass). Its title is its key: the same facts always give the same title, at most 150 characters (YouTube's limit). A video goes into each playlist at most once, even when two groups give the same title.

```json
"Playlists": {
  "Map": true,
  "Mode": true,
  "Rank": true,
  "Draft": true,
  "ViewerReview": true,
  "HeroFocus": true,
  "HeroFocusRole": false,
  "MapMode": false
}
```

| Group | Title | Filed when |
| --- | --- | --- |
| `Map` | `Alterac Pass` | A full match with a filed mode. Every mode shares one playlist per map. The map is the English catalog name. |
| `Mode` | `Storm League`, `Quick Match`, `ARAM`, `Unranked Draft` | A full match in one of those modes. Other modes (brawls, custom) are not filed in any group. |
| `Rank` | `Storm League - Diamond` | A Storm League game with a known league: Grandmaster, Master, Diamond, Platinum, Gold, Silver, or Bronze, without division. Unranked Storm League, Quick Match, and ARAM have no rank playlist. |
| `Draft` | `Unusual drafts` | The description has a `Draft:` note. Every note shares the one playlist: `Blue no tank, Red double healer` and `Red dive` both go into `Unusual drafts`, once each. |
| `ViewerReview` | `Viewer requested reviews` | The description has a `Featured:` hero, which only a request that named a player writes. A paid upload that named no player is not a review. |
| `HeroFocus` | `Hero gameplay` | The cycle's focus-hero video (#433): the description has a `Hero focus:` line. |
| `HeroFocusRole` | `Hero gameplay - Healer` | Off by default. The focus-hero video's role, one playlist per catalog role. |
| `MapMode` | `Alterac Pass - Storm League - Diamond`, `Alterac Pass - Quick Match` | Off by default. The earlier combined playlist. |

Only a public full match in a filed mode is filed. A clip goes into no playlist. There are no patch playlists: the build is still recorded, but nothing is filed by it.

The draft notes are the title's notes (see "What the video contains"): no tank or healer, no healer, double or triple healer, double bruiser without a tank, no tank, double tank, triple bruiser, double support, and no ranged assassin, with counts above three written as a number (`4 healers`), plus the composition labels: split push, siege, dive, poke, double or triple melee assassin, all melee, one ranged, triple sustain, triple specialist, merc control, and glass cannon. Each of those used to have its own `Unusual drafts - <note>` playlist, about twenty in practice, most holding a few videos. One `Unusual drafts` playlist is one insert a game, however many notes it has.

`MapMode` is off because it is one playlist per map and tier: 15 maps times 8 Storm League shapes, plus Quick Match and ARAM, is well over 100 playlists of a few videos each, and every one of them repeats what the map and rank playlists already show. Turning it off stops new inserts. The combined playlists already on the channel keep their videos and are not filled any further. Delete them in YouTube Studio if they are not wanted, or set `MapMode` true to keep them growing.

Old videos. The first template (`Sky Temple - 65269475 - Platinum`, with a `Game type:` line) has no `Draft:` or `Featured:` line, and Heroes Profile does not know about a draft note or a Twitch request. Those videos go into the map, mode, and rank playlists only. A current-template video gets the draft note only when `YouTube:Titles:DraftNotes` was on at upload, and composition labels only from `TemplateVersion` 6 with `YouTube:Titles:Compositions:Enabled` on. The `Draft:` line kept its shape, so the library pass reads both templates the same way.

Cost per video. Each group that applies is one `playlistItems.insert`, 50 units:

| Video | Inserts with the defaults | Units |
| --- | --- | --- |
| Ranked Storm League (map, mode, rank) | 3 | 150 |
| Unranked Storm League, Quick Match, or ARAM (map, mode) | 2 | 100 |
| Plus an unusual draft | +1 | +50 |
| Plus a viewer review | +1 | +50 |
| Plus a focus-hero video | +1 | +50 |
| Clip | 0 | 0 |

On the channel's 200 newest videos (2026-10-22) every one was ranked Storm League, 43% had a draft note and 17% were focus-hero videos, so a video cost about 3.6 inserts, 180 units. Production turns `Mode` off (#507): the `Storm League` playlist held the whole channel, and at the stream's 60 or so games a day 180 units a video is about 10,800 units, more than the pool. Without it a video costs about 130 units, and 72 a day fit in `LibraryUnitsPerDay` 9,500. The `Storm League` playlist on the channel keeps its videos and is not filled any further. `YouTubeQuotaPlan.PlaylistUnitsPerVideo` makes the same estimate from the groups that are on, and the uploader warns at start when `MaxPublicPerDay` videos would cost more than the library pass may spend.

### Retired playlists

The patch playlists (`Patch 2.57`, `Patch 2.55 archive`, `Unknown patch`, and the old `YouTube:SeasonName` title), the `Unusual drafts - <note>` playlists, and the `Hero focus` and `Hero focus - <role>` playlists are no longer filed, and the library pass leaves the ones on the channel alone. `tools/youtube-retire-playlists.cs` cleans the channel up once:

```powershell
heroesreplay services stop
dotnet run tools/youtube-retire-playlists.cs                              # dry run: print the plan
dotnet run tools/youtube-retire-playlists.cs -- --season "Season 2026"    # also retire the season title
dotnet run tools/youtube-retire-playlists.cs -- --apply
```

It renames `Hero focus` to `Hero gameplay`, so its videos stay on it (or deletes it when `Hero gameplay` already exists), and deletes the other retired playlists. Deleting a playlist never deletes its videos. It rewrites `Data\youtube-playlists.json` (backup `youtube-playlists.json.bak`) so the library pass forgets the deleted playlists and does not insert the renamed playlist's videos again. The next passes file the backlog into `Unusual drafts` and `Hero gameplay`, newest first, within the usual units. Each rename or delete is 50 units of the same day, not recorded in `Data\youtube-quota-units.json`, so run it on a day with room.

## Quota units

YouTube gives the project separate daily buckets (Google Cloud Console, project 467500904749, YouTube Data API v3, Quotas). Since 2026-06-01 `videos.insert` and `search.list` have their own:

| Bucket | Per day | What uses it here |
| --- | --- | --- |
| Video Uploads | 100 calls | `videos.insert`, full matches and clips |
| Search Queries | 100 calls | nothing since #127 |
| Queries (the pool) | 10,000 units | list calls 1 (including the `videos.list?part=status` check), `playlistItems.insert` 50, `playlists.insert` 50 |

The uploader process and `youtube library` share one ledger per Pacific quota day in `Data\youtube-quota-units.json`. Each change takes `youtube-quota-units.json.lock`, so two processes cannot both spend the same room. `UploadCalls` counts `videos.insert` calls in the upload bucket and `LibraryUnits` counts pool units. An upload spends nothing from the pool.

An insert is counted when it is sent. A new upload starts only while `UploadCalls` is under `YouTube:DailyUploadCalls` (100) minus `YouTube:UploadCallReserve` (5), and `MaxInsertsPerQuotaDay` still caps it. Otherwise the recording stays on disk until the next Pacific quota day. The library pass reserves each call's units before it makes the call, and it stops when either limit is reached:

- `YouTube:LibraryUnitsPerDay` (3000 in the base file, 9500 in production since #507) for the pass in one day.
- `YouTube:DailyQuotaUnits` (10000) minus `YouTube:QuotaReserveUnits` (500 in the base file and in production since #507) for the whole pool.

A file written before #250 has `UploadUnits` (1600 per insert) instead of `UploadCalls`. Reading it turns those units into calls (9600 is 6 calls) and frees the pool, so the first library pass after the update runs. Newer files keep `UploadUnits` at 0.

A quota response from YouTube during the pass pauses the pass until the next Pacific quota day, and uploads continue. A daily quota response during an upload pauses new uploads until then (`UploadsPausedUntil`), and the pass continues. YouTube checks quota when the upload session is created, so a quota refusal before any session exists means nothing was sent: the attempt goes back to pending and is retried automatically after the quota day turns. An upload that stopped after its session started is ambiguous until the next pass checks its session (see Interrupted uploads).

Backfilling the 338 videos on the channel today (4 already on the current template) costs about 7 units of listing (and 7 more once, for the full re-listing that reads the draft note and named player), 334 Heroes Profile lookups (the older template has no build) spread over 7 passes, and the playlist inserts. Each group that is on adds one insert per video: 338 × 50 = 16,900 units, about 6 days at 3000 units a day. With the defaults a ranked Storm League video is 3 inserts (map, mode, rank), so the backlog is about 1,014 × 50 = 50,700 units, plus 50 for each playlist that does not exist yet (about 25: one per map, mode, and league, plus the unusual drafts, review, and hero gameplay playlists). At production's 8000 units a day that is about 7 days, while new public videos take about 900 units a day of the same room. An insert an earlier pass already made is not made again. To shorten the backlog, turn `Mode` off first: almost every video is Storm League, so the `Storm League` playlist is close to the whole channel. Production turned it off in #507.

## The score

The score is stored on the attempt and written to the log. It is not configurable, and nothing orders the upload queue by it.

| Class | Weight |
| --- | --- |
| Requested | 400000 |
| Notable | 300000 |
| High-skill | 200000 |
| Ordinary | 100000 |

Added to the weight, each capped at 20000: one point per minute newer than 14 days, 100 per pentakill and 60 more when that pentakill wiped the team, 100 per ladder step above the high-skill rank floor, and one point per MMR point above the MMR floor. The replay id and the game time are tie-breaks only.
