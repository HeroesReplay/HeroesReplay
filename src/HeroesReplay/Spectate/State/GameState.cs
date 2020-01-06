namespace HeroesReplay
{
    public enum GameState
    {
        /// <summary>
        /// This is when the replay file is loading and the replay timer has not yet started in the game client
        /// </summary>
        Loading,

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