using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Egocarib.AutoMapMarkers.Compat
{
    /// <summary>
    /// This fork and egocarib's original Auto Map Markers share a settings file, behavior class
    /// names, Harmony patch targets and hotkeys. Running both would double-place markers or fail
    /// on duplicate registrations, so when the original is loaded this fork stands down entirely.
    /// </summary>
    public static class UpstreamModGuard
    {
        private static readonly string[] UpstreamModIds = { "egocaribautomapmarkers", "egocaribresinmapmarkers" };

        public static bool IsUpstreamLoaded(ICoreAPI api)
        {
            foreach (string modId in UpstreamModIds)
            {
                if (api.ModLoader.IsModEnabled(modId))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Loads only when the original mod is present, to tell the player why this fork is inactive.
    /// </summary>
    public class UpstreamConflictNotice : ModSystem
    {
        public override bool ShouldLoad(ICoreAPI api)
        {
            return api.Side == EnumAppSide.Client && UpstreamModGuard.IsUpstreamLoaded(api);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.Event.LevelFinalize += () =>
            {
                // Delay so the message lands after the join spam instead of scrolling away under it
                api.Event.RegisterCallback(_ => api.ShowChatMessage(Lang.Get("egocarib-mapmarkers:upstream-conflict")), 3000);
            };
        }
    }
}
