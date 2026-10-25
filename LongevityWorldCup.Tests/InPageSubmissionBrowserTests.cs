using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class InPageSubmissionBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("submit")]
    [InlineData("recovery")]
    [InlineData("error")]
    public async Task SubmissionDeadline_CoversResponseBodiesAndPreservesRecovery(string stalled)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.RouteAsync("**/api/**", route => route.Request.Method == "GET" ? route.FallbackAsync()
            : route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof window.submitApplicationWithRecovery==='function'");
        await page.Clock.InstallAsync();
        await page.EvaluateAsync("""
            stalled => {
                const originalFetch=window.fetch;
                window.applicationRequests=[];
                window.bodyAborts=0;
                window.deadlineResult=null;
                window.fetch=async (input,init) => {
                    const path=new URL(String(input),location.href).pathname;
                    if(!['/api/application/application','/api/application/submission-status'].includes(path))
                        return originalFetch(input,init);
                    const body=JSON.parse(init.body);
                    applicationRequests.push({path,submissionId:body.submissionId});
                    const first=applicationRequests.length===1;
                    const shouldStall=stalled==='recovery' ? path.endsWith('submission-status') : first;
                    if(shouldStall) {
                        const stream=new ReadableStream({start(controller) {
                            init.signal.addEventListener('abort',()=>{
                                bodyAborts++;
                                controller.error(new DOMException('Aborted','AbortError'));
                            },{once:true});
                        }});
                        return new Response(stream,{status:stalled==='error'?422:200});
                    }
                    if(stalled==='recovery' && first) return new Response('Unavailable',{status:503});
                    return new Response(JSON.stringify({success:true,paymentRequired:false}),{
                        status:200,headers:{'content-type':'application/json'}
                    });
                };
                void submitApplicationWithRecovery({submissionId:'mock-body-deadline'},1000)
                    .then(attempt=>window.deadlineResult={ok:attempt.ok,recovered:attempt.recovered});
            }
            """, stalled);
        await page.WaitForFunctionAsync("() => applicationRequests.length>0");
        await page.Clock.RunForAsync(1001);
        await page.WaitForFunctionAsync("() => window.deadlineResult!==null", options: new() { Timeout = 1500 });
        Assert.True(await page.EvaluateAsync<bool>("() => deadlineResult.ok"));
        Assert.Equal(stalled != "recovery", await page.EvaluateAsync<bool>("() => deadlineResult.recovered"));
        Assert.Equal(1, await page.EvaluateAsync<int>("() => bodyAborts"));
        Assert.Equal(stalled == "recovery" ? 3 : 2, await page.EvaluateAsync<int>("() => applicationRequests.length"));
        Assert.True(await page.EvaluateAsync<bool>("() => applicationRequests.every(r=>r.submissionId==='mock-body-deadline')"));
    }

    [Theory]
    [InlineData("apply", "success", 1440)]
    [InlineData("apply", "error", 390)]
    [InlineData("apply", "abort", 390)]
    [InlineData("edit", "success", 390)]
    [InlineData("edit", "error", 1440)]
    [InlineData("edit", "error", 195)]
    [InlineData("edit", "error", 187)]
    [InlineData("edit", "abort", 390)]
    [InlineData("proof", "success", 1440)]
    [InlineData("proof", "error", 390)]
    [InlineData("proof", "abort", 390)]
    public async Task Submission_TerminalPathsKeepTheActionAndReleaseBusyState(string flow, string outcome, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = width <= 195 ? 422 : 900 },
            ColorScheme = width <= 390 ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.NoPreference
        });
        await context.RouteAsync("**/api/**", route => route.Request.Method == "GET" ? route.FallbackAsync()
            : route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" }));
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete', JSON.stringify({Name:'Wait Test Athlete',DisplayName:'Wait Test Athlete',
                ProfilePic:'/assets/content-images/play-athlete-placeholder.jpg',Division:"Men's",Flag:'United Kingdom',
                MediaContact:'press@example.test',Why:'More healthy years with my family.',Biomarkers:[]}));
            sessionStorage.setItem('biomarkerData',JSON.stringify({DateOfBirth:{Year:1980,Month:5,Day:20},Biomarkers:[{Date:'2026-09-01',AlbGL:45,GluMmolL:5.1}]}));
            """);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(flow == "apply" ? "/apply?fake=1" : flow == "edit" ? "/edit-profile" : "/proofs",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcWait && !document.documentElement.hasAttribute('data-initial-view')");
        if (flow == "apply")
        {
            await page.WaitForFunctionAsync("() => !document.getElementById('nextButton').disabled");
            await page.EvaluateAsync("() => { currentStage=7; goToStage(7); }");
            await page.Locator("#accountEmail").FillAsync("wait@example.test");
        }
        else if (flow == "edit")
        {
            await page.WaitForFunctionAsync("() => document.querySelector('#mediaContactInput')?.value==='press@example.test'");
            await page.Locator("#whyDisplayInput").FillAsync("More healthy years exploring the world with my family.");
        }
        else
        {
            await page.WaitForFunctionAsync("() => document.querySelector('#proofPicInput')?.hasAttribute('data-listener')");
            var image = await page.EvaluateAsync<string>("() => {const c=document.createElement('canvas');c.width=300;c.height=400;return c.toDataURL('image/png').split(',')[1]}");
            await page.Locator("#proofPicInput").SetInputFilesAsync(new FilePayload
            {
                Name = "Synthetic regression proof.png", MimeType = "image/png", Buffer = Convert.FromBase64String(image)
            });
            await page.WaitForFunctionAsync("() => !document.getElementById('proofPicInput').disabled");
            await page.EvaluateAsync("() => document.querySelectorAll('.biomarker-checkbox').forEach(box=>{box.checked=true;box.dispatchEvent(new Event('change',{bubbles:true}));})");
        }
        await page.EvaluateAsync("""
            () => {
                window.trySendApplicationSubmissionReport=async()=>{};
                window.submissionCount=0;
                window.submitApplicationWithRecovery=()=>{submissionCount++;return new Promise((resolve,reject)=>{
                    window.finishSubmission=outcome=>outcome==='abort' ? reject(new DOMException('Connection interrupted','AbortError'))
                        : resolve(outcome==='success' ? {ok:true,submitResult:{success:true,paymentRequired:false}}
                            : {ok:false,response:new Response('Unavailable.',{status:503,headers:{'content-type':'text/plain'}})});
                });};
            }
            """);
        var action = page.Locator(flow == "apply" ? "#nextButton" : "#submitButton");
        var label = await action.InnerTextAsync();
        var initialWidth = await action.EvaluateAsync<double>("el=>el.getBoundingClientRect().width");
        await action.ClickAsync();
        await page.WaitForFunctionAsync("() => window.submissionCount===1");
        await Assertions.Expect(action).ToBeVisibleAsync();
        await Assertions.Expect(action).ToBeDisabledAsync();
        await Assertions.Expect(action).ToHaveTextAsync(label);
        Assert.InRange(await action.EvaluateAsync<double>("el=>el.getBoundingClientRect().width") - initialWidth, -1, 1);
        if (width <= 195)
        {
            // A 390px phone at native 200% zoom has a 195px layout viewport,
            // with 187px usable when its native scrollbar occupies 8px.
            // Ordinary words must fit even though scroll/client sizes can agree.
            Assert.True(await action.EvaluateAsync<bool>("""
                el => {
                    const text=el.querySelector('.flow-action__label').firstChild;
                    const start=text.textContent.indexOf('request');
                    const range=document.createRange();range.setStart(text,start);range.setEnd(text,start+7);
                    return range.getClientRects().length===1;
                }
                """));
        }
        if (flow == "edit" && width == 1440)
        {
            await page.SetViewportSizeAsync(320, 640);
            await page.EvaluateAsync("() => new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))");
            Assert.True(await action.EvaluateAsync<bool>("el=>{const r=el.getBoundingClientRect();return r.width>0 && r.left>=0 && r.right<=innerWidth}"));
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth<=innerWidth"));
        }
        Assert.False(await page.EvaluateAsync<bool>("() => document.querySelector('main').inert"));
        Assert.Equal("Submitting...", await page.Locator(".lwc-wait-status").InnerTextAsync());
        if (flow != "proof")
        {
            var input = page.Locator(flow == "apply" ? "#accountEmail" : "#whyDisplayInput");
            await input.FillAsync(flow == "apply" ? "later@example.test" : "A later unsent edit remains usable.");
            await Assertions.Expect(input).ToBeFocusedAsync();
        }
        Assert.True(await page.EvaluateAsync<bool>("""
            flow => {
                const back=flow==='apply' ? document.getElementById('backButton') : document.querySelector('.back-button');
                back.focus();return document.activeElement===back;
            }
            """, flow));
        await page.EvaluateAsync("flow=>document.querySelector(flow==='apply'?'#nextButton':'#submitButton').click()", flow);
        Assert.Equal(1, await page.EvaluateAsync<int>("() => submissionCount"));
        await page.EvaluateAsync("outcome=>finishSubmission(outcome)", outcome);
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('[data-lwc-wait], .lwc-wait-fill, .lwc-wait-status')");
        Assert.False(await page.EvaluateAsync<bool>("() => document.querySelector('main').inert"));
        Assert.Empty(errors);
    }
}
