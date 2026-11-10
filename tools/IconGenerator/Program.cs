using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// <summary>
/// Zeichnet das App-Symbol des RAL_BG-Generators als Vektorgrafik (256er-Raster) und
/// schreibt daraus eine Mehrfach-ICO (16 ... 256 px) plus Vorschaubilder.
/// Motiv: RAL-Farbfaecher + isometrisches Bauteil in der Farbe der vordersten Karte.
/// </summary>
internal static class Program
{
    private const double D = 256;

    // sRGB-Naeherungen der RAL-Toene
    private static readonly Color Ral1023 = Color.FromRgb(0xF2, 0xB7, 0x00); // Verkehrsgelb
    private static readonly Color Ral6018 = Color.FromRgb(0x57, 0xA6, 0x39); // Gelbgruen
    private static readonly Color Ral5015 = Color.FromRgb(0x22, 0x71, 0xB3); // Himmelblau
    private static readonly Color Ral3020 = Color.FromRgb(0xC1, 0x12, 0x1C); // Verkehrsrot

    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];

    /// <summary>
    /// Aufruf: IconGenerator &lt;Zielordner&gt; [--preview]
    /// Schreibt AppIcon.ico; mit --preview zusaetzlich icon_*.png und preview.png.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? ".";
        var withPreview = args.Contains("--preview", StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(outDir);

        var bitmaps = Sizes.ToDictionary(size => size, Render);
        var icoPath = Path.Combine(outDir, "AppIcon.ico");

        WriteIco(bitmaps, icoPath);
        Console.WriteLine("Geschrieben: " + Path.GetFullPath(icoPath));

        if (withPreview)
        {
            foreach (var (size, bitmap) in bitmaps)
            {
                SavePng(bitmap, Path.Combine(outDir, $"icon_{size}.png"));
            }

            SavePng(ContactSheet(bitmaps), Path.Combine(outDir, "preview.png"));
            Console.WriteLine("Vorschau:    " + Path.GetFullPath(Path.Combine(outDir, "preview.png")));
        }

        return 0;
    }

    // ------------------------------------------------------------------ Motiv

    private static BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / D, size / D));
            DrawIcon(dc, small: size <= 24);
            dc.Pop();
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static void DrawIcon(DrawingContext dc, bool small)
    {
        var cube = new IsoCube(new Point(186, 177), small ? 68 : 62);

        // Faecher mit ausgespartem Rand um den Wuerfel, damit sich beide auf jedem
        // Hintergrund sauber trennen (statt eines weissen Aufkleber-Rands).
        var silhouette = cube.Silhouette();
        var gap = silhouette.GetWidenedPathGeometry(
            new Pen(Brushes.Black, small ? 22 : 14) { LineJoin = PenLineJoin.Round });
        var knockout = Geometry.Combine(
            new RectangleGeometry(new Rect(0, 0, D, D)),
            Geometry.Combine(silhouette, gap, GeometryCombineMode.Union, null),
            GeometryCombineMode.Exclude,
            null);

        dc.PushClip(knockout);
        DrawFan(dc, new Point(72, 204), small);
        dc.Pop();

        DrawCube(dc, cube, small);
    }

    private static void DrawFan(DrawingContext dc, Point pivot, bool small)
    {
        (Color Color, double Angle)[] blades = small
            ? [(Ral1023, -8), (Ral5015, 20), (Ral3020, 48)]
            : [(Ral1023, -10), (Ral6018, 9), (Ral5015, 28), (Ral3020, 47)];

        var width = small ? 70.0 : 58.0;
        const double length = 180;
        var blade = BladeGeometry(width, length, 12);

        foreach (var (color, angle) in blades)
        {
            dc.PushTransform(new TranslateTransform(pivot.X, pivot.Y));
            dc.PushTransform(new RotateTransform(angle));

            var fill = new LinearGradientBrush(Lighten(color, 0.16), color, new Point(0, 0), new Point(0, 0.7));
            var pen = small ? null : new Pen(new SolidColorBrush(Darken(color, 0.38)), 3.5);

            dc.DrawGeometry(fill, pen, blade);

            if (!small)
            {
                // Glanzkante links - wirkt wie die Papierkante einer Faecherkarte.
                var highlight = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 4)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                dc.DrawLine(highlight, new Point(-width / 2 + 7, -length + 14), new Point(-width / 2 + 7, -28));
            }

            dc.Pop();
            dc.Pop();
        }

        // Niete am Drehpunkt
        if (small)
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xE9)), null, pivot, 13, 13);
        }
        else
        {
            var metal = new RadialGradientBrush(Color.FromRgb(0xF2, 0xF4, 0xF5), Color.FromRgb(0x86, 0x90, 0x96))
            {
                GradientOrigin = new Point(0.35, 0.3),
                Center = new Point(0.4, 0.35),
                RadiusX = 0.75,
                RadiusY = 0.75
            };
            dc.DrawEllipse(metal, new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x58)), 3.5), pivot, 14, 14);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x5E, 0x67, 0x6D)), null, pivot, 4, 4);
        }
    }

    /// <summary>
    /// Faecherkarte mit dem Drehpunkt im Ursprung: oben abgerundete Ecken, unten ein
    /// Halbkreis um den Drehpunkt - so decken sich die Kartenenden aller Karten.
    /// </summary>
    private static Geometry BladeGeometry(double width, double length, double radius)
    {
        var half = width / 2;
        var figure = new PathFigure { StartPoint = new Point(-half, 0), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(new Point(-half, -length + radius), true));
        figure.Segments.Add(new ArcSegment(new Point(-half + radius, -length), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(new Point(half - radius, -length), true));
        figure.Segments.Add(new ArcSegment(new Point(half, -length + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(new Point(half, 0), true));
        figure.Segments.Add(new ArcSegment(new Point(-half, 0), new Size(half, half), 0, false, SweepDirection.Clockwise, true));

        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static void DrawCube(DrawingContext dc, IsoCube cube, bool small)
    {
        var baseColor = Ral3020;

        var top = new LinearGradientBrush(Lighten(baseColor, 0.34), Lighten(baseColor, 0.2), new Point(0, 0), new Point(1, 1));
        var left = new LinearGradientBrush(Lighten(baseColor, 0.06), Darken(baseColor, 0.06), new Point(0, 0), new Point(0, 1));
        var right = new LinearGradientBrush(Darken(baseColor, 0.3), Darken(baseColor, 0.42), new Point(0, 0), new Point(0, 1));

        dc.DrawGeometry(top, null, cube.TopFace());
        dc.DrawGeometry(left, null, cube.LeftFace());
        dc.DrawGeometry(right, null, cube.RightFace());

        if (!small)
        {
            // Lichtkanten an der Vorderecke
            var edge = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 214, 208)), 3)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            dc.DrawLine(edge, cube.L, cube.C);
            dc.DrawLine(edge, cube.C, cube.R);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 214, 208)), 3), cube.C, cube.B);

            // Aussenkontur
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Darken(baseColor, 0.55)), 3.5) { LineJoin = PenLineJoin.Round }, cube.Silhouette());
        }
    }

    private sealed class IsoCube(Point center, double edge)
    {
        private static readonly double Cos30 = Math.Sqrt(3) / 2;

        public Point T => new(center.X, center.Y - edge);
        public Point R => new(center.X + edge * Cos30, center.Y - edge / 2);
        public Point RB => new(center.X + edge * Cos30, center.Y + edge / 2);
        public Point B => new(center.X, center.Y + edge);
        public Point LB => new(center.X - edge * Cos30, center.Y + edge / 2);
        public Point L => new(center.X - edge * Cos30, center.Y - edge / 2);
        public Point C => center;

        public Geometry TopFace() => Polygon(T, R, C, L);
        public Geometry LeftFace() => Polygon(L, C, B, LB);
        public Geometry RightFace() => Polygon(C, R, RB, B);
        public Geometry Silhouette() => Polygon(T, R, RB, B, LB, L);

        private static Geometry Polygon(params Point[] points)
        {
            var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };

            foreach (var point in points.Skip(1))
            {
                figure.Segments.Add(new LineSegment(point, true));
            }

            var geometry = new PathGeometry([figure]);
            geometry.Freeze();
            return geometry;
        }
    }

    // ------------------------------------------------------------------ Vorschau

    private static BitmapSource ContactSheet(IReadOnlyDictionary<int, BitmapSource> bitmaps)
    {
        const int width = 1000;
        const int height = 900;
        var light = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        var dark = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            // Gross auf hell / dunkel
            dc.DrawRectangle(light, null, new Rect(20, 20, 296, 296));
            dc.DrawImage(bitmaps[256], new Rect(40, 40, 256, 256));
            dc.DrawRectangle(dark, null, new Rect(336, 20, 296, 296));
            dc.DrawImage(bitmaps[256], new Rect(356, 40, 256, 256));

            // Alle Groessen 1:1
            dc.DrawRectangle(light, null, new Rect(20, 336, 612, 90));
            dc.DrawRectangle(dark, null, new Rect(20, 426, 612, 90));
            var x = 36.0;

            foreach (var size in Sizes.Where(size => size <= 64))
            {
                dc.DrawImage(bitmaps[size], new Rect(x, 381 - size / 2.0, size, size));
                dc.DrawImage(bitmaps[size], new Rect(x, 471 - size / 2.0, size, size));
                x += size + 24;
            }

            // Kleine Groessen vergroessert (Pixelraster)
            dc.DrawRectangle(light, null, new Rect(20, 536, 960, 344));
            x = 40;

            foreach (var size in new[] { 16, 24, 32, 48 })
            {
                var scale = size <= 24 ? 8 : 6;
                dc.DrawImage(Upscale(bitmaps[size], scale), new Rect(x, 556, size * scale, size * scale));
                x += size * scale + 30;
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return target;
    }

    /// <summary>Pixelgenaue Vergroesserung (Nearest Neighbor) fuer die Vorschau.</summary>
    private static BitmapSource Upscale(BitmapSource bitmap, int factor)
    {
        var size = bitmap.PixelWidth;
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);

        var big = size * factor;
        var result = new byte[big * big * 4];

        for (var y = 0; y < big; y++)
        {
            for (var x = 0; x < big; x++)
            {
                Array.Copy(pixels, ((y / factor) * size + x / factor) * 4, result, (y * big + x) * 4, 4);
            }
        }

        return BitmapSource.Create(big, big, 96, 96, PixelFormats.Bgra32, null, result, big * 4);
    }

    // ------------------------------------------------------------------ Ausgabe

    private static void WriteIco(IReadOnlyDictionary<int, BitmapSource> bitmaps, string path)
    {
        // Bis 64 px als 32-Bit-DIB (maximal kompatibel), 256 px als PNG (Standard seit Vista).
        var entries = Sizes
            .Select(size => (Size: size, Data: size >= 256 ? EncodePng(bitmaps[size]) : EncodeDib(bitmaps[size])))
            .ToList();

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)entries.Count);

        var offset = 6 + 16 * entries.Count;

        foreach (var (size, data) in entries)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in entries)
        {
            writer.Write(data);
        }
    }

    private static byte[] EncodeDib(BitmapSource bitmap)
    {
        var size = bitmap.PixelWidth;
        var stride = size * 4;
        var pixels = new byte[stride * size];
        bitmap.CopyPixels(pixels, stride, 0);

        var maskStride = (size + 31) / 32 * 4;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // BITMAPINFOHEADER - Hoehe doppelt (Farbe + AND-Maske)
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(stride * size + maskStride * size);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        for (var y = size - 1; y >= 0; y--)
        {
            writer.Write(pixels, y * stride, stride);
        }

        for (var y = size - 1; y >= 0; y--)
        {
            var row = new byte[maskStride];

            for (var x = 0; x < size; x++)
            {
                if (pixels[y * stride + x * 4 + 3] == 0)
                {
                    row[x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }

            writer.Write(row);
        }

        return stream.ToArray();
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void SavePng(BitmapSource bitmap, string path) => File.WriteAllBytes(path, EncodePng(bitmap));

    private static Color Lighten(Color color, double amount) => Mix(color, Colors.White, amount);

    private static Color Darken(Color color, double amount) => Mix(color, Colors.Black, amount);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        a.A,
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
