# Heroes Profile API

Canonical OpenAPI (do not vendor the 5MB file):

https://raw.githubusercontent.com/Heroes-Profile/heroesprofile/refs/heads/develop/public/spec/heroesprofile-v1.json

Base URL: `https://www.heroesprofile.com/api/external/v1/`  
Auth: `Authorization: Bearer <key>` (same key as the old `api_token`).

## Endpoints we use

| Call | Path |
| --- | --- |
| List / max id | `GET /replays?after={id}&game_type=Storm League` |
| Download | `GET /download/replay?replayID={id}` |
| One match | `GET /replays?after={id-1}` then pick `replayID` |

`region` is an integer (1 NA, 2 EU, 3 KR, 5 CN). List rows have `downloadable` instead of a GCS/S3 `url`. Skip when `downloadable` is false or `deleted` is non-zero.

The legacy host `https://api.heroesprofile.com/api/` (`Replay/Min_id`, `Replay/Max`, `?api_token=`) still exists for old keys; this project talks to v1.
