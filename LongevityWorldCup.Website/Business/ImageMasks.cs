using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LongevityWorldCup.Website.Business;

internal static class ImageMasks
{
    public static void MakeCircular(Image<Rgba32> image)
    {
        var w = image.Width;
        var h = image.Height;
        var radius = Math.Min(w, h) / 2f;

        using var mask = new Image<Rgba32>(w, h, Color.Transparent);
        mask.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                Antialias = true,
                AntialiasSubpixelDepth = 16
            });
            ctx.Fill(Color.White, new EllipsePolygon(w / 2f, h / 2f, radius));
        });

        image.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                AlphaCompositionMode = PixelAlphaCompositionMode.DestIn,
                Antialias = true,
                AntialiasSubpixelDepth = 16
            });
            ctx.DrawImage(mask, new Point(0, 0), 1f);
        });
    }
}
