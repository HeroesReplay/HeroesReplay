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
  "QuotaReserveUnits": 1000,
  "LibraryUnitsPerDay": 8000
},
"Disk": {
  "WarnWhenPendingBytesAtLeast": 42949672960,
  "StopWhenPendingBytesAtLeast": 85899345920
},
"ReplayMedia": {
  "Version": "1",
  "RecordingMode": "Selected",
  "PublicationMode": "AllEligible",
  "MaxPublicPerDay": 12,
  "MaxPublicPerWeek": 84,
  "MinimumPublicInterval": "02:00:00",
  "MaxPublishAhead": "3.00:00:00",
  "OrdinaryCandidateMaxAge": "3.00:00:00",
  "MapCooldown": "08:00:00",
  "RankCooldown": "08:00:00",
  "FeaturedHeroCooldown": "08:00:00",
  "MaxSharedHeroes": 4,
  "ReservedRequestSlotsPerDay": 2,
  "MaxInsertsPerQuotaDay": 20,
  "CapRecordingToPublication": true
}
```

`OrdinaryCandidateMaxAge` of `3.00:00:00` is 72 hours. `72:00:00` is 72 days, because that is how .NET reads a time span. `MaxPublishAhead` of `3.00:00:00` is 3 days (the code default stays 14). The library pass checks a scheduled video by id once its publish time has passed (#154), so how far back it sits in the uploads listing does not matter.

The uploader logs the effective quota settings once when it starts (`YouTube quota: uploads 100 videos.insert calls a day ...`), and one warning for each way the settings cannot all hold: `MaxInsertsPerQuotaDay` above the usable upload calls, `LibraryUnitsPerDay` above the pool, no pool left after the reserve, a library budget too small to file a day of public videos into playlists, request room that takes the whole day, or a week cap below the day cap.

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
| `cap-upload-backlog` | no | The recordings waiting for upload are at least one day of upload calls: `min(DailyUploadCalls - UploadCallReserve, MaxInsertsPerQuotaDay)`. |
| `cap-room` | yes | A private listing, or the window has a free slot: waiting plus scheduled is under what it holds. |
| `cap-queued` | yes | The window is full, and the slots that must open first (waiting plus scheduled plus this one, less what the window holds), at the window's daily pace, still leave a day before the replay expires. |
| `cap-publication-full` | no | The window is full, and this replay would wait until less than a day before it expires, or its game time is unknown. |

With the production settings the window holds 10 a day over 3 days, so 30 slots, and a full window opens about one slot every 2.4 hours. On 2026-10-08 the stream PC had 10 recordings waiting for upload and 29 uploads scheduled (#370). Before #370 the cap stopped at 30 in flight, so it recorded nothing for about a day, although a replay played 6 hours earlier would have waited one day and still had 1.75 days of its 3 left. Now that replay is recorded (`cap-queued`). One played 2 days earlier is not. In steady state about 17 fresh recordings wait (about 37 GB), and a video goes public about 4 to 5 days after its game. Without the queue only about one waited, so a slot whose map, rank, or hero cooldown ruled out that one recording went unused. The publication pace is the same either way.

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
| `Curated` | Written for a request, and for notable and high-skill. A full day does not drop them: the uploader schedules them for a later slot (#136). Only with `MaxPublishAhead` 0 are they written just while fewer than `MaxPublicPerDay` videos were observed public in the last 24 hours. An ordinary replay stays local. |
| `AllEligible` | Written for every class that is still inside its age, including when the day is already full. The uploader schedules it for a later day. |

The day count at this step is `Data`'s publication ledger of videos already observed public. The send step below uses its own reservation file, and that file is what spaces the publish times.

## When a video is sent

The uploader watches `Data\Contexts` for an mp4. It also retries pending files every 5 minutes. A pass sends a viewer request first (`Requested` in the entry), then the oldest recording first. A request never waits behind ordinary recordings for the quota: an ordinary upload leaves one of the day's remaining inserts for each request still waiting for its insert (#161). A dry-run process does not retry on that timer. It writes `youtube-dry-run.json` and does not call YouTube.

A recording stays on disk only for the YouTube quota or for the media rules. These lines decide whether it is sent now. The first refusal wins.

1. Configuration is valid.
2. The replay is not already published, incomplete, or uncorrelated.
3. The mode allows this class. `Disabled` refuses everyone. `RequestedOnly` refuses anything except a request. `Curated` refuses ordinary. `AllEligible` allows every class.
4. The upload bucket has room. `Data\youtube-quota-units.json` counts `videos.insert` calls today, and they stay under `YouTube:DailyUploadCalls` (100) less `YouTube:UploadCallReserve` (5). Library spend is a different bucket and never holds an upload. No quota response from an upload paused uploads, and `videos.insert` calls today are under `MaxInsertsPerQuotaDay`. The quota day starts at midnight Pacific. A held upload logs when uploads resume: the pause a quota response set, or the next Pacific midnight. `MaxInsertsPerQuotaDay` (20 in production) is this app's own cap, not YouTube's bucket, and its reason is `insert-cap` (it was `quota`, which read like YouTube's quota). When the bucket or the insert cap is closed, the pass does not open each recording: it logs one line (`N recording(s) wait: the daily insert cap is reached (20 of ReplayMedia:MaxInsertsPerQuotaDay 20 ...). Uploads resume at ...`) when the gate closes, and only an interrupted upload (below) still goes on, because resuming its session spends no new insert.
5. When `YouTube:PrivacyStatus` is public, an ordinary replay's game time is inside `OrdinaryCandidateMaxAge`. A request, a notable replay, and a high-skill replay do not use this age at send time. They already expired by their own windows above. An ordinary replay that is too old is not retried, and its recording is deleted with a warning (`Removed recording that was eligible but never uploaded ... stale`). The recording cap exists so this does not happen.
6. When `YouTube:PrivacyStatus` is public, a publish time inside `MaxPublishAhead` (14 days) keeps every rule below. With none, the reason is `publication-full` (it was `horizon`): every time in that window breaks a pacing rule for this replay, and the recording waits for a later pass. Another replay with a different map, rank, or heroes can still find a time in the same window.

A paid request is not paced. It publishes as soon as it is uploaded, whatever the rules below say. The rules below no longer hold an ordinary, notable, or high-skill recording back. They choose its publish time: the earliest time from now that keeps all of them. Each rule looks both ways, at videos already public and at slots already scheduled, so a later replay can take a free time between two earlier ones.

- Week. No rolling 7 days holds more than `MaxPublicPerWeek` videos.
- Day. No rolling 24 hours holds more than `MaxPublicPerDay` videos.
- Reserved request room. With 6 and 2, a non-request may not join a rolling 24 hours that already holds 4 videos, requests included, so the last 2 stay free for requests.
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

- The resumable session URI is saved on the attempt before the first byte is sent (`InitiateSessionAsync`, then the media), so every interrupted send can be checked. YouTube creates the video only when it has every byte, so an incomplete session has no video.
- On a stop the send ends at the next chunk boundary (10 MB chunks), so YouTube keeps every byte sent; a chunk that has not finished after 10 seconds (`YouTubeUploader.StopGrace`) is cut off, inside the 20 seconds `services stop` waits. The log says `Upload of ... paused for the service stop after N of M bytes. YouTube keeps the upload session; the next start resumes it.`
- The next pass asks the saved session how much YouTube holds (`PUT` with `Content-Range: bytes */size`, no quota): a finished session (the send was cut off after its last chunk, before the response) is recorded with its video id and never inserted again; an incomplete one is resumed from YouTube's offset while its publish time is still ahead. When its publish time has passed, the slot is rescheduled (above): if the new time is now the session is resumed, otherwise the old session is dropped (it has no video) and a new insert carries the new time. A session YouTube no longer knows (404 or 410) may have finished, so it is left for an operator. A failed status query is asked again next pass.
- An attempt with no saved session never sent any media, so it is started again, unless its replay id is already in `youtube-replay-ids.txt`, in which case it is left for an operator.
- Each retry logs `Upload of ... was interrupted. Automatic retry N of M: ...`. `YouTube:InterruptedUploadRetries` (5) bounds them per attempt, counted in `Data\youtube-upload-retries.json` across restarts. After that the recording logs once that it waits for an operator; removing its attempt's line from that file allows the retries again.

### Publication health

After each pending pass the uploader writes `Data\publication-status.txt` and logs `YouTube publication health pending ... uploaded ... deferred ... published-day ... published-week ... scheduled ... stuck-private ... limit ... policy .... Waiting: insert-cap 17, publication-full 3.` at most every 5 minutes (and after a pass that sent something). `deferred` is the number of recordings waiting now, and `limit` names what holds most of them: `upload-bucket`, `insert-cap`, `publication-full`, or `none`. A waiting recording logs its own line once, when it starts waiting and when its reason changes, not on every pass. A recording the pass finds already has its entry, so it is not watched for 10 seconds before its turn; only a new mp4 from the watcher waits until its size holds still. The published, scheduled, and stuck counts come from `Data\youtube-library.jsonl`, which the library pass updates when YouTube reports a scheduled video public (#250):

- `published-day` and `published-week`: videos reported public whose publish time is in the last 24 hours or 7 days.
- `scheduled`: uploads not public yet whose publish time is ahead, or passed less than two library passes ago (at least 2 hours).
- `stuck-private`: uploads still not public later than that, within the last 30 days.

Before #250, `published-*` counted only inserts whose response was already public, which a scheduled upload never is, so it stayed 0. `stuck-private` added one for every scheduled upload and never went down, so it grew by 6 a day while the videos did go public on schedule.

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

A recording is uploaded as soon as the upload bucket and a publish slot allow. YouTube's Video Uploads bucket is 100 calls a day; 5 are held back, and production's `MaxInsertsPerQuotaDay` (20) caps it lower. Full matches and clips share it, and library spend never takes from it. Before #250 the app charged each insert 1600 units from the 10,000-unit pool, so it stopped at 6 uploads a day and starved the library pass, while Google's console showed 6 of 100 upload calls and 4 of 10,000 pool units used.

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
| 7 | 13:00 | next day 14:00 | reserved |
| 8 | 13:30 | next day 16:00 | reserved |

A paid request that ends at 14:00 that day publishes at 14:00, as soon as it is uploaded. A ninth ordinary game publishes on the third day at 10:00. Every recording goes on the retention sweep that follows its upload; none waits on disk for the quota. With `MaxPublishAhead` 3 days and 4 non-request videos a day, the window holds 12. Once it is full, the recording cap records an ordinary game only while the recordings ahead of it drain, at 4 a day, at least a day before the game is 3 days old.

## What the video contains

A full match title is one line of at most 100 characters. It does not name a pentakill, a team wipe, a date, or an MMR. Those events are not a title, because one streak does not describe the match.

- Ordinary: `Volskaya Foundry - Storm League - Diamond - 65389750`.
- A reward that names a player: `Illidan focus - Dragon Shire - Storm League - Diamond 3 - 65550001`. The title never names the Twitch viewer.
- A paid upload that does not name a player has the ordinary title. The requestor is credited in the description only.
- A new hero in the match: `Ft. Xal'atath - Volskaya Foundry - Storm League - Diamond - 65389750`. Ft. means featuring. One hero only. When the reward already leads with that hero, the title does not say her twice.

