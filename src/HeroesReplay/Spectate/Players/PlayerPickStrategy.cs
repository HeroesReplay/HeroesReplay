namespace HeroesReplay
{
    /// <summary>
    /// An enum to define different picking strategies when multiple deaths/objectives occur in the same timeframe
    /// </summary>
    public enum PlayerPickStrategy
    {
        /// <summary>
        /// From a moment in time, pick a random player
        /// </summary>
        Random,

        /// <summary>
        /// From a moment in time, pick a player with the least amount of kills
        /// </summary>
        LeastKills,

        /// <summary>
        /// From a moment in time, pick a player with the median amount of kills
        /// </summary>
        MedianKills,

        /// <summary>
        /// From a moment in time, pick a player with the most kills
        /// </summary>
        MostKills,

        /// <summary>
        /// From a moment in time, pick a player with the least deaths
        /// </summary>
        LeastDeaths,

        /// <summary>
        /// From a moment in time, pick a player with the median amount of deaths
        /// </summary>
        MedianDeaths,

        /// <summary>
        /// From a moment in time, pick a player with the most deaths
        /// </summary>
        MostDeaths,
    }
}
