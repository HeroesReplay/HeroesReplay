using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Obs
{
    public class ObsController : IObsController
    {
        private readonly ILogger<ObsController> logger;
        private readonly Settings settings;

        public ObsController(ILogger<ObsController> logger, Settings settings)
        {
            this.logger = logger;
            this.settings = settings;
        }

        public async Task CycleScenesAsync(int replayId)
        {
            OBSWebsocket obs = new OBSWebsocket();

            // Connect
            var waiter = new ManualResetEventSlim();

            obs.Connected += (sender, e) =>
            {
                waiter.Set();
                logger.LogInformation("OBS Web Socket Connected");
            };

            obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
            waiter.Wait();

            var sceneList = obs.GetSceneList();
            var sourceList = obs.GetSourcesList();

            // INTERLUDE MUSIC
            var interlude = sourceList.Find(x => x.TypeID == "ffmpeg_source");

            if (interlude != null)
            {
                var interludeSourceSettings = obs.GetSourceSettings(interlude.Name);
                JObject interludeSettings = interludeSourceSettings.sourceSettings;

                var interludeTrack = interludeSettings["local_file"].Value<string>();

                if (interludeTrack != settings.OBS.InterludeMusicPath)
                {
                    interludeSettings["local_file"] = settings.OBS.InterludeMusicPath;
                    obs.SetSourceSettings(interlude.Name, interludeSettings);
                    logger.LogInformation($"set interlude music to: {settings.OBS.InterludeMusicPath}");
                }
            }

            // SET THE BROWSER ENDPOINTS
            foreach (var segment in this.settings.OBS.BrowserSources)
            {
                var url = segment.SourceUrl.Replace("[ID]", replayId.ToString());
                var source = sourceList.Find(si => si.Name.Equals(segment.SourceName, System.StringComparison.OrdinalIgnoreCase));

                if (source != null)
                {
                    SourceSettings sourceSettings = obs.GetSourceSettings(source.Name);
                    JObject browserSettings = sourceSettings.sourceSettings;
                    browserSettings["url"] = url;
                    obs.SetSourceSettings(source.Name, browserSettings);
                    logger.LogInformation($"set {segment.SceneName} URL to: {url}");
                    await Task.Delay(250);
                }
            }

            // CYCLE THE SCENES
            foreach (var source in this.settings.OBS.BrowserSources)
            {
                logger.LogInformation($"set scene to: {source.SceneName}");
                obs.SetCurrentScene(source.SceneName);
                await Task.Delay(source.DisplayTime);
            }

            obs.SetCurrentScene(this.settings.OBS.GameSceneName);

            obs.Disconnect();
            logger.LogInformation($"OBS WebSocket Disconnected");
        }
    }
}
