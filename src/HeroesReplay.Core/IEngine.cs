using System.Threading.Tasks;

namespace HeroesReplay.Core;

public interface IEngine
{
    /// <summary>False when an unexpected error ended the engine. A requested stop is true.</summary>
    Task<bool> RunAsync();
}
