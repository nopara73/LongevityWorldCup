using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LeaderboardProofViewerBrowserTests
{
    [Fact]
    public async Task ProofViewer_StopsAtEndsAndRestoresCurrentProofFocus()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync(
            "/",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof populateProofsGallery === 'function'");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name]').length > 0");
        await page.EvaluateAsync(
            """
            () => {
                const modal = document.getElementById('detailsModal');
                modal.style.display = 'block';
                modal.querySelector('.modal-content').dataset.athleteSlug = 'viewer-focus-test';
                history.pushState({ modal: 'details', athlete: 'viewer-focus-test' }, '', '/athlete/viewer-focus-test');
                const gallery = document.createElement('div');
                gallery.id = 'proofsGallery';
                gallery.className = 'proofs-gallery';
                modal.querySelector('.modal-content').appendChild(gallery);
                populateProofsGallery({
                    Proofs: [
                        '/athletes/christopher_yamba/proof_1.webp',
                        '/athletes/christopher_yamba/proof_2.webp',
                        '/athletes/christopher_yamba/proof_3.webp'
                    ]
                });
            }
            """);

        await page.EvaluateAsync(
            """
            () => {
                const firstProof = document.querySelector('#proofsGallery .proof-item img');
                firstProof.focus();
                openEnlargedView(firstProof);
            }
            """);
        var viewer = page.Locator(".enlarged-portrait.show");
        await viewer.WaitForAsync();

        Assert.Equal("region", await viewer.GetAttributeAsync("role"));
        Assert.Equal(1, await page.Locator("#detailsModal > #athleteImageViewer").CountAsync());
        Assert.Equal(1, await page.Locator("[role=dialog]:visible").CountAsync());
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.inert"));

        var previousButton = viewer.Locator(".image-nav--previous");
        var nextButton = viewer.Locator(".image-nav--next");
        var closeButton = viewer.Locator(".close-btn");
        var position = viewer.Locator(".image-position");
        var zoomOutButton = viewer.Locator(".image-zoom-out");
        var zoomInButton = viewer.Locator(".image-zoom-in");
        var fitButton = viewer.Locator(".image-zoom-fit");

        await page.WaitForFunctionAsync(
            "() => { const image = document.querySelector('.enlarged-portrait.show img'); return image?.complete && image.naturalWidth > 0; }");
        var mobileLayout = await viewer.EvaluateAsync<ProofViewerLayoutDiagnostics>(
            """
            element => {
                const stage = element.querySelector('.image-viewer-stage');
                const previous = element.querySelector('.image-nav--previous');
                const next = element.querySelector('.image-nav--next');
                const hint = element.querySelector('.image-viewer-hint');
                const zoomStatus = element.querySelector('.image-zoom-status');
                const stageRect = stage.getBoundingClientRect();
                const previousRect = previous.getBoundingClientRect();
                const nextRect = next.getBoundingClientRect();
                return {
                    StageClientWidth: stage.clientWidth,
                    StageScrollWidth: stage.scrollWidth,
                    StageClientHeight: stage.clientHeight,
                    StageScrollHeight: stage.scrollHeight,
                    NavigationOverlapsStage: previousRect.top < stageRect.bottom || nextRect.top < stageRect.bottom,
                    HintIsVisible: !hint.hidden && getComputedStyle(hint).display !== 'none',
                    ZoomStatus: zoomStatus.textContent.trim()
                };
            }
            """);

        Assert.Equal("Proof 1 of 3", await position.InnerTextAsync());
        Assert.Equal("200%", mobileLayout.ZoomStatus);
        Assert.True(mobileLayout.HintIsVisible);
        Assert.True(
            mobileLayout.StageScrollWidth >= mobileLayout.StageClientWidth * 1.9,
            $"Expected a readable zoomed proof width; client={mobileLayout.StageClientWidth}, scroll={mobileLayout.StageScrollWidth}.");
        Assert.True(mobileLayout.StageScrollHeight >= mobileLayout.StageClientHeight);
        Assert.False(mobileLayout.NavigationOverlapsStage);
        Assert.True(await IsFocusedAsync(closeButton));

        // Freeze the viewer at the production entrance scale so every viewer
        // control remains a valid touch target throughout the motion.
        await viewer.EvaluateAsync(
            """
            element => {
                const entryScale = getComputedStyle(element)
                    .getPropertyValue('--image-viewer-entry-scale')
                    .trim();
                if (!entryScale) throw new Error('Missing image viewer entry scale.');
                element.style.transition = 'none';
                element.style.transform = `scale(${entryScale})`;
                void element.offsetWidth;
            }
            """);
        await AssertTouchTargetAsync(closeButton);
        await AssertTouchTargetAsync(previousButton);
        await AssertTouchTargetAsync(nextButton);
        await AssertTouchTargetAsync(zoomOutButton);
        await AssertTouchTargetAsync(zoomInButton);
        await AssertTouchTargetAsync(fitButton);
        await viewer.EvaluateAsync(
            """
            element => {
                element.style.removeProperty('transition');
                element.style.removeProperty('transform');
                void element.offsetWidth;
            }
            """);
        Assert.True(await viewer.EvaluateAsync<bool>(
            "element => !element.style.transition && !element.style.transform"));
        Assert.False(await previousButton.IsEnabledAsync());
        Assert.True(await nextButton.IsEnabledAsync());

        await page.Keyboard.PressAsync("ArrowLeft");
        Assert.Equal("Proof 1 of 3", await position.InnerTextAsync());
        Assert.Contains("/proof_1.webp", await viewer.Locator("img").GetAttributeAsync("src"));

        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("Proof 2 of 3", await position.InnerTextAsync());
        Assert.Contains("/proof_2.webp", await viewer.Locator("img").GetAttributeAsync("src"));

        await page.Keyboard.PressAsync("End");
        Assert.Equal("Proof 3 of 3", await position.InnerTextAsync());
        Assert.True(await previousButton.IsEnabledAsync());
        Assert.False(await nextButton.IsEnabledAsync());

        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("Proof 3 of 3", await position.InnerTextAsync());
        Assert.Contains("/proof_3.webp", await viewer.Locator("img").GetAttributeAsync("src"));

        await previousButton.ClickAsync();
        Assert.Equal("Proof 2 of 3", await position.InnerTextAsync());

        await page.Keyboard.PressAsync("End");
        await previousButton.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await IsFocusedAsync(closeButton));

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('athleteImageViewer')?.getAttribute('aria-hidden') === 'true'");
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.inert"));
        Assert.Equal("true", await page.Locator(".enlarged-portrait").GetAttributeAsync("aria-hidden"));
        Assert.Equal(
            "Enlarge proof image 3",
            await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('aria-label') || document.activeElement?.tagName"));

        await page.GoForwardAsync();
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        Assert.Equal("Proof 3 of 3", await position.InnerTextAsync());
        Assert.Contains("/proof_3.webp", await viewer.Locator("img").GetAttributeAsync("src"));
        await page.GoBackAsync();
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await page.Locator(".enlarged-portrait").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.inert"));
        Assert.Equal(
            "Enlarge proof image 3",
            await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('aria-label') || document.activeElement?.tagName"));
    }

    [Fact]
    public async Task ProofViewer_HistoryClosesOneLayerAtATime()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync(
            "/",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof populateProofsGallery === 'function'");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name]').length > 0");
        await page.EvaluateAsync(
            """
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'history-test';
                history.pushState({ modal: 'details', athlete: 'history-test' }, '', '/athlete/history-test');

                const gallery = document.createElement('div');
                gallery.id = 'proofsGallery';
                gallery.className = 'proofs-gallery';
                modalContent.appendChild(gallery);
                populateProofsGallery({
                    Proofs: ['/athletes/christopher_yamba/proof_1.webp']
                });
            }
            """);

        var viewer = page.Locator("#athleteImageViewer");

        // A dismissal in the same frame as opening must cancel the queued
        // entrance class instead of resurrecting a visually closed viewer.
        await page.EvaluateAsync(
            """
            () => {
                const proof = document.querySelector('#proofsGallery .proof-item img');
                const viewer = document.getElementById('athleteImageViewer');
                proof.focus();
                openEnlargedView(proof, { pushHistory: false });
                closeEnlargedView(viewer);
            }
            """);
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        Assert.Equal("true", await viewer.GetAttributeAsync("aria-hidden"));
        Assert.False(await viewer.EvaluateAsync<bool>("element => element.classList.contains('show')"));
        Assert.True(await viewer.IsHiddenAsync());
        await AssertDetailsStateAsync(page);

        await page.EvaluateAsync(
            """
            () => {
                const proof = document.querySelector('#proofsGallery .proof-item img');
                proof.focus();
                openEnlargedView(proof);
            }
            """);
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        var initialViewerState = await viewer.EvaluateAsync<ViewerStateDiagnostics>(
            """
            element => ({
                ClassName: element.className,
                Hidden: element.hidden,
                AriaHidden: element.getAttribute('aria-hidden'),
                ModalDisplay: document.getElementById('detailsModal').style.display,
                HistoryModal: history.state?.modal || ''
            })
            """);
        Assert.Contains("show", initialViewerState.ClassName, StringComparison.Ordinal);
        Assert.False(initialViewerState.Hidden);
        Assert.Equal("false", initialViewerState.AriaHidden);
        Assert.Equal("block", initialViewerState.ModalDisplay);
        Assert.Equal("enlarged", initialViewerState.HistoryModal);
        Assert.Equal("enlarged", await page.EvaluateAsync<string>("() => history.state?.modal"));
        Assert.Equal("/athlete/history-test", new Uri(page.Url).AbsolutePath);

        await page.GoBackAsync();
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await AssertDetailsStateAsync(page);

        await page.GoForwardAsync();
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        Assert.Equal("enlarged", await page.EvaluateAsync<string>("() => history.state?.modal"));

        await viewer.Locator(".close-btn").ClickAsync();
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await AssertDetailsStateAsync(page);

        // Guess mode protects the parent modal, not its nested viewer. The
        // visually topmost viewer also closes before an underlying share menu.
        await page.EvaluateAsync(
            """
            () => {
                document.querySelector('#detailsModal .modal-content').classList.add('guess-mode');
                document.getElementById('athleteShareMenu').hidden = false;
                openEnlargedView(document.querySelector('#proofsGallery .proof-item img'));
            }
            """);
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        await page.EvaluateAsync("() => history.go(-2)");
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await page.WaitForFunctionAsync(
            "() => history.state?.modal === 'details' && location.pathname === '/athlete/history-test'");
        await AssertDetailsStateAsync(page);
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.classList.contains('guess-mode')"));
        Assert.False(await page.Locator("#athleteShareMenu").EvaluateAsync<bool>("element => element.hidden"));
        await page.EvaluateAsync("() => closeAthleteShareMenu()");
        await page.GoBackAsync();
        await page.WaitForFunctionAsync(
            "() => history.state?.modal === 'details' && location.pathname === '/athlete/history-test'");
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.classList.contains('guess-mode')"));
        Assert.Equal("block", await page.Locator("#detailsModal").EvaluateAsync<string>("element => element.style.display"));
        await page.EvaluateAsync(
            "() => document.querySelector('#detailsModal .modal-content').classList.remove('guess-mode')");

        await page.EvaluateAsync(
            "() => openEnlargedView(document.querySelector('#proofsGallery .proof-item img'))");
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        await page.Keyboard.PressAsync("Escape");
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await AssertDetailsStateAsync(page);

        await page.EvaluateAsync(
            "() => openEnlargedView(document.querySelector('#proofsGallery .proof-item img'))");
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        var viewerBox = await viewer.BoundingBoxAsync();
        Assert.NotNull(viewerBox);
        await page.Mouse.ClickAsync((float)viewerBox.X + 8, (float)viewerBox.Y + 8);
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await AssertDetailsStateAsync(page);

        await page.EvaluateAsync(
            "() => openEnlargedView(document.querySelector('#proofsGallery .proof-item img'))");
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        await page.GoBackAsync();
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await AssertDetailsStateAsync(page);

        // Manually closing details leaves the nested entry in the browser's
        // forward stack. It must never restore a stale athlete URL without UI.
        await page.EvaluateAsync("() => closeModal()");
        await page.WaitForFunctionAsync("() => document.getElementById('detailsModal')?.style.display === 'none'");
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);

        await page.GoForwardAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/' && !history.state?.modal");
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
        Assert.True(await viewer.IsHiddenAsync());
        Assert.True(await page.Locator("#detailsModal").IsHiddenAsync());

        // Selecting the base entry from the browser Back menu can skip both
        // nested states at once. Reconcile the whole visible stack in that case.
        await page.EvaluateAsync(
            """
            () => {
                const modal = document.getElementById('detailsModal');
                modal.style.display = 'block';
                history.pushState({ modal: 'details', athlete: 'history-test' }, '', '/athlete/history-test');
                openEnlargedView(document.querySelector('#proofsGallery .proof-item img'));
            }
            """);
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        await page.EvaluateAsync("() => history.go(-2)");
        await page.WaitForFunctionAsync(
            "() => location.pathname === '/' && document.getElementById('detailsModal')?.style.display === 'none'");
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
        Assert.True(await viewer.IsHiddenAsync());
        Assert.True(await page.Locator("#detailsModal").IsHiddenAsync());

        // Browser Back/Forward restores both the real athlete details state and
        // its nested image-viewer state without pushing duplicate entries.
        var openedRealAthlete = await page.EvaluateAsync<bool>(
            """
            () => {
                localStorage.setItem('gmaSkipAll', 'true');
                const slug = window.slugifyName('Christopher Yamba', true);
                window.__proofHistoryAthleteSlug = slug;
                return window.openAthleteModalBySlug(slug, { suppressGuessMyAge: true });
            }
            """);
        Assert.True(openedRealAthlete);
        await page.WaitForFunctionAsync(
            """
            () => {
                const content = document.querySelector('#detailsModal .modal-content');
                return history.state?.modal === 'details'
                    && content?.dataset.athleteSlug === window.__proofHistoryAthleteSlug
                    && !content.classList.contains('is-loading');
            }
            """);
        var realAthletePath = new Uri(page.Url).AbsolutePath;
        var expectedRealAthletePath = await page.EvaluateAsync<string>(
            "() => `/athlete/${window.__proofHistoryAthleteSlug}`");
        Assert.Equal(expectedRealAthletePath, realAthletePath);
        var populatedChronologicalAge = await page.Locator("#chronologicalAge").InnerTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(populatedChronologicalAge));

        await page.EvaluateAsync(
            """
            () => {
                const image = document.getElementById('modalProfilePic');
                image.focus();
                openEnlargedView(image);
            }
            """);
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        var realProfileSource = await viewer.Locator("img").GetAttributeAsync("src");
        Assert.False(string.IsNullOrWhiteSpace(realProfileSource));
        await page.GoBackAsync();
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        Assert.Equal("details", await page.EvaluateAsync<string>("() => history.state?.modal"));

        await page.GoBackAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('detailsModal')?.style.display === 'none'");
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);

        var failAthleteRequest = false;
        var omitAthleteFromResponse = false;
        var athleteRequestDelayMilliseconds = 300;
        await page.RouteAsync("**/api/data/athletes", async route =>
        {
            if (failAthleteRequest)
            {
                await Task.Delay(100);
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 503,
                    ContentType = "application/json",
                    Body = "{}"
                });
                return;
            }

            if (omitAthleteFromResponse)
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = "[]"
                });
                return;
            }

            await Task.Delay(athleteRequestDelayMilliseconds);
            await route.ContinueAsync();
        });
        await page.EvaluateAsync("() => { window.__sharedAthletesRequest = null; }");
        await page.GoForwardAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const content = document.querySelector('#detailsModal .modal-content');
                return history.state?.modal === 'details'
                    && content?.dataset.athleteSlug === window.__proofHistoryAthleteSlug
                    && content.classList.contains('is-loading');
            }
            """);
        Assert.Equal(realAthletePath, new Uri(page.Url).AbsolutePath);
        Assert.Equal("block", await page.Locator("#detailsModal").EvaluateAsync<string>("element => element.style.display"));

        await page.GoForwardAsync();
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('is-loading')");
        Assert.Equal("enlarged", await page.EvaluateAsync<string>("() => history.state?.modal"));
        Assert.Equal(realAthletePath, new Uri(page.Url).AbsolutePath);
        Assert.Equal(realProfileSource, await viewer.Locator("img").GetAttributeAsync("src"));

        await page.EvaluateAsync("() => history.go(-2)");
        await page.WaitForFunctionAsync(
            "() => location.pathname === '/' && document.getElementById('detailsModal')?.style.display === 'none'");
        Assert.True(await viewer.IsHiddenAsync());

        await page.EvaluateAsync(
            "() => { window.__sharedAthletesRequest = null; history.go(2); }");
        await page.Locator("#athleteImageViewer.show").WaitForAsync();
        await page.WaitForFunctionAsync(
            "() => history.state?.modal === 'enlarged' && !document.querySelector('#detailsModal .modal-content')?.classList.contains('is-loading')");
        Assert.Equal(realAthletePath, new Uri(page.Url).AbsolutePath);
        Assert.Equal(realProfileSource, await viewer.Locator("img").GetAttributeAsync("src"));

        await page.EvaluateAsync("() => history.go(-2)");
        await page.WaitForFunctionAsync(
            "() => location.pathname === '/' && document.getElementById('detailsModal')?.style.display === 'none'");
        await page.EvaluateAsync(
            "() => { window.__sharedAthletesRequest = null; history.go(2); }");
        await page.WaitForFunctionAsync(
            """
            () => history.state?.modal === 'enlarged'
                && document.querySelector('#detailsModal .modal-content')?.classList.contains('is-loading')
            """);
        await page.GoBackAsync();
        await page.WaitForFunctionAsync(
            """
            () => history.state?.modal === 'details'
                && !document.querySelector('#detailsModal .modal-content')?.classList.contains('is-loading')
            """);
        Assert.True(await viewer.IsHiddenAsync());
        Assert.Equal(realAthletePath, new Uri(page.Url).AbsolutePath);

        await page.GoBackAsync();
        await page.WaitForFunctionAsync(
            "() => location.pathname === '/' && document.getElementById('detailsModal')?.style.display === 'none'");
        failAthleteRequest = true;
        await page.EvaluateAsync(
            "() => { window.__sharedAthletesRequest = null; history.go(2); }");
        await page.WaitForFunctionAsync(
            """
            () => history.state?.modal === 'details'
                && !document.querySelector('#detailsModal .modal-content')?.classList.contains('is-loading')
            """);
        Assert.Equal(realAthletePath, new Uri(page.Url).AbsolutePath);
        Assert.True(await viewer.IsHiddenAsync());
        Assert.True(await page.Locator("#detailsModal").IsVisibleAsync());
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.inert"));
        Assert.Equal("Athlete details unavailable", await page.Locator("#athleteName").InnerTextAsync());
        var loadError = page.Locator("#athleteLoadError");
        Assert.True(await loadError.IsVisibleAsync());
        Assert.Equal("alert", await loadError.GetAttributeAsync("role"));
        Assert.True(await page.Locator("#retryAthleteLoad").IsVisibleAsync());
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.Equal(populatedChronologicalAge, await page.Locator("#chronologicalAge").InnerTextAsync());
        Assert.False(await page.Locator("#athlete-stats").IsVisibleAsync());
        Assert.False(await page.Locator("#athlete-profile .portrait-wrapper").IsVisibleAsync());
        Assert.False(await page.Locator("#athlete-biomarkers").IsVisibleAsync());
        Assert.False(await page.Locator("#athlete-proofs").IsVisibleAsync());
        Assert.Null(await page.Locator("#modalProfilePic").GetAttributeAsync("data-full-src"));

        await page.GoForwardAsync();
        await page.WaitForFunctionAsync(
            """
            () => history.state?.modal === 'enlarged'
                && document.querySelector('#detailsModal .modal-content')?.classList.contains('is-loading')
            """);
        await page.WaitForFunctionAsync(
            """
            () => history.state?.modal === 'details'
                && document.querySelector('#detailsModal .modal-content')?.classList.contains('has-load-error')
            """);
        Assert.True(await viewer.IsHiddenAsync());
        Assert.True(await loadError.IsVisibleAsync());

        await page.GoBackAsync();
        await page.WaitForFunctionAsync(
            "() => location.pathname === '/' && document.getElementById('detailsModal')?.style.display === 'none'");

        failAthleteRequest = false;
        omitAthleteFromResponse = true;
        var openedMissingAthlete = await page.EvaluateAsync<bool>(
            """
            () => {
                window.__sharedAthletesRequest = null;
                return window.openAthleteModalBySlug(window.__proofHistoryAthleteSlug, {
                    suppressGuessMyAge: true
                });
            }
            """);
        Assert.True(openedMissingAthlete);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#detailsModal .modal-content')?.classList.contains('has-load-error')");
        Assert.True(await page.Locator("#athleteLoadError").IsVisibleAsync());

        omitAthleteFromResponse = false;
        await page.Locator("#retryAthleteLoad").ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const content = document.querySelector('#detailsModal .modal-content');
                return history.state?.modal === 'details'
                    && content?.dataset.athleteSlug === window.__proofHistoryAthleteSlug
                    && !content.classList.contains('is-loading')
                    && !content.classList.contains('has-load-error');
            }
            """);
        Assert.Equal(realAthletePath, new Uri(page.Url).AbsolutePath);
        Assert.True(await page.Locator("#athlete-stats").IsVisibleAsync());
        Assert.True(await page.Locator("#athleteLoadError").IsHiddenAsync());

        await page.GoBackAsync();
        await page.WaitForFunctionAsync(
            "() => location.pathname === '/' && document.getElementById('detailsModal')?.style.display === 'none'");
        athleteRequestDelayMilliseconds = 700;
        var delayedResponse = page.WaitForResponseAsync(
            response => response.Url.Contains("/api/data/athletes", StringComparison.OrdinalIgnoreCase));
        var openedBeforeClose = await page.EvaluateAsync<bool>(
            """
            () => {
                window.__sharedAthletesRequest = null;
                return window.openAthleteModalBySlug(window.__proofHistoryAthleteSlug);
            }
            """);
        Assert.True(openedBeforeClose);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#detailsModal .modal-content')?.classList.contains('is-loading')");
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.getElementById('detailsModal')?.style.display === 'none'");
        await delayedResponse;
        await page.WaitForFunctionAsync(
            "() => location.pathname === '/' && !history.state?.modal && document.getElementById('detailsModal')?.style.display === 'none'");
        Assert.True(await viewer.IsHiddenAsync());
    }

    private static async Task AssertDetailsStateAsync(IPage page)
    {
        Assert.Equal("details", await page.EvaluateAsync<string>("() => history.state?.modal"));
        Assert.Equal("/athlete/history-test", new Uri(page.Url).AbsolutePath);
        Assert.Equal("block", await page.Locator("#detailsModal").EvaluateAsync<string>("element => element.style.display"));
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.inert"));
    }

    private static Task<bool> IsFocusedAsync(ILocator locator) =>
        locator.EvaluateAsync<bool>("element => element === document.activeElement");

    private static async Task AssertTouchTargetAsync(ILocator button)
    {
        var box = await button.BoundingBoxAsync();
        Assert.NotNull(box);
        const double renderingTolerance = 0.01;
        Assert.True(box.Width + renderingTolerance >= 44, $"Expected navigation button width of at least 44px, got {box.Width}px.");
        Assert.True(box.Height + renderingTolerance >= 44, $"Expected navigation button height of at least 44px, got {box.Height}px.");
    }

    private sealed class ProofViewerLayoutDiagnostics
    {
        public double StageClientWidth { get; set; }
        public double StageScrollWidth { get; set; }
        public double StageClientHeight { get; set; }
        public double StageScrollHeight { get; set; }
        public bool NavigationOverlapsStage { get; set; }
        public bool HintIsVisible { get; set; }
        public string ZoomStatus { get; set; } = "";
    }

    private sealed class ViewerStateDiagnostics
    {
        public string ClassName { get; set; } = "";
        public bool Hidden { get; set; }
        public string? AriaHidden { get; set; }
        public string ModalDisplay { get; set; } = "";
        public string HistoryModal { get; set; } = "";
    }
}
