using UnityEngine;

namespace OrbitalForecast
{
    // The "config" half of the mod: defaults loaded from
    // GameData/OrbitalForecast/settings.cfg via KSP's GameDatabase, so the
    // node is ModuleManager-patchable.
    public class ForecastSettings
    {
        public float markerSize = 11f;
        public bool showMoons = false;
        public bool showLines = true;
        public double defaultStepDays = 10.0;
        public Color ghostColor = new Color(0.40f, 0.90f, 1.00f, 1.00f);
        public Color currentColor = new Color(1.00f, 1.00f, 1.00f, 0.50f);
        public Color lineColor = new Color(0.40f, 0.90f, 1.00f, 0.35f);

        public static ForecastSettings Load()
        {
            ForecastSettings s = new ForecastSettings();
            if (GameDatabase.Instance == null) return s;

            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("ORBITAL_FORECAST");
            if (nodes == null || nodes.Length == 0) return s;

            ConfigNode n = nodes[0];
            float f;
            bool b;
            double d;
            if (n.HasValue("markerSize") && float.TryParse(n.GetValue("markerSize"), out f)) s.markerSize = f;
            if (n.HasValue("showMoons") && bool.TryParse(n.GetValue("showMoons"), out b)) s.showMoons = b;
            if (n.HasValue("showLines") && bool.TryParse(n.GetValue("showLines"), out b)) s.showLines = b;
            if (n.HasValue("defaultStepDays") && double.TryParse(n.GetValue("defaultStepDays"), out d)) s.defaultStepDays = d;
            s.ghostColor = ParseColor(n, "ghostColor", s.ghostColor);
            s.currentColor = ParseColor(n, "currentColor", s.currentColor);
            s.lineColor = ParseColor(n, "lineColor", s.lineColor);
            return s;
        }

        // Parses "r,g,b,a" as floats in the 0..1 range.
        static Color ParseColor(ConfigNode n, string key, Color fallback)
        {
            if (!n.HasValue(key)) return fallback;
            string raw = n.GetValue(key);
            string[] parts = raw.Split(',');
            if (parts.Length < 3) return fallback;
            float r, g, bl;
            float a = 1f;
            if (!float.TryParse(parts[0].Trim(), out r)) return fallback;
            if (!float.TryParse(parts[1].Trim(), out g)) return fallback;
            if (!float.TryParse(parts[2].Trim(), out bl)) return fallback;
            if (parts.Length >= 4) float.TryParse(parts[3].Trim(), out a);
            return new Color(r, g, bl, a);
        }
    }
}
