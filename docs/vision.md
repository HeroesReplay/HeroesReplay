# Vision

HeroesReplay is a 24/7 automated spectator for Heroes of the Storm. It plays relatively recent replays, a few client versions back, and keeps going when a game fails. The live install updates itself between replays. The match clock and the client state tell it where it is, including when a run is stuck or frozen.

The broadcast is [twitch.tv/saltysadism](https://twitch.tv/saltysadism). The stream PC runs the cycle below with ingest on. A development machine uses the same spectate path with streaming off.

## The cycle

These four parts run together. Settings turn pieces on or off. The cycle stays the same.

1. **Spectate.** HeroesReplay plays a `.StormReplay` in the Heroes of the Storm client. It chooses who to watch, sends the spectator keys, and follows the match clock.
2. **Download, spectate, upload.** A downloader saves replays from Heroes Profile. The spectator plays the next one and records it. An uploader publishes the recording. Requested replays, mode filters, recording, privacy, and streaming are configuration on that same loop. The YouTube pick, cadence, and playlists are in [youtube-uploader.md](youtube-uploader.md).
3. **Stream.** OBS shows the automated process on Twitch: the game, the report after the match, and the wait for the next one. Viewers claim channel rewards to ask for a replay or change what plays next.
4. **Launch the client that matches the replay.** The replay version and the installed clients are both known. The latest replays open on the latest client, which signs in through Battle.net. Older supported builds start their own client. A missing build stays queued, and the loop continues.

A folder of local replays plays through the same spectate step, one pass.

## Recent patches

"Relatively recent" means the current main patch and the few client versions still installed beside it.

A main patch is the first two numbers, such as `2.57.*`. Small updates and build fixes on that line are in scope: `2.57.0.98285`, `2.57.0.98304`, and the next `2.57.*` build. They are separate clients. The replay build and the exe build have to be the same. Battle.net Play always starts the newest client, so only the newest replays use that signed-in launch. An older build of the same patch, or an older installed client, is launched on its own exe.

Battle.net removes the previous build's exe when it installs the next one. HeroesReplay keeps a copy of each supported exe it has seen and puts that exe back before opening a replay of that build. A build that was deleted before a copy was kept cannot be spectated. The replay stays queued.

## Knowing when it is working

The spectator can run unattended because it can tell these apart:

- The client is starting, on the home screen, or in the match.
- The match clock is advancing.
- The match has ended.
- The client is the wrong build, the build is not installed, or the window is stuck, blank, or frozen.

The clock comes from the game's memory once it is locked. Screen text is the fallback, and it is also how menus and dialogs are recognised.

A replay that never reaches a healthy match is held or returned to the queue, and the next replay starts. Between replays, the production install can replace itself with the current release and continue.

## What stays in human hands

Battle.net stays logged in. OBS, Twitch, and YouTube are configured once. The choices that change the loop live in settings: which patch is recent enough, which modes play, whether to record, whether to stream, and whether an upload is public. After that, the cycle runs.
