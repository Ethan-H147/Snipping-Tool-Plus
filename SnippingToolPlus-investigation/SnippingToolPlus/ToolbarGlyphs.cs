using System.Drawing.Drawing2D;

namespace SnippingToolPlus;

internal enum ToolbarGlyph { None, Back, Copy, More, Close }

internal static class ToolbarGlyphs
{
    // A 20-unit stroke grid, scaled to physical pixels without relying on a symbol font.
    internal static void Draw(Graphics graphics, ToolbarGlyph glyph, RectangleF bounds, Color color)
    {
        if (glyph == ToolbarGlyph.None) return;
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 20, bounds.Height / 20);
            using var stroke = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            switch (glyph)
            {
                case ToolbarGlyph.Back:
                    graphics.DrawLine(stroke, 4, 10, 16, 10);
                    graphics.DrawLines(stroke, [new PointF(9, 5), new PointF(4, 10), new PointF(9, 15)]);
                    break;
                case ToolbarGlyph.Close:
                    graphics.DrawLine(stroke, 5, 5, 15, 15);
                    graphics.DrawLine(stroke, 15, 5, 5, 15);
                    break;
                case ToolbarGlyph.More:
                    using (var dot = new Pen(color, 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        foreach (float x in new[] { 4f, 10f, 16f }) graphics.DrawLine(dot, x, 10, x + .05f, 10);
                    break;
                case ToolbarGlyph.Copy:
                    using (var rear = new GraphicsPath())
                    {
                        rear.AddLine(7, 6, 5, 6); rear.AddBezier(5, 6, 3, 6, 3, 7, 3, 8);
                        rear.AddLine(3, 8, 3, 16); rear.AddBezier(3, 16, 3, 18, 4, 18, 5, 18);
                        rear.AddLine(5, 18, 11, 18); rear.AddBezier(11, 18, 13, 18, 13, 17, 13, 16);
                        rear.AddLine(13, 16, 13, 14);
                        graphics.DrawPath(stroke, rear);
                    }
                    using (var front = Theme.RoundedRectangle(new Rectangle(7, 2, 10, 12), 2))
                        graphics.DrawPath(stroke, front);
                    break;
            }
        }
        finally { graphics.Restore(state); }
    }
}
