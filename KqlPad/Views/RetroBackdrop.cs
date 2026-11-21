using System.Windows;
using System.Windows.Media;

namespace KqlPad.Views;

/// <summary>Synthwave empty-state art: striped sun, glowing horizon and a perspective grid.</summary>
public sealed class RetroBackdrop : FrameworkElement
{
    static Brush Frozen(Brush b) { b.Freeze(); return b; }
    static Pen FrozenPen(Color c, double thickness) { var p = new Pen(new SolidColorBrush(c), thickness); p.Freeze(); return p; }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 80) return;

        double horizon = h * 0.64;
        double cx = w / 2;

        // haze above the horizon
        dc.DrawRectangle(Frozen(new LinearGradientBrush(Color.FromArgb(0, 255, 46, 151), Color.FromArgb(70, 255, 46, 151), 90)),
            null, new Rect(0, horizon * 0.35, w, horizon * 0.65));

        // sun
        double r = Math.Min(w * 0.14, horizon * 0.29);
        double cy = horizon - r * 0.7;
        dc.DrawEllipse(Frozen(new RadialGradientBrush(Color.FromArgb(90, 255, 46, 151), Color.FromArgb(0, 255, 46, 151))),
            null, new Point(cx, cy), r * 1.9, r * 1.9);

        Geometry sun = new EllipseGeometry(new Point(cx, cy), r, r);
        var stripes = new GeometryGroup();
        for (var k = 0; k < 7; k++)
        {
            double top = cy + r * (0.12 + k * 0.115);
            stripes.Children.Add(new RectangleGeometry(new Rect(cx - r - 2, top, 2 * r + 4, 1.5 + k * 1.6)));
        }
        sun = Geometry.Combine(sun, stripes, GeometryCombineMode.Exclude, null);
        var sunBrush = new LinearGradientBrush(Color.FromRgb(0xFE, 0xDE, 0x5D), Color.FromRgb(0xFF, 0x2E, 0x97), 90);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, horizon)));
        dc.DrawGeometry(Frozen(sunBrush), null, sun);
        dc.Pop();

        // ground
        dc.DrawRectangle(Frozen(new LinearGradientBrush(Color.FromRgb(0x1C, 0x08, 0x3C), Color.FromRgb(0x08, 0x08, 0x14), 90)),
            null, new Rect(0, horizon, w, h - horizon));

        // perspective grid
        dc.PushClip(new RectangleGeometry(new Rect(0, horizon, w, h - horizon)));
        var vert = FrozenPen(Color.FromArgb(120, 255, 46, 151), 1);
        var spacing = w / 9.0;
        for (var i = -16; i <= 16; i++)
            dc.DrawLine(vert, new Point(cx + i * spacing * 0.06, horizon), new Point(cx + i * spacing, h));
        const int rows = 12;
        for (var j = 1; j <= rows; j++)
        {
            double t = (double)j / rows;
            double y = horizon + (h - horizon) * Math.Pow(t, 2.2);
            dc.DrawLine(FrozenPen(Color.FromArgb((byte)(40 + 130 * t), 157, 77, 255), 1), new Point(0, y), new Point(w, y));
        }
        dc.Pop();

        // horizon line
        dc.DrawLine(FrozenPen(Color.FromRgb(0x00, 0xF0, 0xFF), 2), new Point(0, horizon), new Point(w, horizon));
        dc.DrawRectangle(Frozen(new LinearGradientBrush(Color.FromArgb(90, 0, 240, 255), Color.FromArgb(0, 0, 240, 255), 90)),
            null, new Rect(0, horizon, w, 14));
    }
}
