namespace HeroesReplay.Core.Models
{
    public enum State
    {
        /// <summary>
        /// When a replay is currently loading there is no timer. 
        /// Usually during the map loading screen or ARAM hero select screen
        /// </summary>
        Loading,

        /// <summary>
        /// When the UI Timer with the Negative offset is detected at the top.
        /// This means the replay is loaded, running and we can map focus heroes to the timer.
        /// </summary>
        TimerDetected,

        /// <summary>
        /// No timer has been detected and this state comes after the Timer state.
        /// This state should only be set once the Timer state has been set
        /// </summary>
        EndDetected
    }
}