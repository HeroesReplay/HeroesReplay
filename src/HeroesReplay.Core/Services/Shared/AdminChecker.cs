using System.Security.Principal;

namespace HeroesReplay.Core.Services.Shared
{
    public class AdminChecker : IAdminChecker
    {
        public bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}