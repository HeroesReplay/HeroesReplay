namespace HeroesReplay.Spectator
{
    public enum CaptureMethod
    {
        /// <summary>
        /// PrintScreen for CPU rendering windows (Battle.net)
        /// </summary>
        PrintScreen,

        /// <summary>
        /// BitBlt for GPU rendering windows (HeroesOfTheStorm_x64)
        /// </summary>
        BitBlt
    }
}