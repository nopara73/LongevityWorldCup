using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class EventLivingFeedBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Athletes = """
        [{"Key":"feed-a","Name":"Ada Feed","CurrentPlacement":4,"ProfilePic":"/assets/favicon-64x64.png","Flag":"Hungary","Division":"Women's","Badges":[{"BadgeLabel":"≥2 submissions"}]},
         {"Key":"feed-b","Name":"Bela Feed","CurrentPlacement":12}]
        """;
    private const string Events = """
        [{"Id":"joined","Type":1,"Text":"slug[feed-a]","OccurredAt":"2026-10-23T12:00:00Z","Relevance":10},
         {"Id":"announcement","Type":6,"Text":"Season update\n\n[Read the rules](https://longevityworldcup.com/ruleset)","OccurredAt":"2026-10-22T12:00:00Z","Relevance":10}]
        """;

    private async Task<IBrowserContext> OpenAsync(string route = "/events", string? events = null, string? athletes = null,
        int width = 1440, int height = 900, bool dark = false, bool reduced = true, IBrowser? browser = null)
    {
        var context = await NewContextAsync(browser ?? Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, Locale = "en-US", TimezoneId = "Asia/Singapore",
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            HasTouch = width < 600, IsMobile = width < 600
        });
        await context.RouteAsync("**/api/data/athletes", r => r.FulfillAsync(new() { ContentType = "application/json", Body = athletes ?? Athletes }));
        await context.RouteAsync("**/api/events", r => r.FulfillAsync(new() { ContentType = "application/json", Body = events ?? Events }));
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(new DateTime(2026, 10, 24, 4, 0, 0, DateTimeKind.Utc));
        await NavigateAndSettleAsync(page, route);
        await page.Locator("#eventsTable tr.main-row").First.WaitForAsync();
        return context;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_PublishesBothSnapshotsAndRejectsLateSuccessOrFailure(bool oldFails)
    {
        await using var context = await OpenAsync("/");
        var page = context.Pages[0];
        await page.WaitForFunctionAsync("() => document.querySelector('#eventsStatus')?.textContent === 'Events loaded.'");
        await page.EvaluateAsync("""
            () => {
                window.pendingFeedRequests = [];
                window.fetchPublicJson = url => new Promise((resolve,reject) => pendingFeedRequests.push({url,resolve,reject}));
                window.firstFeedLoad = loadEventsTable(5,true,null,true,{live:true});
                window.secondFeedLoad = loadEventsTable(5,true,null,true,{live:true});
            }
            """);
        Assert.Equal(new[] { "/api/events", "/api/data/athletes", "/api/events", "/api/data/athletes" },
            await page.EvaluateAsync<string[]>("pendingFeedRequests.map(r=>r.url)"));
        await page.EvaluateAsync("""
            async () => {
                const athletes = [{Key:'new-athlete',Name:'New Athlete',CurrentPlacement:2,
                    ProfilePic:'/assets/favicon-64x64.png',Badges:[{BadgeLabel:'≥2 submissions'}]}];
                pendingFeedRequests[2].resolve([{Id:'arrival',Type:1,Text:'slug[new-athlete]',OccurredAt:'2026-10-24T03:00:00Z',Relevance:20}]);
                pendingFeedRequests[3].resolve(athletes);
                await secondFeedLoad;
            }
            """);
        await Assertions.Expect(page.Locator("tr.main-row")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("tr.main-row")).ToContainTextAsync("New Athlete");
        await page.EvaluateAsync("""
            async oldFails => {
                if(oldFails) { pendingFeedRequests[0].reject(new Error('old request failed'));pendingFeedRequests[1].reject(new Error('old directory failed')); }
                else { pendingFeedRequests[0].resolve([]);pendingFeedRequests[1].resolve([]); }
                await firstFeedLoad;
                await loadEventsTable(8,true,null,true,{useSharedEvents:true,preserveExistingRows:true});
            }
            """, oldFails);
        await Assertions.Expect(page.Locator("tr.main-row[data-event-id='arrival']")).ToContainTextAsync("New Athlete");
        Assert.Equal(4, await page.EvaluateAsync<int>("pendingFeedRequests.length"));
        Assert.Equal("arrival", await page.EvaluateAsync<string>("async () => (await getSharedEvents())[0].Id"));
        Assert.Equal("New Athlete", await page.EvaluateAsync<string>("async () => (await getSharedAthletes())[0].Name"));
        var link = page.Locator("tr.main-row .event-message a[data-athlete-slug]").First;
        await link.ScrollIntoViewIfNeededAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        await link.HoverAsync();
        await Assertions.Expect(page.Locator("#eventAthletePreview")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#eventAthletePreview")).ToContainTextAsync("#2");
        Assert.Equal("/assets/favicon-64x64.png", await page.Locator("#eventAthletePreview img").GetAttributeAsync("src"));
        Assert.Equal("The Regular: two or more tests submitted", await page.Locator("#eventAthletePreview .badge-class").GetAttributeAsync("aria-label"));
    }

    [Fact]
    public async Task Refresh_KeepsRowsOpenDetailsFocusAndAnchor_AndUpdatesExistingAthletes()
    {
        var events = Enumerable.Range(0, 24).Select(i => new
        {
            Id = "item-" + i, Type = 6, Text = $"Announcement {i}\n\n[Read the rules](https://longevityworldcup.com/ruleset)",
            OccurredAt = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc).AddDays(-i).ToString("O")
        });
        await using var context = await OpenAsync(events: JsonSerializer.Serialize(events));
        var page = context.Pages[0];
        await page.EvaluateAsync("""
            async () => {
                const row=document.querySelector('[data-event-id="item-12"].main-row');
                row._setCustomEventExpanded(true);
                row.scrollIntoView({block:'center',behavior:'instant'});
                row.nextElementSibling.querySelector('a').focus({preventScroll:true});
                window.keptRow=row; window.keptFocus=document.activeElement;
                window.originalTop=row.getBoundingClientRect().top;
                const original=await getSharedEvents();
                window.fetchPublicJson=async url => url.includes('athletes') ? [] :
                    [{Id:'new',Type:6,Text:'New announcement',OccurredAt:'2026-10-25T00:00:00Z'},...original];
                await loadEventsTable(null,false,null,true,{live:true});
            }
            """);
        Assert.True(await page.EvaluateAsync<bool>("keptRow===document.querySelector('[data-event-id=\"item-12\"].main-row') && keptFocus===document.activeElement"));
        await Assertions.Expect(page.Locator("[data-event-id='item-12'].main-row .custom-event-expander")).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.InRange(await page.EvaluateAsync<double>("Math.abs(keptRow.getBoundingClientRect().top-originalTop)"), 0, 2);
        var ids = await page.Locator("[id^='custom-event-details-']").EvaluateAllAsync<string[]>("els=>els.map(e=>e.id)");
        Assert.Equal(ids.Length, ids.Distinct().Count());

        await using var updatedContext = await OpenAsync();
        var updated = updatedContext.Pages[0];
        await updated.EvaluateAsync("""
            async () => {
                window.keptRow=document.querySelector('[data-event-id="joined"].main-row');
                const events=await getSharedEvents();
                window.fetchPublicJson=async url=>url.includes('athletes')
                    ? [{Key:'feed-a',Name:'Ada Updated',CurrentPlacement:1,ProfilePic:'/assets/favicon-64x64.png'}] : events;
                await loadEventsTable(null,false,null,true,{live:true});
            }
            """);
        Assert.True(await updated.EvaluateAsync<bool>("keptRow===document.querySelector('[data-event-id=\"joined\"].main-row')"));
        await Assertions.Expect(updated.Locator("[data-event-id='joined'].main-row")).ToContainTextAsync("Ada Updated");
        await Assertions.Expect(updated.Locator("[data-event-id='joined'].main-row")).ToContainTextAsync("#1");
    }

    [Fact]
    public async Task FailedRefresh_KeepsTheReadableFeedAndRecoversOnTheNextAttempt()
    {
        await using var context = await OpenAsync();
        var page = context.Pages[0];
        await page.EvaluateAsync("""
            async () => {
                window.keptRow=document.querySelector('.main-row');
                window.originalFetch=fetchPublicJson;
                window.fetchPublicJson=async()=>{throw new Error('offline')};
                await loadEventsTable(null,false,null,true,{live:true});
            }
            """);
        Assert.True(await page.EvaluateAsync<bool>("keptRow===document.querySelector('.main-row')"));
        await Assertions.Expect(page.Locator(".events-recovery")).ToHaveCountAsync(0);
        await page.EvaluateAsync("async()=>{window.fetchPublicJson=originalFetch;await loadEventsTable(null,false,null,true,{live:true});}");
        await Assertions.Expect(page.Locator(".main-row")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task Chunks_KeepDeepLinksMonthsAndBrowserFindReachable()
    {
        var events = Enumerable.Range(0, 105).Select(i => new
        {
            Id = "chunk-" + i, Type = 6, Text = $"Announcement {i}\n\nThe whole announcement.",
            OccurredAt = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc).AddDays(-i).ToString("O")
        });
        await using var context = await OpenAsync("/events?event=chunk-89", JsonSerializer.Serialize(events));
        var page = context.Pages[0];
        var target = page.Locator("tr.main-row[data-event-id='chunk-89']");
        await Assertions.Expect(target).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));
        await page.Keyboard.PressAsync("Control+f");
        await Assertions.Expect(page.Locator(".main-row")).ToHaveCountAsync(105);
        Assert.True(await page.EvaluateAsync<bool>("() => [...document.querySelectorAll('#eventsTable tbody')].every(b=>b.dataset.month && b.querySelectorAll('.events-month-row').length===1)"));
        await page.EvaluateAsync("async()=>await loadEventsTable(null,false,null,true,{live:true})");
        await Assertions.Expect(page.Locator(".main-row")).ToHaveCountAsync(105);
        await Assertions.Expect(target).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));
    }

    [Fact]
    public async Task ClockAndVisibility_RefreshAtLocalDayBoundaryAndStopWhenHidden()
    {
        await using var context = await OpenAsync();
        var page = context.Pages[0];
        await page.Clock.InstallAsync(new() { Time = "2026-10-24T15:59:30Z" });
        await page.EvaluateAsync("""
            () => {
                window.feedFetchCount=0; const original=fetchPublicJson;
                window.fetchPublicJson=url=>{feedFetchCount++;return original(url)};
                Object.defineProperty(document,'hidden',{configurable:true,get:()=>true});
                document.dispatchEvent(new Event('visibilitychange'));
            }
            """);
        await page.Clock.FastForwardAsync(120000);
        Assert.Equal(0, await page.EvaluateAsync<int>("feedFetchCount"));
        await page.EvaluateAsync("() => {Object.defineProperty(document,'hidden',{configurable:true,get:()=>false});document.dispatchEvent(new Event('visibilitychange'));}");
        await Assertions.Expect(page.Locator(".event-rel")).ToContainTextAsync("2 days ago");
        await page.WaitForFunctionAsync("feedFetchCount===2");
        await page.EvaluateAsync("() => dispatchEvent(new PageTransitionEvent('pagehide'))");
        await page.Clock.FastForwardAsync(120000);
        Assert.Equal(2, await page.EvaluateAsync<int>("feedFetchCount"));
        await page.EvaluateAsync("() => dispatchEvent(new PageTransitionEvent('pageshow',{persisted:true}))");
        await page.Clock.FastForwardAsync(90001);
        await page.WaitForFunctionAsync("feedFetchCount===4");
    }

    [Fact]
    public async Task Preview_KeyboardEntersBadgesAndEscapeReturnsToItsOpener()
    {
        await using var context = await OpenAsync();
        var page = context.Pages[0];
        var link = page.Locator(".event-message a[data-athlete-slug='feed-a']");
        await page.Keyboard.PressAsync("Tab");
        await link.FocusAsync();
        await Assertions.Expect(page.Locator("#eventAthletePreview")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".event-athlete-preview-name")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#eventAthletePreview .badge-class")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator(".badge-tip")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".badge-tip")).ToBeHiddenAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#eventAthletePreview")).ToBeHiddenAsync();
        await Assertions.Expect(link).ToBeFocusedAsync();
        await page.WaitForTimeoutAsync(550);
        await Assertions.Expect(page.Locator("#eventAthletePreview")).ToBeHiddenAsync();
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_LongPressSuppressesOnlyThatTap_AndPortraitFlightCleansUp(bool reduced)
    {
        await using var context = await OpenAsync(width:390,height:844,reduced:reduced);
        var page=context.Pages[0];
        var link=page.Locator(".event-message a[data-athlete-slug='feed-a']");
        await link.ScrollIntoViewIfNeededAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        await page.EvaluateAsync("""
            () => {
                window.profileOpens=0;
                window.portraitFlights=[];
                new MutationObserver(records=>records.forEach(record=>record.addedNodes.forEach(node=>{
                    if(node.nodeType===1&&node.matches('.event-portrait-flight'))
                        portraitFlights.push([parseFloat(node.style.left),parseFloat(node.style.top),parseFloat(node.style.width),parseFloat(node.style.height)]);
                }))).observe(document.body,{childList:true});
                document.addEventListener('lwc:athlete-dialog-before-open',()=>profileOpens++);
                // Inspect the normal link gesture without navigating to a synthetic athlete.
                document.addEventListener('click',event=>{
                    const link=event.target.closest('.events-board a[data-athlete-slug]');
                    if(!link)return;
                    event.preventDefault();event.stopImmediatePropagation();
                    link.dispatchEvent(new CustomEvent('lwc:athlete-dialog-before-open',{bubbles:true,detail:{athleteSlug:'feed-a'}}));
                },true);
            }
            """);
        var box=(await link.BoundingBoxAsync())!;
        var cdp=await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Input.dispatchTouchEvent",new(){{"type","touchStart"},{"touchPoints",new[]{new{x=box.X+box.Width/2,y=box.Y+box.Height/2}}}});
        await Assertions.Expect(page.Locator("#eventAthletePreview")).ToBeVisibleAsync();
        await cdp.SendAsync("Input.dispatchTouchEvent",new(){{"type","touchEnd"},{"touchPoints",Array.Empty<object>()}});
        Assert.Equal(0,await page.EvaluateAsync<int>("profileOpens"));
        var badge=page.Locator("#eventAthletePreview .badge-class");
        await badge.TapAsync();
        await Assertions.Expect(page.Locator(".badge-tip")).ToBeVisibleAsync();
        Assert.Equal(0,await page.EvaluateAsync<int>("profileOpens"));
        await page.Keyboard.PressAsync("Escape");
        var portrait=(await page.Locator(".event-athlete-preview-portrait img").BoundingBoxAsync())!;
        await page.Locator(".event-athlete-preview-name").TapAsync();
        Assert.Equal(1,await page.EvaluateAsync<int>("profileOpens"));
        await Assertions.Expect(page.Locator("#eventAthletePreview")).ToBeHiddenAsync();
        var flights=await page.EvaluateAsync<float[][]>("portraitFlights");
        if(!reduced)
        {
            var flight=Assert.Single(flights);
            Assert.InRange(Math.Abs(flight[0]-portrait.X),0,1);
            Assert.InRange(Math.Abs(flight[1]-portrait.Y),0,1);
            Assert.InRange(Math.Abs(flight[2]-portrait.Width),0,1);
            Assert.InRange(Math.Abs(flight[3]-portrait.Height),0,1);
        }
        else Assert.Empty(flights);
        await Assertions.Expect(page.Locator(".event-portrait-flight")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".is-lifted")).ToHaveCountAsync(0);
        await link.TapAsync();
        Assert.Equal(2,await page.EvaluateAsync<int>("profileOpens"));
    }

    [Fact]
    public async Task StorageDenialAndReducedMotion_DoNotBlockRowsOrPreviewCleanup()
    {
        await using var context=await NewContextAsync(Browser,App,new(){ViewportSize=new(){Width=390,Height=844},ReducedMotion=ReducedMotion.Reduce});
        await context.AddInitScriptAsync("Object.defineProperty(window,'localStorage',{get(){throw new DOMException('Denied','SecurityError')}})");
        await context.RouteAsync("**/api/events",r=>r.FulfillAsync(new(){ContentType="application/json",Body=Events}));
        await context.RouteAsync("**/api/data/athletes",r=>r.FulfillAsync(new(){ContentType="application/json",Body=Athletes}));
        var page=await context.NewPageAsync();
        var errors=new List<string>();page.PageError+=(_,message)=>errors.Add(message);
        await NavigateAndSettleAsync(page,"/events");
        await Assertions.Expect(page.Locator(".main-row")).ToHaveCountAsync(2);
        Assert.Equal(0,await page.Locator(".main-row:not(.is-visible)").CountAsync());
        await page.Locator(".event-message a[data-athlete-slug='feed-a']").HoverAsync();
        await Assertions.Expect(page.Locator("#eventAthletePreview")).ToBeVisibleAsync();
        await page.EvaluateAsync("()=>dispatchEvent(new PageTransitionEvent('pagehide'))");
        await Assertions.Expect(page.Locator("#eventAthletePreview")).ToBeHiddenAsync();
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Preview_InShortLandscapeKeepsTheLastBadgeReachableInsideTheViewport()
    {
        // The six real badge labels in Wen Z's preview reproduce the landscape
        // overflow without shortening the name, metadata or explanations.
        const string athletes = """
            [{"Key":"feed-a","Name":"Wen Z","CurrentPlacement":4,"ProfilePic":"/assets/favicon-64x64.png","Flag":"Rapa Nui (Easter Island)","Division":"Open",
              "Badges":[{"Label":"Age reduction","LeagueCategory":"Division","LeagueValue":"Open","Place":1},
                        {"Label":"Age reduction","LeagueCategory":"Generation","LeagueValue":"Gen Z","Place":1},
                        {"Label":"Best domain – liver","LeagueCategory":"Global","Place":1},
                        {"Label":"Best domain – vitamin D","LeagueCategory":"Global","Place":1},
                        {"Label":"Bortz Age – lowest","LeagueCategory":"Global","Place":1},
                        {"Label":"Bortz pace of aging","LeagueCategory":"Global","Place":1}]}]
            """;
        await using var context=await OpenAsync(athletes:athletes,width:844,height:390,dark:true);
        var page=context.Pages[0];
        await page.EvaluateAsync("()=>document.documentElement.style.fontSize='24px'");
        var link=page.Locator(".event-message a[data-athlete-slug='feed-a']");
        await link.ScrollIntoViewIfNeededAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        await link.HoverAsync();
        var card=page.Locator("#eventAthletePreview");
        await Assertions.Expect(card).ToBeVisibleAsync();
        Assert.True(await card.EvaluateAsync<bool>("c=>{const r=c.getBoundingClientRect();return r.top>=8&&r.bottom<=innerHeight-8&&c.scrollWidth===c.clientWidth}"));
        var last=card.Locator(".badge-class").Last;
        await last.FocusAsync();
        Assert.True(await last.EvaluateAsync<bool>("e=>{const a=e.getBoundingClientRect(),b=e.closest('#eventAthletePreview').getBoundingClientRect();return a.top>=b.top&&a.bottom<=b.bottom}"));
        await Assertions.Expect(page.Locator(".badge-tip")).ToBeVisibleAsync();
    }


    [Theory]
    [InlineData(1440,900,false,16)]
    [InlineData(1440,900,true,16)]
    [InlineData(390,844,false,16)]
    [InlineData(390,844,true,16)]
    [InlineData(320,640,false,32)]
    [InlineData(844,390,true,32)]
    public async Task PreviewAndBadge_ShortContentFitsAndLongContentRemainsReachable(int width,int height,bool dark,int fontSize)
    {
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true,IgnoreDefaultArgs=["--hide-scrollbars"]});
        await using var context=await OpenAsync(width:width,height:height,dark:dark,browser:browser);
        var page=context.Pages[0];
        await page.EvaluateAsync("size=>document.documentElement.style.fontSize=size+'px'",fontSize);
        await page.EvaluateAsync("()=>document.fonts.ready");
        var link=page.Locator(".event-message a[data-athlete-slug='feed-a']");
        await link.ScrollIntoViewIfNeededAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        await page.Keyboard.PressAsync("Tab");
        await link.FocusAsync();
        var card=page.Locator("#eventAthletePreview");
        await Assertions.Expect(card).ToBeVisibleAsync();
        var problems=await card.EvaluateAsync<string[]>("""
            card=>{
                const issues=[],r=card.getBoundingClientRect();
                if(r.left<0||r.right>innerWidth||r.top<0||r.bottom>innerHeight)issues.push('preview leaves viewport');
                if(card.scrollWidth>card.clientWidth)issues.push('preview clips horizontally');
                if(innerHeight>=640&&card.scrollHeight>card.clientHeight)issues.push('short preview needs scrolling');
                for(const el of card.querySelectorAll('.event-athlete-preview-name,.event-athlete-preview-meta')){
                    const range=document.createRange();range.selectNodeContents(el);
                    for(const box of range.getClientRects())if(box.width>1&&(box.left<r.left||box.right>r.right))issues.push('text leaves card');
                }
                return issues;
            }
            """);
        Assert.True(problems.Length==0,string.Join("; ",problems));
        var badge=page.Locator("#eventAthletePreview .badge-class");
        await badge.FocusAsync();
        var tip=page.Locator(".badge-tip");
        await Assertions.Expect(tip).ToBeVisibleAsync();
        var fit=await tip.EvaluateAsync<int[]>("tip=>{const c=tip.querySelector('.badge-tip-content');return[c.clientHeight,c.scrollHeight,c.clientWidth,c.scrollWidth]}");
        Assert.Equal(fit[0],fit[1]);
        Assert.Equal(fit[2],fit[3]);
        await page.Keyboard.PressAsync("Escape");
        await badge.EvaluateAsync("el=>el.setAttribute('title',Array.from({length:14},(_,i)=>'The Regular: two or more tests submitted').join('\\n'))");
        await page.Locator(".event-athlete-preview-name").FocusAsync();
        await badge.FocusAsync();
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await tip.EvaluateAsync("tip=>{const c=tip.querySelector('.badge-tip-content');c.scrollTop=c.scrollHeight}");
        Assert.True(await tip.EvaluateAsync<bool>("tip=>{const c=tip.querySelector('.badge-tip-content');return c.scrollHeight>c.clientHeight&&Math.abs(c.scrollTop+c.clientHeight-c.scrollHeight)<=1}"));
    }
}
