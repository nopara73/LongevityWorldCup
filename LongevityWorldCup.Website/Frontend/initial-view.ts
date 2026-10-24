interface InitialViewApi {
    prepare(): void;
    complete(): void;
    hold(): void;
    fail(): void;
    reload(): void;
    run(initialize: () => void | Promise<void>): void;
}

interface Window {
    LwcInitialView: InitialViewApi;
}

(function () {
    const documentRoot = document.documentElement;
    let deadline = 0;
    let recoveryObserver: MutationObserver | null = null;
    let revealTimer = 0;

    function hold(): void {
        window.clearTimeout(deadline);
        deadline = 0;
        recoveryObserver?.disconnect();
        recoveryObserver = null;
    }

    function prepare(): void {
        window.clearTimeout(revealTimer);
        delete documentRoot.dataset.lwcRevealed;
        documentRoot.dataset.initialView = 'pending';
        document.querySelectorAll<HTMLElement>('.initial-view-main').forEach(view => {
            view.setAttribute('aria-busy', 'true');
        });
        // A cached view already has controls and computed styles. Refresh them
        // before a reload freezes that document while its replacement loads.
        window.LwcFlowActionDock?.refreshNow?.();
        hold();
        // A missing script or stalled module must not leave an indefinite blank
        // task. Keep its defaults hidden and offer a reload without losing data.
        deadline = window.setTimeout(fail, 15000);
    }

    // A resolved task fades in over its placeholder instead of replacing it in
    // one frame. Input is never blocked; reduced motion skips the handover.
    function reveal(): void {
        window.clearTimeout(revealTimer);
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        documentRoot.dataset.lwcRevealed = '';
        const style = getComputedStyle(documentRoot);
        const duration = parseFloat(style.getPropertyValue('--lwc-duration-normal')) || 220;
        const easing = style.getPropertyValue('--lwc-ease').trim() || 'ease';
        // Animations compose over each panel's own CSS motion without restarting it.
        document.querySelectorAll<HTMLElement>('.initial-view-main > :not(.initial-view-recovery, .play-start-panel, [hidden])').forEach(panel => {
            panel.animate?.([{ opacity: 0 }, { opacity: 1 }], { duration, easing });
        });
        revealTimer = window.setTimeout(() => {
            delete documentRoot.dataset.lwcRevealed;
        }, duration + 80);
    }

    function complete(): void {
        hold();
        if (documentRoot.dataset.initialView === 'pending') reveal();
        delete documentRoot.dataset.initialView;
        document.querySelectorAll<HTMLElement>('.initial-view-main').forEach(view => {
            view.setAttribute('aria-busy', 'false');
            view.querySelector('.initial-view-recovery')?.remove();
        });
        window.LwcFlowActionDock?.refreshNow?.();
    }

    function reload(): void {
        prepare();
        // Paint the loading state before navigation freezes a cached document.
        window.requestAnimationFrame(() => {
            window.requestAnimationFrame(() => window.location.reload());
        });
    }

    function fail(): void {
        hold();
        if (documentRoot.dataset.initialView !== 'pending' && documentRoot.dataset.initialView !== 'failed') return;
        documentRoot.dataset.initialView = 'failed';
        const views = document.querySelectorAll<HTMLElement>('.initial-view-main');
        if (!views.length) {
            // The head can time out before parsing reaches the task. Mount
            // recovery when it arrives, even if later scripts still block DCL.
            recoveryObserver = new MutationObserver(() => {
                if (document.querySelector('.initial-view-main')) fail();
            });
            recoveryObserver.observe(documentRoot, { childList: true, subtree: true });
        }
        views.forEach(view => {
            view.setAttribute('aria-busy', 'false');
            if (view.querySelector('.initial-view-recovery')) return;
            const recovery = document.createElement('section');
            recovery.className = 'initial-view-recovery';
            recovery.setAttribute('role', 'alert');
            const message = document.createElement('p');
            message.textContent = 'This page couldn’t load.';
            const retry = document.createElement('button');
            retry.type = 'button';
            retry.textContent = 'Retry';
            retry.addEventListener('click', () => window.location.reload());
            recovery.append(message, retry);
            view.append(recovery);
        });
    }

    function run(initialize: () => void | Promise<void>): void {
        document.querySelectorAll<HTMLElement>('.initial-view-main').forEach(view => {
            view.setAttribute('aria-busy', 'true');
        });
        const onFailure = (error: unknown): void => {
            console.error('Unable to initialize this view:', error);
            fail();
        };
        try {
            Promise.resolve(initialize()).catch(onFailure);
        } catch (error) {
            onFailure(error);
        }
    }

    // A parser-blocking dependency can outlast the deadline before the task
    // markup exists. Mount recovery once that markup arrives, too.
    window.addEventListener('DOMContentLoaded', () => {
        if (documentRoot.dataset.initialView === 'failed') fail();
    }, { once: true });

    window.LwcInitialView = { prepare, complete, hold, fail, reload, run };
})();
