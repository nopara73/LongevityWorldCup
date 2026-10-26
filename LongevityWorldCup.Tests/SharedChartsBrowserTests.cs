using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class SharedChartsBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task FocusedSvgChart_PreservesNativeModifiedPageNavigation()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), ReducedMotion = ReducedMotion.Reduce });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/site-statistics.html");
        await page.EvaluateAsync("""
            () => {
                document.body.innerHTML = '<main><div style="height:1200px"></div><div><svg id="chart" viewBox="0 0 200 100" style="width:400px;height:200px"></svg></div><div style="height:1600px"></div></main>';
                LwcCharts.crosshair(document.querySelector('svg'), {points:()=>[
                    {x:20,y:40,key:'first',title:'First',lines:['One reading']},
                    {x:180,y:60,key:'last',title:'Last',lines:['Another reading']}], onPick:key=>window.picked=key});
            }
            """);
        var chart = page.Locator("#chart");
        await chart.FocusAsync();
        // Rounded offsets can reach the bottom before Chromium finishes the native
        // scroll. Wait for that scrollend before sending the opposite navigation key.
        await page.EvaluateAsync("""
            () => {
                window.chartScrollEnded = false;
                const finished = () => {
                    if (scrollY + innerHeight < document.documentElement.scrollHeight - 2) return;
                    window.chartScrollEnded = true;
                    document.removeEventListener('scrollend', finished);
                };
                document.addEventListener('scrollend', finished);
            }
            """);
        await chart.PressAsync("Control+End");
        await page.WaitForFunctionAsync("() => scrollY + innerHeight >= document.documentElement.scrollHeight - 2");
        await page.WaitForFunctionAsync("() => window.chartScrollEnded");
        await Expect(page.Locator(".lwc-chart-tip")).ToContainTextAsync("First");
        await Expect(page.Locator(".lwc-chart-tip")).ToBeHiddenAsync();
        await page.Keyboard.PressAsync("Control+Home");
        await page.WaitForFunctionAsync("() => scrollY === 0");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal("last", await page.EvaluateAsync<string>("() => window.picked"));
        await Expect(page.Locator(".lwc-chart-tip")).ToBeHiddenAsync();
        await chart.ScrollIntoViewIfNeededAsync();
        await Expect(page.Locator(".lwc-chart-tip")).ToBeVisibleAsync();
        await Expect(page.Locator(".lwc-chart-tip")).ToContainTextAsync("Last");
    }

    [Fact]
    public async Task ColdAssets_BindTheFirstTrafficChart_AndUseIndependentVersions()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), ReducedMotion = ReducedMotion.Reduce });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var release = new TaskCompletionSource();
        var requested = new TaskCompletionSource();
        await context.RouteAsync("**/js/charts.js*", async route => { requested.TrySetResult(); await release.Task; await route.ContinueAsync(); });
        await context.RouteAsync("**/api/site-statistics/dashboard?**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = """
            {"generatedAtUtc":"2026-10-17T12:00:00Z","filters":{"fromUtc":"2026-10-10T00:00:00Z","toUtc":"2026-10-17T00:00:00Z"},"events":[],"previousEvents":[],
            "trafficSummary":{"totals":{"sessions":2,"pageViews":3,"events":4},"previousTotals":{"sessions":0,"pageViews":0,"events":0},"cleanTotals":{"sessions":2,"pageViews":3,"events":4},"quality":{},"daily":[{"day":"2026-10-16","sessions":2,"pageViews":3,"events":4}],"topPages":[],"sources":[],"referrers":[],"devices":[],"browsers":[],"outcomes":{}}}
            """
        }));
        var page = await context.NewPageAsync();
        var navigation = page.GotoAsync("/internal/site-statistics.html");
        try { await requested.Task.WaitAsync(TimeSpan.FromSeconds(20)); await Task.Delay(250); }
        finally { release.TrySetResult(); }
        await navigation;
        await page.Locator("#trafficDailyChart").FocusAsync();
        await page.Locator("#trafficDailyChart").PressAsync("End");
        await Expect(page.Locator(".lwc-chart-tip")).ToBeVisibleAsync();
        await Expect(page.Locator(".lwc-chart-tip")).ToContainTextAsync("2 sessions");
        var urls = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('script[src],link[rel=stylesheet]')].map(e=>e.src||e.href).filter(u=>/\\/(charts|motion)\\.(js|css)/.test(u))");
        Assert.Equal(new[] { "/css/charts.css", "/js/charts.js", "/js/motion.js" },
            urls.Select(url => new Uri(url).AbsolutePath).OrderBy(path => path, StringComparer.Ordinal).ToArray());
        Assert.Equal(3, urls.Select(url => new Uri(url).Query).Distinct().Count());
        Assert.All(urls, url => Assert.Matches(@"\?v=[a-f0-9]+$", url));
        await page.Locator("#trafficDailyChart").PressAsync("Enter");
        await Expect(page.Locator("#trafficDayDetail")).ToContainTextAsync("2 sessions · 3 page views");
        await page.Locator("#statsRange").SelectOptionAsync("7d");
        await Expect(page.Locator(".lwc-chart-tip")).ToHaveCountAsync(1);
        await page.SetViewportSizeAsync(160, 422);
        await page.WaitForFunctionAsync("() => { const s=document.querySelector('#trafficDailyChart'); return Math.abs(s.viewBox.baseVal.width-s.parentElement.clientWidth)<1; }");
        Assert.True(await page.Locator("#trafficDailyChart .traffic-date-label").EvaluateAllAsync<bool>("es => es.every((e,i)=>!i || es[i-1].getBoundingClientRect().right+4<=e.getBoundingClientRect().left)"));
    }

    [Theory]
    [InlineData(1440, 900, false, 100)]
    [InlineData(390, 844, true, 100)]
    [InlineData(320, 640, false, 200)]
    [InlineData(844, 390, true, 200)]
    public async Task SvgTooltip_ShortContentFits_LongContentIsReachable_AndCoordinatesFollowSvg(int width, int height, bool dark, int textPercent)
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = height }, ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/site-statistics.html");
        await page.EvaluateAsync("""
            () => {
                document.body.replaceChildren();
                document.body.innerHTML = '<main style="padding:12px"><div id="host"><svg viewBox="20 10 200 200" style="width:100%;height:150px" role="img" aria-label="Daily sessions"><circle cx="30" cy="20" r="4"/><circle cx="210" cy="180" r="4"/></svg></div><button>After chart</button></main>';
                window.testPoints = [{x:30,y:20,key:'first',title:'Oct 16',lines:['2 sessions','3 page views']},{x:210,y:180,key:'last',title:'Oct 17 · partial day',lines:['1,234 sessions','2,345 page views']}];
                window.disposeTestChart = LwcCharts.crosshair(document.querySelector('svg'), {points:()=>window.testPoints,onPick:key=>window.picked=key});
            }
            """);
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }} ::-webkit-scrollbar {{ width:16px; height:16px; }}" });
        var svg = page.Locator("svg");
        await svg.FocusAsync();
        await AssertTipFitsAsync(page, shortContent: true);
        await svg.PressAsync("End");
        await svg.PressAsync("Enter");
        Assert.Equal("last", await page.EvaluateAsync<string>("() => window.picked"));
        await AssertTipFitsAsync(page, shortContent: true);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => { const svg=document.querySelector('svg'), host=document.querySelector('#host'), p=new DOMPoint(210,180).matrixTransform(svg.getScreenCTM());
              return Math.abs(parseFloat(host.style.getPropertyValue('--lwc-guide-x'))-(p.x-host.getBoundingClientRect().left))<1; }
            """));
        await page.EvaluateAsync("() => window.testPoints[1].lines = Array.from({length:40}, (_,i)=>'Reading '+i+' · 1,234 sessions · 2,345 page views')");
        await svg.PressAsync("End");
        await AssertTipFitsAsync(page, shortContent: false);
        await page.Locator(".lwc-chart-tip").FocusAsync();
        await page.Locator(".lwc-chart-tip").PressAsync("Control+End");
        await page.WaitForFunctionAsync("() => { const e=document.querySelector('.lwc-chart-tip'); return e.scrollTop+e.clientHeight>=e.scrollHeight-2; }");
        Assert.True(await page.Locator(".lwc-chart-tip").EvaluateAsync<bool>("e=>e.scrollTop+e.clientHeight>=e.scrollHeight-2"));
        await page.Locator(".lwc-chart-tip").PressAsync("Escape");
        await Expect(page.Locator(".lwc-chart-tip")).ToBeHiddenAsync();
        await page.EvaluateAsync("() => { window.disposeTestChart(); window.disposeTestChart(); }");
        await Expect(page.Locator(".lwc-chart-tip,.lwc-chart-guide,.lwc-chart-dot")).ToHaveCountAsync(0);
        Assert.Null(await svg.GetAttributeAsync("tabindex"));
    }

    private static async Task AssertTipFitsAsync(IPage page, bool shortContent)
    {
        var result = await page.Locator(".lwc-chart-tip").EvaluateAsync<JsonElement>("""
            e=>{const r=e.getBoundingClientRect();return {visible:!e.hidden, x:r.x,y:r.y,right:r.right,bottom:r.bottom, width:innerWidth,height:innerHeight,sw:e.scrollWidth,cw:e.clientWidth,sh:e.scrollHeight,ch:e.clientHeight};}
            """);
        Assert.True(result.GetProperty("visible").GetBoolean());
        Assert.True(result.GetProperty("x").GetDouble() >= 0 && result.GetProperty("y").GetDouble() >= 0, result.ToString());
        Assert.True(result.GetProperty("right").GetDouble() <= result.GetProperty("width").GetDouble() && result.GetProperty("bottom").GetDouble() <= result.GetProperty("height").GetDouble(), result.ToString());
        Assert.True(result.GetProperty("sw").GetInt32() <= result.GetProperty("cw").GetInt32(), result.ToString());
        if (shortContent) Assert.True(result.GetProperty("sh").GetInt32() <= result.GetProperty("ch").GetInt32(), result.ToString());
    }

    [Fact]
    public async Task Registration_PreservesGlobalDescriptors_AndSupportsReplacementStubs()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("window.chartSlot=undefined; Object.defineProperty(window,'Chart',{configurable:true,get(){return window.chartSlot},set(v){window.chartSlot=v}}); window.chartGetter=Object.getOwnPropertyDescriptor(window,'Chart').get;");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/site-statistics.html");
        Assert.True(await page.EvaluateAsync<bool>("""
            async () => {
                if(Object.getOwnPropertyDescriptor(window,'Chart').get!==window.chartGetter) return false;
                const a=function(){}, b=function(){}; a.register=b.register=function(){this.calls=(this.calls||0)+1};
                LwcCharts.install(a); LwcCharts.install(a); LwcCharts.install(b);
                delete window.Chart; window.Chart=b; LwcCharts.install(window.Chart);
                const script=document.createElement('script'); script.src=document.querySelector('script[src*="/js/charts.js"]').src;
                await new Promise(resolve=>{script.onload=resolve;document.head.append(script)});
                return a.calls===1 && b.calls===1 && window.Chart===b;
            }
            """));
    }

    [Fact]
    public async Task Plugin_UsesExplicitOwnReadingsInDateOrder_AndCleansUpBeforeCanvasRelease()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), ReducedMotion = ReducedMotion.Reduce });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/site-statistics.html");
        var result = await page.EvaluateAsync<JsonElement>("""
            () => {
                const Constructor=function(){}; Constructor.defaults={}; Constructor.register=p=>Constructor.plugin=p;
                LwcCharts.install(Constructor);
                const canvas=document.createElement('canvas');document.body.append(canvas);
                const paths=[],arcs=[];
                const ctx={save(){},restore(){},beginPath(){},rect(){},clip(){},stroke(){},fill(){},setLineDash(){},moveTo:(x,y)=>paths.push([x,y]),lineTo:(x,y)=>paths.push([x,y]),arc:(x,y)=>arcs.push([x,y])};
                const meta=[{data:[{x:200,y:100}]},{data:[{x:150,y:30},{x:50,y:40}]}];
                const chart={canvas,ctx,chartArea:{left:0,right:300,top:0,bottom:200},config:{type:'scatter'},options:{plugins:{tooltip:{}}},scales:{x:{},y:{}},
                  data:{datasets:[{data:[{isAthlete:false,date:'2026-01-01'}]},{data:[{isAthlete:true,date:'2026-01-01'},{isAthlete:true,date:'2025-01-01'}]}]},getDatasetMeta:i=>meta[i],draw(){}};
                Constructor.plugin.afterInit(chart);
                Constructor.plugin.beforeDatasetsDraw(chart);
                Constructor.plugin.afterDatasetsDraw(chart);
                Constructor.plugin.beforeDestroy(chart); chart.canvas=null; canvas.remove();
                return {paths,arcs};
            }
            """);
        Assert.Equal("[[50,40],[150,30]]", result.GetProperty("paths").GetRawText());
        Assert.Equal("[[150,30],[50,40]]", result.GetProperty("arcs").GetRawText());
    }

    [Fact]
    public async Task ReducedMotion_ClearsPendingOffscreenDrawsAcrossPreferenceChanges()
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ReducedMotion = ReducedMotion.NoPreference
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/site-statistics.html");
        await page.EvaluateAsync("() => document.body.replaceChildren()");
        for (var cycle = 0; cycle < 12; cycle++)
        {
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            await page.WaitForFunctionAsync("() => LwcCharts.motion()");
            await page.EvaluateAsync("""
                () => {
                    document.body.innerHTML='<div style="position:absolute;top:10000px"><svg id="pendingChart" width="200" height="200"></svg></div>';
                    LwcCharts.reveal(document.getElementById('pendingChart'));
                }
                """);
            await Expect(page.Locator("#pendingChart")).ToHaveAttributeAsync("data-lwc-draw", "wait");
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
            // Reading the public preference must not consume the native change
            // event before pending draws and observers have been cleaned up.
            await page.WaitForFunctionAsync("() => !LwcCharts.motion()");
            await Expect(page.Locator("#pendingChart")).Not.ToHaveAttributeAsync(
                "data-lwc-draw", new System.Text.RegularExpressions.Regex(".*"));
            Assert.Equal("1", await page.Locator("#pendingChart").EvaluateAsync<string>("e => getComputedStyle(e).opacity"));
        }
    }

    [Fact]
    public async Task Counters_KeepExactText_AndCancelOnChangesHiddenHostsAndReducedMotion()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), ReducedMotion = ReducedMotion.NoPreference });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/site-statistics.html");
        await page.EvaluateAsync("() => {document.body.innerHTML='<button id=start>Start</button><div id=number>−1,234.50 years</div>';LwcCharts.count(document.querySelector('#number'),50)}");
        var number = page.Locator("#number");
        await Expect(number).ToHaveTextAsync("−1,234.50 years");
        await Expect(number).Not.ToHaveAttributeAsync("data-lwc-count", new System.Text.RegularExpressions.Regex(".*"));
        await page.Locator("#start").ClickAsync();
        var animated = await number.EvaluateAsync<string?>("e=>{LwcCharts.count(e,50);return e.getAttribute('data-lwc-count')}");
        Assert.Contains("years", animated ?? "");
        await Expect(number).ToHaveTextAsync("−1,234.50 years");
        await number.EvaluateAsync("e=>{e.textContent='+2.00 years';LwcCharts.count(e,-1234.5);e.hidden=true}");
        await Expect(number).Not.ToHaveAttributeAsync("data-lwc-count", new System.Text.RegularExpressions.Regex(".*"));
        await number.EvaluateAsync("e=>{e.hidden=false;LwcCharts.count(e,-100)}");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Expect(number).Not.ToHaveAttributeAsync("data-lwc-count", new System.Text.RegularExpressions.Regex(".*"));
        await Expect(number).ToHaveTextAsync("+2.00 years");
        Assert.Equal("−1,234.50 years", await page.EvaluateAsync<string>("() => LwcCharts.format(-1234.5,{decimals:2,unit:'years'})"));
    }
}
