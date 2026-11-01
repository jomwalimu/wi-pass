using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace WiPass.UI.Fluent
{
    internal enum Ico
    {
        None, Check, Plus, Close, Pencil, Trash, Building, Wifi, Pin, Info, Refresh, Eye,
        Lock, Sliders, Gear, Phone, Warning, CircleCheck, Logo, ChevronRight
    }

    /// <summary>
    /// Line icons drawn with GDI+ in a 24-unit box (no icon font needed, same on Windows 10 and 11).
    /// </summary>
    internal static class Icons
    {
        public static void Draw(Graphics g, Ico icon, RectangleF box, Color color, float strokeUnits = 1.7f)
        {
            if (icon == Ico.None) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Math.Min(box.Width, box.Height) / 24f;
            float ox = box.X + (box.Width - 24 * k) / 2f, oy = box.Y + (box.Height - 24 * k) / 2f;
            Func<float, float, PointF> P = (x, y) => new PointF(ox + x * k, oy + y * k);
            Func<float, float, float, RectangleF> C = (cx, cy, r) => new RectangleF(ox + (cx - r) * k, oy + (cy - r) * k, 2 * r * k, 2 * r * k);

            using (var pen = new Pen(color, Math.Max(1f, strokeUnits * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (icon)
                {
                    case Ico.Check:
                        g.DrawLines(pen, new[] { P(5, 12.5f), P(9.5f, 17), P(19, 7.5f) });
                        break;
                    case Ico.CircleCheck:
                        g.DrawEllipse(pen, C(12, 12, 9));
                        g.DrawLines(pen, new[] { P(8, 12.5f), P(10.8f, 15.3f), P(16.5f, 9.5f) });
                        break;
                    case Ico.Plus:
                        g.DrawLine(pen, P(12, 5), P(12, 19));
                        g.DrawLine(pen, P(5, 12), P(19, 12));
                        break;
                    case Ico.Close:
                        g.DrawLine(pen, P(6, 6), P(18, 18));
                        g.DrawLine(pen, P(18, 6), P(6, 18));
                        break;
                    case Ico.ChevronRight:
                        g.DrawLines(pen, new[] { P(9, 5), P(16, 12), P(9, 19) });
                        break;
                    case Ico.Pencil:
                        g.DrawPolygon(pen, new[] { P(4, 20), P(8, 20), P(19, 9), P(15, 5), P(4, 16) });
                        g.DrawLine(pen, P(13.5f, 6.5f), P(17.5f, 10.5f));
                        break;
                    case Ico.Trash:
                        g.DrawLine(pen, P(4, 7), P(20, 7));
                        g.DrawLines(pen, new[] { P(9, 7), P(9, 4), P(15, 4), P(15, 7) });
                        g.DrawLines(pen, new[] { P(6, 7), P(7, 20), P(17, 20), P(18, 7) });
                        break;
                    case Ico.Building:
                        g.DrawLine(pen, P(3, 21), P(21, 21));
                        g.DrawLines(pen, new[] { P(5, 21), P(5, 10), P(12, 5), P(19, 10), P(19, 21) });
                        g.DrawLines(pen, new[] { P(10, 21), P(10, 16), P(14, 16), P(14, 21) });
                        break;
                    case Ico.Wifi:
                        foreach (float r in new[] { 4.5f, 9f, 13.5f })
                            g.DrawArc(pen, C(12, 20, r), 225, 90);
                        g.FillEllipse(brush, C(12, 19.4f, 1.3f));
                        break;
                    case Ico.Pin:
                        using (var path = new GraphicsPath())
                        {
                            path.AddArc(C(12, 10, 6.5f), 150, 240);
                            path.AddLine(P(17.63f, 13.25f), P(12, 21));
                            path.AddLine(P(12, 21), P(6.37f, 13.25f));
                            path.CloseFigure();
                            g.DrawPath(pen, path);
                        }
                        g.DrawEllipse(pen, C(12, 10, 2.3f));
                        break;
                    case Ico.Info:
                        g.DrawEllipse(pen, C(12, 12, 9));
                        g.DrawLine(pen, P(12, 11), P(12, 16.5f));
                        g.FillEllipse(brush, C(12, 7.8f, 1.1f));
                        break;
                    case Ico.Warning:
                        g.DrawPolygon(pen, new[] { P(12, 4), P(21, 20), P(3, 20) });
                        g.DrawLine(pen, P(12, 10), P(12, 14));
                        g.FillEllipse(brush, C(12, 17, 1.1f));
                        break;
                    case Ico.Refresh:
                        g.DrawArc(pen, C(12, 12, 8), -60, 300);
                        g.DrawLines(pen, new[] { P(16, 3.5f), P(16.2f, 7.3f), P(20, 7) });
                        break;
                    case Ico.Eye:
                        g.DrawEllipse(pen, new RectangleF(ox + 2 * k, oy + 6 * k, 20 * k, 12 * k));
                        g.DrawEllipse(pen, C(12, 12, 3));
                        break;
                    case Ico.Lock:
                        using (GraphicsPath body = Fx.Round(new RectangleF(ox + 5 * k, oy + 11 * k, 14 * k, 10 * k), 2 * k))
                            g.DrawPath(pen, body);
                        g.DrawArc(pen, new RectangleF(ox + 8 * k, oy + 4 * k, 8 * k, 10 * k), 180, 180);
                        g.DrawLine(pen, P(8, 9), P(8, 11));
                        g.DrawLine(pen, P(16, 9), P(16, 11));
                        break;
                    case Ico.Sliders:
                        g.DrawLine(pen, P(4, 7), P(14, 7));
                        g.DrawLine(pen, P(18, 7), P(20, 7));
                        g.DrawLine(pen, P(4, 17), P(8, 17));
                        g.DrawLine(pen, P(12, 17), P(20, 17));
                        g.DrawEllipse(pen, C(16, 7, 2));
                        g.DrawEllipse(pen, C(10, 17, 2));
                        break;
                    case Ico.Gear:
                        g.DrawEllipse(pen, C(12, 12, 3));
                        g.DrawEllipse(pen, C(12, 12, 6.8f));
                        using (var tooth = new Pen(color, Math.Max(1.5f, 2.6f * k)) { StartCap = LineCap.Flat, EndCap = LineCap.Flat })
                        {
                            for (int i = 0; i < 8; i++)
                            {
                                double a = Math.PI / 4 * i;
                                float cx = (float)Math.Cos(a), cy = (float)Math.Sin(a);
                                g.DrawLine(tooth, P(12 + cx * 6.6f, 12 + cy * 6.6f), P(12 + cx * 9.4f, 12 + cy * 9.4f));
                            }
                        }
                        break;
                    case Ico.Phone:
                        using (GraphicsPath ph = Fx.Round(new RectangleF(ox + 7 * k, oy + 3 * k, 10 * k, 18 * k), 2 * k))
                            g.DrawPath(pen, ph);
                        g.DrawLine(pen, P(11, 18), P(13, 18));
                        break;
                    case Ico.Logo:
                        DrawLogo(g, box, color);
                        break;
                }
            }
        }

        /// <summary>The WiPass mark (logo A) in a 64-unit box, filling <paramref name="box"/>.</summary>
        public static void DrawLogo(Graphics g, RectangleF box, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Math.Min(box.Width, box.Height) / 64f;
            float ox = box.X + (box.Width - 64 * k) / 2f, oy = box.Y + (box.Height - 64 * k) / 2f;
            using (var pen = new Pen(color, 7f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (float r in new[] { 14f, 24f, 34f })
                    g.DrawArc(pen, ox + (17 - r) * k, oy + (32 - r) * k, 2 * r * k, 2 * r * k, -45, 90);
            }
            using (var b = new SolidBrush(color)) g.FillEllipse(b, ox + 12 * k, oy + 27 * k, 10 * k, 10 * k);
        }
    }
}
