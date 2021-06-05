using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.Crunchyroll
{
    public class Plugin : BasePlugin<PluginConfiguration>
	{
		public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
		{
			Instance = this;
		}
		public override string Name => "Crunchyroll";

		public override string Description => "Stream anime and drama from Crunchyroll.";

		public static Plugin Instance { get; private set; }
	}
}
