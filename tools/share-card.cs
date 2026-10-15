// Renders docs/og-image.png, the 1200x630 card shown when the site is shared.
// Run from the repo root: dotnet run tools/share-card.cs -- docs/icon.png docs/screenshot.webp docs/og-image.png
#:package SkiaSharp@4.153.1
using SkiaSharp;

const int W = 1200, H = 630;
using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
var c = surface.Canvas;

// Background: deep charcoal with soft teal/green glows.
c.Clear(new SKColor(0x10, 0x13, 0x15));
void Glow(float x, float y, float r, SKColor col)
{
    using var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new SKPoint(x, y), r, [col, col.WithAlpha(0)], SKShaderTileMode.Clamp) };
    c.DrawCircle(x, y, r, p);
}
Glow(180, 80, 520, new SKColor(0x2D, 0xD4, 0xBF, 70));
Glow(1050, 560, 620, new SKColor(0x0E, 0x74, 0x90, 90));
Glow(820, 120, 380, new SKColor(0x4A, 0xDE, 0x80, 40));

var bold = SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
var regular = SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
var medium = SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyleWeight.Medium, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

// App window on the right, bleeding off the edge.
using (var shot = SKBitmap.Decode(args[1]))
{
    float sw = 700, sh = sw * shot.Height / shot.Width;
    var dest = SKRect.Create(560, 150, sw, sh);
    using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 150), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 24) };
    c.DrawRoundRect(SKRect.Create(dest.Left, dest.Top + 16, dest.Width, dest.Height), 12, 12, shadow);
    using var sampling = new SKPaint { IsAntialias = true };
    c.DrawImage(SKImage.FromBitmap(shot), dest, new SKSamplingOptions(SKCubicResampler.Mitchell), sampling);
}

// Left column: icon, name, tagline, footer.
using (var icon = SKBitmap.Decode(args[0]))
    c.DrawImage(SKImage.FromBitmap(icon), SKRect.Create(72, 72, 96, 96), new SKSamplingOptions(SKCubicResampler.Mitchell));

using var white = new SKPaint { IsAntialias = true, Color = new SKColor(0xF3, 0xF4, 0xF5) };
using var muted = new SKPaint { IsAntialias = true, Color = new SKColor(0xA3, 0xA8, 0xB0) };
using var teal = new SKPaint { IsAntialias = true, Color = new SKColor(0x2D, 0xD4, 0xBF) };

c.DrawText("PowerLens", 186, 140, SKTextAlign.Left, new SKFont(bold, 58), white);

var tagFont = new SKFont(bold, 46);
string[] tagline = ["See what your", "USB-C charger", "really delivers."];
for (int i = 0; i < tagline.Length; i++)
    c.DrawText(tagline[i], 72, 268 + i * 58, SKTextAlign.Left, tagFont, white);

var bodyFont = new SKFont(regular, 24);
c.DrawText("Battery health, cycle count, live power", 72, 460, SKTextAlign.Left, bodyFont, muted);
c.DrawText("and your charger's real wattage.", 72, 492, SKTextAlign.Left, bodyFont, muted);

var footFont = new SKFont(medium, 22);
c.DrawText("Free & open source · Windows 10 & 11", 72, 562, SKTextAlign.Left, footFont, muted);
c.DrawText("getpowerlens.eu", 72, 594, SKTextAlign.Left, footFont, teal);

using var img = surface.Snapshot();
using var data = img.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes(args[2], data.ToArray());
Console.WriteLine($"{args[2]} {W}x{H} {data.Size / 1024} KB");
