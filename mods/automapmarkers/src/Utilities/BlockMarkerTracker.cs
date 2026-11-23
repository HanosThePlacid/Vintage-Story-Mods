using Egocarib.AutoMapMarkers.Settings;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static Egocarib.AutoMapMarkers.Settings.MapMarkerConfig.Settings;

namespace Egocarib.AutoMapMarkers.Utilities
{
    /// <summary>
    /// Links waypoints created by a block interaction to the block that produced them.
    /// When that block is broken the matching waypoint is deleted, so the next matching
    /// block nearby can be marked without the old marker covering it.
    /// </summary>
    /// <remarks>
    /// Only markers created before a break (interact, sneak/hotkey detection and the
    /// OnGettingBroken pass) are tracked. Markers whose purpose is to record a collected
    /// block - loose ores and scythe harvesting - are deliberately left alone.
    /// </remarks>
    internal static class BlockMarkerTracker
    {
        private sealed class MarkerRecord
        {
            public Vec3d WaypointPosition;
            public string TitlePattern;
        }

        // Waypoint coordinates are the block's integer coordinates, so this radius only
        // matches the marker created for that exact block, not one on a neighbouring block.
        private const double ExactBlockRadius = 0.5d;

        private static readonly Dictionary<BlockPos, MarkerRecord> TrackedMarkers =
            new Dictionary<BlockPos, MarkerRecord>();

        private static readonly object Sync = new object();

        /// <summary>
        /// Remembers the waypoint requested for a block so it can be removed if the block
        /// is broken later. Repeat interactions with the same block simply replace the
        /// existing association.
        /// </summary>
        public static void Track(BlockPos sourceBlockPos, Vec3d waypointPosition,
            AutoMapMarkerSetting settings)
        {
            if (sourceBlockPos == null || waypointPosition == null || settings == null)
                return;

            MapMarkerConfig.Settings config = MapMarkerConfig.GetSettings(MapMarkerMod.CoreAPI);
            if (config == null || !config.RemoveMarkerOnBlockBreak)
                return;

            lock (Sync)
            {
                TrackedMarkers[sourceBlockPos.Copy()] = new MarkerRecord
                {
                    WaypointPosition = waypointPosition.Clone(),
                    TitlePattern = settings.MarkerTitle
                };
            }
        }

        /// <summary>
        /// Client event handler for a completed survival block break. Deletes only the
        /// waypoint associated with the broken position.
        /// </summary>
        public static void HandleBlockBroken(BlockDamage damage)
        {
            if (damage?.Position == null)
                return;

            MapMarkerConfig.Settings config = MapMarkerConfig.GetSettings(MapMarkerMod.CoreAPI);
            if (config == null || !config.RemoveMarkerOnBlockBreak)
                return;

            MarkerRecord record;
            lock (Sync)
            {
                if (!TrackedMarkers.TryGetValue(damage.Position, out record))
                    return;
                TrackedMarkers.Remove(damage.Position);
            }

            MapMarkerMod.Network?.RequestWaypointDeletionAtPositionFromServer(
                position: record.WaypointPosition,
                sendChatMessage: config.ChatNotifyOnWaypointDeletion,
                titlePattern: record.TitlePattern,
                maxRadius: ExactBlockRadius);
        }

        /// <summary>
        /// Drops all associations, e.g. when leaving a world.
        /// </summary>
        public static void Reset()
        {
            lock (Sync)
            {
                TrackedMarkers.Clear();
            }
        }
    }
}
