using System.Threading.Tasks;

namespace HeroesReplay.Console.AnalyzerReporter.Core
{
    public interface ISpectateReportWriter
    {
        /// <summary>
        /// Output the calculator spectate logic to a CSV file so you can see the spectate logic for an entire replay file
        /// without having to watch the replay. This can be useful when adding or modifying existing calculators 
        /// and you already know the replay well enough to know what to expect at certain times.
        /// </summary>
        Task OutputReportAsync();
    }
}
