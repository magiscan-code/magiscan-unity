using UnityEngine;

namespace Magiscan.Editor.Qr
{
    /// <summary>Renders a <see cref="QrCode"/> into a crisp, point-filtered <see cref="Texture2D"/>.</summary>
    public static class QrTextureBuilder
    {
        /// <summary>
        /// Builds a texture for <paramref name="qr"/>. A quiet zone (white border) is included, which
        /// scanners require. Modules are rendered as solid blocks of <paramref name="moduleSize"/> pixels.
        /// </summary>
        public static Texture2D Build(QrCode qr, int moduleSize = 8, int quietZone = 4, Color32? dark = null, Color32? light = null)
        {
            if (qr == null) return null;
            if (moduleSize < 1) moduleSize = 1;
            if (quietZone < 0) quietZone = 0;

            Color32 darkColor = dark ?? new Color32(0, 0, 0, 255);
            Color32 lightColor = light ?? new Color32(255, 255, 255, 255);

            int dim = qr.Size + quietZone * 2;
            int px = dim * moduleSize;

            var tex = new Texture2D(px, px, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "MagiscanQR",
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[px * px];
            for (int ty = 0; ty < px; ty++)
            {
                // Texture origin is bottom-left; QR row 0 is the top row, so flip vertically.
                int moduleFromBottom = ty / moduleSize - quietZone;
                int qrY = qr.Size - 1 - moduleFromBottom;
                bool rowInside = moduleFromBottom >= 0 && moduleFromBottom < qr.Size;

                for (int tx = 0; tx < px; tx++)
                {
                    int qrX = tx / moduleSize - quietZone;
                    bool isDark = rowInside && qrX >= 0 && qrX < qr.Size && qr.GetModule(qrX, qrY);
                    pixels[ty * px + tx] = isDark ? darkColor : lightColor;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
