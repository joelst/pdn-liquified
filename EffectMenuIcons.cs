using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DistortionPlugins;

internal static class EffectMenuIcons
{
    internal static Image Liquified { get; } = Create(0);
    internal static Image GridWarp { get; } = Create(1);
    internal static Image DocumentRectify { get; } = Create(2);

    private static Bitmap Create(int kind)
    {
        var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = new Pen(Color.FromArgb(30, 60, 95), 4.5f) { LineJoin = LineJoin.Round };
        using var accent = new Pen(Color.FromArgb(85, 185, 240), 2.5f) { LineJoin = LineJoin.Round };
        void Draw(Action<Pen> draw)
        {
            draw(outline);
            draw(accent);
        }
        if (kind == 0)
        {
            for (int row = 0; row < 3; row++)
            {
                int y = 7 + row * 9;
                Draw(pen => graphics.DrawBezier(pen, 4, y, 12, y - 8, 20, y + 8, 28, y));
            }
        }
        else if (kind == 1)
        {
            Draw(pen => graphics.DrawPolygon(pen, new[] { new Point(5, 5), new Point(27, 5), new Point(25, 27), new Point(5, 27) }));
            Draw(pen => graphics.DrawLines(pen, new[] { new Point(16, 5), new Point(19, 15), new Point(16, 27) }));
            Draw(pen => graphics.DrawLines(pen, new[] { new Point(5, 16), new Point(19, 15), new Point(26, 16) }));
        }
        else
        {
            Draw(pen => graphics.DrawPolygon(pen, new[] { new Point(6, 9), new Point(23, 4), new Point(27, 26), new Point(5, 27) }));
            Draw(pen => graphics.DrawLine(pen, 10, 13, 21, 10));
            Draw(pen => graphics.DrawLine(pen, 10, 19, 22, 17));
        }
        return bitmap;
    }
}
