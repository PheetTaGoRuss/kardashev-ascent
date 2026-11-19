using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    public sealed partial class PlanetSimulation
    {
        readonly AnglePresetBook anglePresets = new AnglePresetBook();

        public IReadOnlyList<AnglePreset> AnglePresets => anglePresets.All;

        public AnglePreset SaveAnglePreset(int tileId, int slot, string name, string note)
        {
            if (!TryGetPanelAngles(tileId, slot, out double tilt, out double azimuth))
                return null;
            if (TryGetAdjustOrder(tileId, slot, out double orderedTilt, out double orderedAzimuth))
            {
                tilt = orderedTilt;       // a layout saves the angles the player chose, even if a robot has not turned the panel yet
                azimuth = orderedAzimuth;
            }
            return SaveAnglePresetAt(tileId, slot, name, note, tilt, azimuth);
        }

        public AnglePreset SaveAnglePresetAt(int tileId, int slot, string name, string note, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId) || GetTile(tileId).IsWater)
                return null;
            if (double.IsNaN(tilt) || double.IsNaN(azimuth))
                return null;
            return anglePresets.Add(name, note, tileId, A(tileId, slot),
                                    Sphere.Latitude(tileId) * 180.0 / Math.PI, Sphere.Longitude(tileId) * 180.0 / Math.PI,
                                    tilt, azimuth, TimeHours);
        }

        public bool UpdateAnglePreset(int id, string name, string note) => anglePresets.Update(id, name, note);

        public bool DeleteAnglePreset(int id) => anglePresets.Delete(id);

        public bool ApplyAnglePreset(int id, int tileId, int slot)
        {
            AnglePreset p = anglePresets.Find(id);
            return p != null && OrderPanelAngles(tileId, slot, p.TiltDeg * Math.PI / 180.0, p.FacingDeg * Math.PI / 180.0);
        }
    }
}
