using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace LongevityWorldCup.Website.Business;

internal static class ImageTextLayout
{
    internal static Font FitFontToWidth(FontFamily family, string text, float startSize, float minSize, float maxWidth, float sizeStep = 2f)
    {
        var size = startSize;
        while (size > minSize)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            if (TextMeasurer.MeasureSize(text, new RichTextOptions(font)).Width <= maxWidth)
                return font;
            size -= sizeStep;
        }

        return family.CreateFont(minSize, FontStyle.Bold);
    }
}
