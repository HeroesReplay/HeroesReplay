namespace HeroesReplay
{
    public enum SpectatorState
    {
        /// <summary>
        /// This is when the replay file is loading and the replay timer has not yet started in the game client
        /// </summary>
        Loading = 0,

        /// <summary>
        /// This is when the game has loaded and the pause button has been detected?
        /// </summary>
        StartOfGame,

        /// <summary>
        /// This is when the game has come to an end (with an end screen etc) and the vote button has been detected?
        /// </summary>
        EndOfGame,

        /// <summary>
        /// This is if the game is currently running because the pause button is detected?
        /// </summary>
        Running,

        /// <summary>
        /// This is if the game is currently paused because the play button is detected?
        /// </summary>
        Paused
    }
}