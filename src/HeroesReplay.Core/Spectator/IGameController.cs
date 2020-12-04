using HeroesReplay.Core.Shared;
using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public interface IGameController
	{
		Task<StormReplay> LaunchAsync(StormReplay stormReplay);
		Task<TimeSpan?> TryGetTimerAsync();
		void SendFocus(int player);
		void SendPanel(int panel);
		void KillGame();
	}
}