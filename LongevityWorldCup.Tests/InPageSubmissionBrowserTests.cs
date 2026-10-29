using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class InPageSubmissionBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task UnchangedMissingPortrait_DoesNotBlockOtherProfileChanges()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.RouteAsync("**/api/**", route => route.Request.Method == "GET" ? route.FallbackAsync()
            : route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" }));
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete',JSON.stringify({Name:'Recovery Athlete',DisplayName:'Recovery Athlete',
                ProfilePic:null,Division:"Men's",Flag:'United Kingdom',MediaContact:'press@example.test',
                Why:'More healthy years with my family.',Biomarkers:[]}));
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/edit-profile");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view')");
        await page.Locator("#whyDisplayInput").FillAsync("More healthy years exploring the world with my family.");
        await page.EvaluateAsync("""
            () => {
                window.trySendApplicationSubmissionReport=async()=>{};
                window.sentProfileChange=null;
                window.submitApplicationWithRecovery=data=>{
                    window.sentProfileChange=structuredClone(data);
                    return new Promise(()=>{});
                };
            }
            """);
        await page.Locator("#submitButton").ClickAsync();
        await page.WaitForFunctionAsync("() => window.sentProfileChange !== null");
        Assert.Equal("", await page.EvaluateAsync<string>("() => sentProfileChange.profilePic"));
        Assert.Equal("More healthy years exploring the world with my family.",
            await page.EvaluateAsync<string>("() => sentProfileChange.why"));
        await Assertions.Expect(page.Locator("#changeProfilePicButtonError")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("apply", false, 390)]
    [InlineData("apply", true, 390)]
    [InlineData("apply", true, 195)]
    [InlineData("edit", false, 1440)]
    [InlineData("edit", true, 1440)]
    [InlineData("edit", true, 195)]
    public async Task InvalidPortrait_ReturnsToThePhotoAndPreservesTheApplication(string flow, bool rejectedByServer, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.RouteAsync("**/api/**", route => route.Request.Method == "GET" ? route.FallbackAsync()
            : route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" }));
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete',JSON.stringify({Name:'Recovery Athlete',DisplayName:'Recovery Athlete',
                ProfilePic:'/assets/content-images/play-athlete-placeholder.jpg',Division:"Men's",Flag:'United Kingdom',
                MediaContact:'press@example.test',Why:'More healthy years with my family.',Biomarkers:[]}));
            sessionStorage.setItem('biomarkerData',JSON.stringify({DateOfBirth:{Year:1980,Month:5,Day:20},
                Biomarkers:[{Date:'2026-09-01',AlbGL:45,GluMmolL:5.1}]}));
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync(flow == "apply" ? "/apply?fake=1" : "/edit-profile");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view')");
        if (flow == "apply")
        {
            await page.WaitForFunctionAsync("() => !document.getElementById('nextButton').disabled");
            await page.EvaluateAsync("() => { currentStage=7; goToStage(7); }");
        }
        else await page.Locator("#whyDisplayInput").FillAsync("More healthy years exploring the world with my family.");
        var before = await page.EvaluateAsync<string>("""
            ({flow,rejectedByServer}) => {
                const invalid=rejectedByServer?'data:image/png;base64,AA==':'data:,';
                if(flow==='apply') {
                    profilePic=invalid;
                    const canvas=document.createElement('canvas');canvas.width=canvas.height=16;
                    proofPics=[canvas.toDataURL('image/png'),canvas.toDataURL('image/png')];
                } else {
                    athlete.ProfilePic=invalid;
                    setProfilePictureValue(invalid);
                }
                const payload=flow==='apply'?collectApplicantData():{name:athlete.Name,why:athlete.Why,mediaContact:athlete.MediaContact};
                window.recoveryPayloads=[];
                window.trySendApplicationSubmissionReport=async()=>{};
                window.submitApplicationWithRecovery=async data=>{
                    recoveryPayloads.push(structuredClone(data));
                    if(rejectedByServer&&recoveryPayloads.length===1)
                        return {ok:false,response:new Response(JSON.stringify({title:'Check your profile picture',
                            detail:'Please choose your profile picture again.',field:'profilePic',code:'profile_image_processing_failed'}),
                            {status:400,headers:{'content-type':'application/problem+json'}})};
                    return {ok:true,submitResult:{success:true,paymentRequired:false}};
                };
                return JSON.stringify(payload);
            }
            """, new { flow, rejectedByServer });
        await page.Locator(flow == "apply" ? "#nextButton" : "#submitButton").ClickAsync();
        var upload = page.Locator(flow == "apply" ? "#uploadButton" : "#changeProfilePicButton");
        var error = page.Locator(flow == "apply" ? "#profilePictureError" : "#changeProfilePicButtonError");
        await Assertions.Expect(error).ToHaveTextAsync("Please choose your photo again.");
        await Assertions.Expect(error).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("""
            ({errorId,uploadId}) => [errorId,uploadId].every(id => {
                const element=document.getElementById(id),rect=element.getBoundingClientRect();
                return element.contains(document.elementFromPoint(rect.x+rect.width/2,rect.y+rect.height/2));
            })
            """, new { errorId = flow == "apply" ? "profilePictureError" : "changeProfilePicButtonError",
                uploadId = flow == "apply" ? "uploadButton" : "changeProfilePicButton" });
        await Assertions.Expect(upload).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator(flow == "apply" ? "#nextButton" : "#submitButton")).ToBeDisabledAsync();
        if (flow == "apply")
        {
            Assert.Equal(4, await page.EvaluateAsync<int>("() => currentStage"));
            var after = await page.EvaluateAsync<string>("() => JSON.stringify({...collectApplicantData(),profilePic:'PLACEHOLDER'})");
            var expected = System.Text.Json.Nodes.JsonNode.Parse(before)!;
            expected["profilePic"] = "PLACEHOLDER";
            Assert.Equal(System.Text.Json.Nodes.JsonNode.Parse(expected.ToJsonString())!.ToJsonString(),
                System.Text.Json.Nodes.JsonNode.Parse(after)!.ToJsonString());
            Assert.False(await page.EvaluateAsync<bool>("() => isApplicationSubmitting"));
        }
        else
        {
            Assert.True(await page.Locator("#character-title").EvaluateAsync<bool>(
                "title => title.getBoundingClientRect().width >= 100 && title.getBoundingClientRect().height < 100"),
                "Photo feedback must not squeeze the athlete name into a vertical column.");
            Assert.Equal(before, await page.EvaluateAsync<string>("() => JSON.stringify({name:athlete.Name,why:athlete.Why,mediaContact:athlete.MediaContact})"));
            Assert.False(await page.EvaluateAsync<bool>("() => isEditProfileSubmitting"));
        }
        Assert.Equal(rejectedByServer ? 1 : 0, await page.EvaluateAsync<int>("() => recoveryPayloads.length"));
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
        await page.Locator(flow == "apply" ? "#profilePicInput" : "#profilePicInputModal").SetInputFilesAsync(new FilePayload
        {
            Name = "replacement.png", MimeType = "image/png",
            Buffer = Convert.FromBase64String(await page.EvaluateAsync<string>("() => {const c=document.createElement('canvas');c.width=c.height=64;return c.toDataURL('image/png').split(',')[1]}"))
        });
        var crop = page.Locator(flow == "apply" ? "#cropButton" : "#changeProfileCropButton");
        await Assertions.Expect(crop).ToBeEnabledAsync();
        await crop.ClickAsync();
        await Assertions.Expect(error).ToBeHiddenAsync();
        if (flow == "apply")
        {
            await page.WaitForFunctionAsync("() => !applicationProfilePending");
            await page.EvaluateAsync("() => {currentStage=7;goToStage(7);}");
        }
        await page.Locator(flow == "apply" ? "#nextButton" : "#submitButton").ClickAsync();
        await page.WaitForFunctionAsync("expected => recoveryPayloads.length===expected", rejectedByServer ? 2 : 1);
        Assert.StartsWith("data:image/", await page.EvaluateAsync<string>("() => recoveryPayloads.at(-1).profilePic"));
        var finalPayload = await page.EvaluateAsync<string>("flow => JSON.stringify(flow==='apply'?{...recoveryPayloads.at(-1),submissionId:undefined,profilePic:'PLACEHOLDER'}:{name:recoveryPayloads.at(-1).name,why:recoveryPayloads.at(-1).why,mediaContact:recoveryPayloads.at(-1).mediaContact})", flow);
        var finalExpected = System.Text.Json.Nodes.JsonNode.Parse(before)!;
        if (flow == "apply") finalExpected["profilePic"] = "PLACEHOLDER";
        Assert.Equal(finalExpected.ToJsonString(), System.Text.Json.Nodes.JsonNode.Parse(finalPayload)!.ToJsonString());
    }

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
        // Native click actionability can finish docking the newly enabled action.
        // Compare the actual input layout with the pending state, not the earlier
        // inline layout (408px) with its normal desktop dock slot (304px). Use the
        // border-box layout width so the intentional press scale is not a resize.
        await action.EvaluateAsync("""
            el => el.addEventListener('pointerdown', () => {
                el.dataset.submissionWidthAtInput = String(el.offsetWidth);
            }, {capture:true, once:true})
            """);
        await action.ClickAsync();
        await page.WaitForFunctionAsync("() => window.submissionCount===1");
        await Assertions.Expect(action).ToBeVisibleAsync();
        await Assertions.Expect(action).ToBeDisabledAsync();
        await Assertions.Expect(action).ToHaveTextAsync(label);
        var initialWidth = await action.EvaluateAsync<double>("el=>Number(el.dataset.submissionWidthAtInput)");
        Assert.True(double.IsFinite(initialWidth) && initialWidth > 0, "The native input width was not captured.");
        Assert.InRange(await action.EvaluateAsync<double>("el=>el.offsetWidth") - initialWidth, -1, 1);
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
