using System.Linq;

namespace HeroesReplay.Core.HeroesProfile;

public static class ReplayListCursor
{
    public static int? AfterRejectedPage(int currentMin, ReplayListing page)
    {
        if (page == null || !page.HadRows)
        {
            return null;
        }

        if (
            page.Playable != null
            && page.Playable.Any(replay => replay != null && replay.Id > currentMin)
        )
        {
            return null;
        }

        if (page.NextAfter is int after && after > currentMin)
        {
            return after;
        }

        if (page.HighestId > currentMin)
        {
            return page.HighestId;
        }

        return null;
    }
}
