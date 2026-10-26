using UnityEngine;

namespace OrbitalForecast
{
    // Lightweight IMGUI drawing helpers (dots + lines) so we don't need custom
    // shaders/materials to render overlay markers in the map view.
    public static class DrawUtils
    {
        static Texture2D _white;

        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                }
                return _white;
            }
        }

        public static void Dot(Vector2 center, float size, Color col)
        {
            Color old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), White);
            GUI.color = old;
        }

        public static void Line(Vector2 a, Vector2 b, float width, Color col)
        {
            Vector2 delta = b - a;
            float len = delta.magnitude;
            if (len < 0.01f) return;

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            Color oldColor = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;

            GUI.color = col;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), White);

            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }
    }
}