There is no parsed MVP hero, so a title does not say MVP. The heroes in the title come from the parsed replay, in the catalog's English name: the hero unit the game spawned for each player (`HeroDemonHunter` is Valla), then the lobby hero attribute outside ARAM, then the character name. In ARAM the lobby attribute is the hero the player had selected before the game assigned one, so it is never used there (#348).

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
- **Playlists.** Each label is a draft note to the library pass: `Unusual drafts - Dive`.

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

The description starts with `Twitch: https://twitch.tv/saltysadism`, then the Heroes Profile attribution when a statistics hook was used, `Full match.` when the recording completed, the replay id, the Heroes Profile match link, date, build, map, mode, rank, the featured hero when one was named, the draft note with its composition labels, `Featuring:` when a new hero is in the title, the `Stats:` line of a hook, the pentakill or team wipe as a highlight, and the requestor when it was a paid upload. The Blue and Red roster lines name each player without the BattleTag number, because YouTube turns `#1234` into a hashtag. Average MMR is not written. The winner is not included. Category id is `20`. Tags come from the map, mode, rank, hero, those events, and each composition label. The entry records `TemplateVersion` 7 (6 had no statistics hook).

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
   3. **New playlists.** Then at most `YouTube:LibraryMaxNewPlaylistsPerPass` (3) playlists are created (`playlists.insert`, 50 units), each followed by its items. The playlist the most planned items wait for goes first; on a tie, the one holding the newest video. That way each create, the scarce call, files as many videos as it can. The rest wait for later passes: `<n> playlist insert(s) wait for <m> new playlist(s) past YouTube:LibraryMaxNewPlaylistsPerPass (3); later passes create them: Storm League - Diamond (12); Unusual drafts - Dive (3); ...`. 0 creates none and only files existing playlists.

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
  "Patch": true,
  "MapMode": false
}
```

| Group | Title | Filed when |
| --- | --- | --- |
| `Map` | `Alterac Pass` | A full match with a filed mode. Every mode shares one playlist per map. The map is the English catalog name. |
| `Mode` | `Storm League`, `Quick Match`, `ARAM`, `Unranked Draft` | A full match in one of those modes. Other modes (brawls, custom) are not filed in any group except the patch. |
| `Rank` | `Storm League - Diamond` | A Storm League game with a known league: Grandmaster, Master, Diamond, Platinum, Gold, Silver, or Bronze, without division. Unranked Storm League, Quick Match, and ARAM have no rank playlist. |
| `Draft` | `Unusual drafts - Double healer` | The description has a `Draft:` note. One playlist per note, without the team: `Blue no tank, Red double healer` goes into `Unusual drafts - No tank` and `Unusual drafts - Double healer`. A composition label is a note too: `Red dive` goes into `Unusual drafts - Dive`. |
| `ViewerReview` | `Viewer requested reviews` | The description has a `Featured:` hero, which only a request that named a player writes. A paid upload that named no player is not a review. |
| `Patch` | `Patch 2.57`, `YouTube:SeasonName`, `Patch 2.55 archive`, `Unknown patch` | Every public video, clips included. The current line of `Spectate:MinimumGameVersion` uses `SeasonName` when that is set. An older line is an archive. A record video waits for its build. A context entry with no build uses `Unknown patch`. Nothing is deleted when the patch rolls. |
| `MapMode` | `Alterac Pass - Storm League - Diamond`, `Alterac Pass - Quick Match` | Off by default. The earlier combined playlist. |

A clip goes into the patch playlist only. Every other group needs a full match in a filed mode. Only a public video is filed.

The draft notes are the title's notes (see "What the video contains"): no tank or healer, no healer, double or triple healer, double bruiser without a tank, no tank, double tank, triple bruiser, double support, and no ranged assassin, with counts above three written as a number (`4 healers`), plus the composition labels: split push, siege, dive, poke, double or triple melee assassin, all melee, one ranged, triple sustain, triple specialist, merc control, and glass cannon. That is about twenty playlists in practice. One playlist per note, rather than a single `Unusual drafts` list, lets a viewer open "every double healer game". Each distinct note costs one insert. In the issue #140 corpus, half the games had no note, 94 of 295 had one, 43 had two, and 11 had three or four, so about 0.7 inserts a game.

`MapMode` is off because it is one playlist per map and tier: 15 maps times 8 Storm League shapes, plus Quick Match and ARAM, is well over 100 playlists of a few videos each, and every one of them repeats what the map and rank playlists already show. Turning it off stops new inserts. The combined playlists already on the channel keep their videos and are not filled any further. Delete them in YouTube Studio if they are not wanted, or set `MapMode` true to keep them growing.

Old videos. The first template (`Sky Temple - 65269475 - Platinum`, with a `Game type:` line) has no `Draft:` or `Featured:` line, and Heroes Profile does not know about a draft note or a Twitch request. Those videos go into the map, mode, rank, and patch playlists only. A current-template video gets the draft note only when `YouTube:Titles:DraftNotes` was on at upload, and composition labels only from `TemplateVersion` 6 with `YouTube:Titles:Compositions:Enabled` on. The `Draft:` line kept its shape, so the library pass reads both templates the same way.

Cost per video. Each group that applies is one `playlistItems.insert`, 50 units:

| Video | Inserts with the defaults | Units |
| --- | --- | --- |
| Ranked Storm League (map, mode, rank, patch) | 4 | 200 |
| Unranked Storm League, Quick Match, or ARAM (map, mode, patch) | 3 | 150 |
| Plus an unusual draft | +1 per note (usually 1 or 2, at most 4 in the corpus) | +50 to +200 |
| Plus a viewer review | +1 | +50 |
| Clip (patch) | 1 | 50 |

The earlier rules cost 2 inserts (100 units) per full match.

## Quota units

YouTube gives the project separate daily buckets (Google Cloud Console, project 467500904749, YouTube Data API v3, Quotas). Since 2026-06-01 `videos.insert` and `search.list` have their own:

| Bucket | Per day | What uses it here |
| --- | --- | --- |
| Video Uploads | 100 calls | `videos.insert`, full matches and clips |
| Search Queries | 100 calls | nothing since #127 |
| Queries (the pool) | 10,000 units | list calls 1 (including the `videos.list?part=status` check), `playlistItems.insert` 50, `playlists.insert` 50 |

The uploader process and `youtube library` share one ledger per Pacific quota day in `Data\youtube-quota-units.json`. Each change takes `youtube-quota-units.json.lock`, so two processes cannot both spend the same room. `UploadCalls` counts `videos.insert` calls in the upload bucket and `LibraryUnits` counts pool units. An upload spends nothing from the pool.

An insert is counted when it is sent. A new upload starts only while `UploadCalls` is under `YouTube:DailyUploadCalls` (100) minus `YouTube:UploadCallReserve` (5), and `MaxInsertsPerQuotaDay` still caps it. Otherwise the recording stays on disk until the next Pacific quota day. The library pass reserves each call's units before it makes the call, and it stops when either limit is reached:

- `YouTube:LibraryUnitsPerDay` (3000 in the base file, 8000 in production) for the pass in one day.
- `YouTube:DailyQuotaUnits` (10000) minus `YouTube:QuotaReserveUnits` (500 in the base file, 1000 in production) for the whole pool.

A file written before #250 has `UploadUnits` (1600 per insert) instead of `UploadCalls`. Reading it turns those units into calls (9600 is 6 calls) and frees the pool, so the first library pass after the update runs. Newer files keep `UploadUnits` at 0.

A quota response from YouTube during the pass pauses the pass until the next Pacific quota day, and uploads continue. A daily quota response during an upload pauses new uploads until then (`UploadsPausedUntil`), and the pass continues. YouTube checks quota when the upload session is created, so a quota refusal before any session exists means nothing was sent: the attempt goes back to pending and is retried automatically after the quota day turns. An upload that stopped after its session started is ambiguous until the next pass checks its session (see Interrupted uploads).

Backfilling the 338 videos on the channel today (4 already on the current template) costs about 7 units of listing (and 7 more once, for the full re-listing that reads the draft note and named player), 334 Heroes Profile lookups (the older template has no build) spread over 7 passes, and the playlist inserts. Each group that is on adds one insert per video: 338 × 50 = 16,900 units, about 6 days at 3000 units a day. With the defaults a ranked Storm League video is 4 inserts (map, mode, rank, patch), so the backlog is about 1,352 × 50 = 67,600 units, plus 50 for each playlist that does not exist yet (about 30: one per map, mode, league, patch line, draft note, and the review playlist). At production's 8000 units a day that is about 9 days, while new public videos take about 1,200 units a day of the same room. An insert an earlier pass already made (the patch playlist) is not made again. To shorten the backlog, turn `Mode` off first: almost every video is Storm League, so the `Storm League` playlist is close to the whole channel.

## The score

The score is stored on the attempt and written to the log. It is not configurable, and nothing orders the upload queue by it.

| Class | Weight |
| --- | --- |
| Requested | 400000 |
| Notable | 300000 |
| High-skill | 200000 |
| Ordinary | 100000 |

Added to the weight, each capped at 20000: one point per minute newer than 14 days, 100 per pentakill, 60 per team wipe, 100 per ladder step above the high-skill rank floor, and one point per MMR point above the MMR floor. The replay id and the game time are tie-breaks only.
