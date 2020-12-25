using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Obs
{
    public interface IObsController
	{
		Task CycleScenesAsync(int replayId);
    }
}
