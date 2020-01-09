using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    public sealed class GameWrapper
    {
        private const string GameProcessName = "HeroesOfTheStorm_x64";
        private const string BNetProcessName = "Battle.net";

        private readonly Key[] KEYS_HEROES = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0 };
        private readonly Key[] KEYS_CONSOLE_PANEL = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8 };
        private readonly IConfiguration configuration;
        private readonly ILogger<GameWrapper> logger;

        private Process Game => Process.GetProcessesByName(GameProcessName)[0];
        private Process Launcher => Process.GetProcessesByName(BNetProcessName)[0];

        private IntPtr GameWindowHandle => Game.MainWindowHandle;

        public const int WM_KEYDOWN = 0x100;
        public const int WM_KEYUP = 0x101;
        public const int WM_CHAR = 0x102;

        public const int SRCCOPY = 0x00CC0020;
        public const int CAPTUREBLT = 0x40000000;

        public bool IsGameRunning => Process.GetProcessesByName(GameProcessName).Any();

        public bool TryKillGame()
        {
            try
            {
                Game.Kill();

                logger.LogInformation("Killed game process at " + DateTime.Now);

                return true;
            }
            catch (Exception)
            {

            }

            return false;
        }

        public void AddReplayInterfaceSetting()
        {
            // replayinterface=AhliObs 0.66.StormInterface
        }

        public GameWrapper(IConfiguration configuration, ILogger<GameWrapper> logger)
        {
            this.configuration = configuration;
            this.logger = logger;
        }

        public async Task<bool> TryLaunchAsync(Game game, CancellationToken token)
        {
            if (IsGameRunning && TryKillGame())
            {
                await Task.Delay(8000, token); // wait so bnet goes from disabled 'Playing' button to clickable 'Play'  button.
            }

            var battlenetExe = Path.Combine(configuration.GetValue<string>("bnet"), "Battle.net.exe");

            using (var battlenetProcess = Process.Start(battlenetExe, arguments: "--game heroes"))
            {
                battlenetProcess.WaitForExit();

                await Task.Delay(8000, token);

                SendEnterByHandle(Launcher.MainWindowHandle);

                while (!IsGameRunning && !token.IsCancellationRequested)
                {
                    logger.LogInformation("Launching...");
                    await Task.Delay(8000, token);
                }

                if (IsGameRunning)
                {
                    logger.LogInformation("Launched...");
                    await Task.Delay(8000, token);

                    Game.WaitForInputIdle();

                    logger.LogInformation($"Selecting {game.Name}");

                    var switcherExe = Path.Combine(configuration.GetValue<string>("hots"), "Support64", "HeroesSwitcher_x64.exe");

                    using (var switcherProcess = Process.Start(switcherExe, $"\"{game.FilePath}\""))
                    {
                        switcherProcess.WaitForExit();

                        Console.WriteLine($"Selected {game.Name}");

                        Game.WaitForInputIdle();

                        await Task.Delay(8000, token);

                        return true;
                    }
                }
            }

            Console.WriteLine($"Failed to launch {game.FilePath}");

            return false;
        }

        public void SendFocusHero(int index)
        {
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, KEYS_HEROES[index], IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, KEYS_HEROES[index], IntPtr.Zero);
        }

        public void SendTogglePause()
        {
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.P, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_CHAR, Key.P, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.P, IntPtr.Zero);
        }

        public void SendToggleChat()
        {
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_CHAR, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleZoom()
        {
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.Z, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_CHAR, Key.Z, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.Z, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
        }

        public void SendToggleTime()
        {
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_CHAR, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleControls()
        {
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_CHAR, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendPanelChange(GamePanel gamePanel)
        {
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, KEYS_CONSOLE_PANEL[(int)gamePanel], IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, KEYS_CONSOLE_PANEL[(int)gamePanel], IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleBottomConsole()
        {
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_CHAR, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleInfoPanel()
        {
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYDOWN, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_CHAR, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(GameWindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendEnterByHandle(IntPtr handle)
        {
            NativeMethods.SendMessage(handle, WM_KEYDOWN, Key.Return, IntPtr.Zero);
            NativeMethods.SendMessage(handle, WM_KEYUP, Key.Return, IntPtr.Zero);
        }

        public bool TryGetScreenshot(out Bitmap screenshot)
        {
            IntPtr gameWnd = IntPtr.Zero;
            IntPtr gameDc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            screenshot = null;
            bool success = false;

            try
            {
                NativeMethods.GetWindowRect(Game.MainWindowHandle, out RECT region);

                gameWnd = Game.MainWindowHandle;
                gameDc = NativeMethods.GetWindowDC(gameWnd);
                memoryDc = NativeMethods.CreateCompatibleDC(gameDc);

                Rectangle rectangle = Rectangle.FromLTRB(region.Left, region.Top, region.Right, region.Bottom);

                bitmap = NativeMethods.CreateCompatibleBitmap(gameDc, rectangle.Width, rectangle.Height);
                oldBitmap = NativeMethods.SelectObject(memoryDc, bitmap);

                success = NativeMethods.BitBlt(memoryDc, 0, 0, rectangle.Width, rectangle.Height, gameDc, region.Left, region.Top, SRCCOPY | CAPTUREBLT);

                if (success)
                {
                    screenshot = Image.FromHbitmap(bitmap);
                }
            }
            catch (Exception e)
            {
                throw;
            }
            finally
            {
                NativeMethods.SelectObject(memoryDc, oldBitmap);
                NativeMethods.DeleteObject(bitmap);
                NativeMethods.DeleteDC(memoryDc);
                NativeMethods.ReleaseDC(gameWnd, gameDc);
            }

            return success;
        }
    }
}