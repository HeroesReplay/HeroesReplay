namespace HeroesReplay.Core.Processes
{
    public enum CaptureMethod
    {
        /// <summary>
        /// This best method without needing to hook. 
        /// </summary>
        BitBlt = 1,

        /// <summary>
        /// Copy directly from screen. The least friendly method.
        /// Will cause problems if there are other windows on top.
        /// </summary>
        CopyFromScreen = 2,

        /// <summary>
        /// Stub the entire capture process.
        /// </summary>
        None = 3
    }
}