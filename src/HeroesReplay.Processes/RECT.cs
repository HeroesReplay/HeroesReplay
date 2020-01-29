using System.Runtime.InteropServices;

namespace HeroesReplay.Processes
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}