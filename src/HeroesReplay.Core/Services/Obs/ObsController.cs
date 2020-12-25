using HeroesReplay.Core.Shared;

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
        private readonly Settings settings;

        public ObsController(Settings settings)
        {
            this.settings = settings;
        }

        public async Task CycleScenesAsync(int replayId)
        {
            OBSWebsocket obs = new OBSWebsocket();

            // Connect
            var waiter = new ManualResetEventSlim();
            obs.Connected += (sender, e) => waiter.Set();
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

            // SET THE BROWSER ENDPOINTS
            foreach (var segment in this.settings.OBS.BrowserSources)
            {
                var scene = sceneList.Scenes.Find(scene => scene.Name == segment.SceneName);
                var source = sourceList.Find(source => scene.Items.Any(sceneItem => sceneItem.SourceName == source.Name));

                SourceSettings sourceSettings = obs.GetSourceSettings(source.Name);
                JObject browserSettings = sourceSettings.sourceSettings;
                browserSettings["url"] = segment.SourceUrl.Replace("[ID]", replayId.ToString());
                obs.SetSourceSettings(source.Name, browserSettings);
            }

            // CYCLE THE SCENES
            foreach (var source in this.settings.OBS.BrowserSources)
            {
                obs.SetCurrentScene(source.SceneName);
                await Task.Delay(source.DisplayTime);
            }

            obs.SetCurrentScene(this.settings.OBS.GameSceneName);

            obs.Disconnect();
        }
    }
}
