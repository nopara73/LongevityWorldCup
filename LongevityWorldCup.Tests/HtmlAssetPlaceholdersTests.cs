using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HtmlAssetPlaceholdersTests
{
    [Fact]
    public void Replace_VersionsOnlyReferencedAssetsOncePerPage()
    {
        var paths = new List<string>();
        var html = HtmlAssetPlaceholders.Replace(
            "<link href='{{ASSET_FONT_AWESOME_CSS}}'><link href='{{ASSET_FONT_AWESOME_CSS}}'><img src='{{ASSET_FAVICON_128}}'>",
            path =>
            {
                paths.Add(path);
                return path + "?v=current";
            });

        Assert.Equal(new[] { "/vendor/font-awesome/6.7.2/css/all.min.css", "/assets/favicon-128x128.png" }, paths);
        Assert.Equal(
            "<link href='/vendor/font-awesome/6.7.2/css/all.min.css?v=current'><link href='/vendor/font-awesome/6.7.2/css/all.min.css?v=current'><img src='/assets/favicon-128x128.png?v=current'>",
            html);
    }

    [Fact]
    public void Replace_ResolvesVersionsAgainForTheNextPage()
    {
        const string template = "{{ASSET_MISC_JS}}";
        var first = HtmlAssetPlaceholders.Replace(template, path => path + "?v=before");
        var second = HtmlAssetPlaceholders.Replace(template, path => path + "?v=after");

        Assert.Equal("/js/misc.js?v=before", first);
        Assert.Equal("/js/misc.js?v=after", second);
    }

    [Fact]
    public void Replace_PreservesOtherTemplateContentWithoutResolvingAssets()
    {
        const string template = "<div>{{SEO_DESCRIPTION}} {{ASSET_UNKNOWN}} regular text</div>";
        var result = HtmlAssetPlaceholders.Replace(template, _ => throw new InvalidOperationException("No known assets are present."));

        Assert.Equal(template, result);
    }
}
