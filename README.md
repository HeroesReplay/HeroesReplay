# Heroes Replay

Heroes of the Storm Replay

## Alpha Stage

This project is in the early phase. Many things have not been worked out yet.

## Goals of the project

- Develop game state detection for `loading`, `playing`, `paused`, `ended` states.
- Add Twitch chat integration for several features (voting, game controls)
- Improve the existing spectate logic
- Intigration of [HotsApi](http://hotsapi.net/) for limitless replays
- Host a 24/7 stream similar to [SaltyTeemo](https://www.twitch.tv/saltyteemo)

## About

Heroes Replay is a project that automates playing and spectating `.StormReplay` files using the Heroes of the Storm game client. 
It uses the library [Heroes.ReplayParser](https://github.com/barrett777/Heroes.ReplayParser) to parse the replay file in order to determine what should be on focus during the replay.

## Features

- Focuses on heroes that will die or kill an enemy hero
- Focuses on heroes doing objectives
- Focuses on heroes destroying structures

- Selects the talent tree panel when a team has just recieved new talents
- Selects the objective panel when an objective has been won
- Selects the Kill Death Assists panel when a hero dies
- Cycles through all panels throughout the game

## Contributing

1. Fork it!
2. Create your feature branch: `git checkout -b my-new-feature`
3. Commit your changes: `git commit -am 'Add some feature'`
4. Push to the branch: `git push origin my-new-feature`
5. Submit a pull request :D

## License
 
The MIT License (MIT)

Copyright (c) 2015 Chris Kibble

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.