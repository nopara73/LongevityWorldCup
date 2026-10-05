interface InitialViewApi {
    prepare(): void;
    complete(): void;
    hold(): void;
    fail(): void;
    run(initialize: () => void | Promise<void>): void;
}

interface Window {
    LwcInitialView: InitialViewApi;
}

(function () {
    const documentRoot = document.documentElement;
    let deadline = 0;

    function hold(): void {
        window.clearTimeout(deadline);
        deadline = 0;
    }

    function prepare(): void {
        documentRoot.dataset.initialView = 'pending';
        hold();
        // A missing script or stalled module must not leave an indefinite blank
        // task. Keep its defaults hidden and offer a reload without losing data.
        deadline = window.setTimeout(fail, 15000);
    }

    function complete(): void {
        hold();
        delete documentRoot.dataset.initialView;
        document.querySelectorAll<HTMLElement>('.initial-view-main').forEach(view => {
            view.setAttribute('aria-busy', 'false');
            view.querySelector('.initial-view-recovery')?.remove();
        });
        window.LwcFlowActionDock?.refreshNow?.();
    }

    function fail(): void {
        hold();
        if (documentRoot.dataset.initialView !== 'pending' && documentRoot.dataset.initialView !== 'failed') return;
        documentRoot.dataset.initialView = 'failed';
        document.querySelectorAll<HTMLElement>('.initial-view-main').forEach(view => {
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

    window.LwcInitialView = { prepare, complete, hold, fail, run };
})();
