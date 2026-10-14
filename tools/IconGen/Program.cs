using SkiaSharp;

// PowerLens icon: a round lens with an energy bolt, in the Windows 11 (Fluent) style.
// Drawn in a 256-unit space and scaled per size.

string outDir = args.Length > 0 ? args[0] : Path.Combine("src", "PowerLens.App", "Assets");
Directory.CreateDirectory(outDir);

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
var pngs = sizes.Select(s => (Size: s, Png: Render(s))).ToList();

File.WriteAllBytes(Path.Combine(outDir, "PowerLens.png"), pngs.Last().Png);
WriteIco(Path.Combine(outDir, "PowerLens.ico"), pngs);
Console.WriteLine($"Wrote PowerLens.ico ({string.Join(", ", sizes)}) and PowerLens.png to {outDir}");

static byte[] Render(int size)
{
    // Fluent-style: a round lens with a soft green-to-teal gradient, a subtle glass rim and
    // highlight, and a rounded energy bolt. No outlines; small sizes drop the glass details.
    bool small = size <= 24;
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var c = surface.Canvas;
    c.Clear(SKColors.Transparent);
    c.Scale(size / 256f);

    var center = new SKPoint(128, 128);
    float radius = small ? 120 : 110;

    // Soft ambient shadow.
    if (!small)
    {
        using var shadow = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x0B, 0x3B, 0x3A, 70),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 7),
        };
        c.DrawCircle(center.X, center.Y + 6, radius - 2, shadow);
    }

    // Lens body.
    using (var lens = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(
            new SKPoint(center.X - radius, center.Y - radius), new SKPoint(center.X + radius, center.Y + radius),
            [new SKColor(0x6E, 0xE7, 0xA8), new SKColor(0x14, 0xB8, 0xA6), new SKColor(0x0E, 0x74, 0x90)],
            [0f, 0.55f, 1f], SKShaderTileMode.Clamp),
    })
    {
        c.DrawCircle(center, radius, lens);
    }

    if (!small)
    {
        // Glass thickness: a lighter inner disc that reads as the lens rim.
        using var inner = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(center.X, center.Y - radius), new SKPoint(center.X, center.Y + radius),
                [new SKColor(255, 255, 255, 46), new SKColor(255, 255, 255, 8)], SKShaderTileMode.Clamp),
        };
        c.DrawCircle(center, radius - 16, inner);

        // Specular highlight along the upper-left edge.
        using var shine = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 7,
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(255, 255, 255, 120),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.2f),
        };
        float r = radius - 9;
        c.DrawArc(new SKRect(center.X - r, center.Y - r, center.X + r, center.Y + r), 195, 60, false, shine);
    }

    // Energy bolt.
    float k = small ? 1.18f : 1f;
    using var bolt = new SKPath();
    var pts = new (float X, float Y)[] { (14, -74), (-44, 10), (-6, 10), (-16, 74), (44, -12), (6, -12) };
    bolt.MoveTo(center.X + pts[0].X * k, center.Y + pts[0].Y * k);
    foreach (var (x, y) in pts.Skip(1)) bolt.LineTo(center.X + x * k, center.Y + y * k);
    bolt.Close();

    using var rounded = new SKPaint { IsAntialias = true, PathEffect = SKPathEffect.CreateCorner(small ? 6 : 9) };
    using var boltPath = new SKPath();
    rounded.GetFillPath(bolt, boltPath);

    if (!small)
    {
        using var boltShadow = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x06, 0x4E, 0x5B, 90),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5),
        };
        c.Save();
        c.Translate(0, 4);
        c.DrawPath(boltPath, boltShadow);
        c.Restore();
    }

    using (var boltFill = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, center.Y - 74), new SKPoint(0, center.Y + 74),
            [SKColors.White, new SKColor(0xEC, 0xFD, 0xF5)], SKShaderTileMode.Clamp),
    })
    {
        c.DrawPath(boltPath, boltFill);
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// ICO container with PNG-compressed entries (supported since Windows Vista).
static void WriteIco(string path, List<(int Size, byte[] Png)> images)
{
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)images.Count);

    int offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(png.Length);
        w.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in images) w.Write(png);
}
