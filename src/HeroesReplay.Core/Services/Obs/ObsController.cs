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
            var interludeSourceSettings = obs.GetSourceSettings(interlude.Name);
            JObject interludeSettings = interludeSourceSettings.sourceSettings;
            interludeSettings["local_file"] = settings.OBS.InterludeMusicPath;
            obs.SetSourceSettings(interlude.Name, interludeSettings);
            logger.LogInformation($"set interlude music to: {interludeSettings["local_file"].Value<string>()}");

            // SET THE BROWSER ENDPOINTS
            foreach (var segment in this.settings.OBS.BrowserSources)
            { 
                var url = segment.SourceUrl.Replace("[ID]", replayId.ToString());
                var scene = sceneList.Scenes.Find(scene => scene.Name == segment.SceneName);
                var source = sourceList.Find(source => scene.Items.Any(sceneItem => sceneItem.SourceName == source.Name));

                SourceSettings sourceSettings = obs.GetSourceSettings(source.Name);
                JObject browserSettings = sourceSettings.sourceSettings;
                browserSettings["url"] = url;
                obs.SetSourceSettings(source.Name, browserSettings);
                logger.LogInformation($"set {segment.SceneName} URL to: {url}");
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
