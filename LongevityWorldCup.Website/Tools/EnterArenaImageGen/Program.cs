using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

// Find wwwroot: from output dir (Tools/EnterArenaImageGen/bin/Debug/net8.0) go up to Website folder
var baseDir = AppContext.BaseDirectory;
var websiteDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
var wwwroot = Path.Combine(websiteDir, "wwwroot");
var assets = Path.Combine(wwwroot, "assets");
var contentImages = Path.Combine(assets, "content-images");
var whyPath = Path.Combine(contentImages, "why.jpg");
var logoPath = Path.Combine(assets, "HdLogo.png");
var outJpg = Path.Combine(contentImages, "enter-arena.jpg");
var outWebp = Path.Combine(contentImages, "enter-arena.webp");

if (!File.Exists(whyPath))
{
    Console.Error.WriteLine("Reference image not found: " + whyPath);
    return 1;
}
if (!File.Exists(logoPath))
{
    Console.Error.WriteLine("Logo not found: " + logoPath);
    return 1;
}

// Get reference dimensions from why.jpg
using (var refImg = await Image.LoadAsync(whyPath))
{
    int refW = refImg.Width;
    int refH = refImg.Height;
    double targetRatio = (double)refW / refH;

    using var logo = await Image.LoadAsync(logoPath);
    int w = logo.Width;
    int h = logo.Height;
    double logoRatio = (double)w / h;

    int cropW, cropH, x, y;
    if (logoRatio >= targetRatio)
    {
        cropH = h;
        cropW = (int)Math.Round(h * targetRatio);
        x = (w - cropW) / 2;
        y = 0;
    }
    else
    {
        cropW = w;
        cropH = (int)Math.Round(w / targetRatio);
        x = 0;
        y = (h - cropH) / 2;
    }

    var cropRect = new SixLabors.ImageSharp.Rectangle(x, y, cropW, cropH);
    logo.Mutate(i => i.Crop(cropRect));

    // Resize to match reference size (so display dimensions match exactly)
    logo.Mutate(i => i.Resize(refW, refH));

    var jpegEncoder = new JpegEncoder { Quality = 90 };
    await logo.SaveAsync(outJpg, jpegEncoder);

    var webpEncoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossy };
    await logo.SaveAsync(outWebp, webpEncoder);

    Console.WriteLine("Created " + outJpg + " and " + outWebp);
}

return 0;
