namespace HeroesReplay.Service.Spectator.Core.Observer
{
    public enum CaptureMethod
    {
        /// <summary>
        /// This best method without needing to hook. 
        /// </summary>
        BitBlt = 1,

        /// <summary>
        /// Stub the entire capture process.
        /// </summary>
        None = 3
    }
}