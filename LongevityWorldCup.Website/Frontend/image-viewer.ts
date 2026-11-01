// Image viewer: one way to look closely at any picture on the site.
//
// A viewable picture lifts out of the page and flies to fill the screen, then can be
// pinched, wheeled or double-tapped to zoom, dragged to pan, swiped to its neighbours
// and flicked down (or Back, or Escape) to fly home again. Pages opt in with
// `data-lwc-view` on an <img>; neighbours are the viewable pictures sharing the
// nearest `[data-lwc-view-group]` (documents group automatically).
//
// Presentation only: the page's own <img> stays where it is and keeps its alt text.
interface LwcImageViewerOptions {
    images?: HTMLImageElement[];
    kind?: 'image' | 'proof' | 'photo';
    beforeClose?: (image: HTMLImageElement) => void;
    onLoad?: (image: HTMLImageElement) => void;
    pushHistory?: boolean;
    historyState?: (image: HTMLImageElement, index: number) => Record<string, unknown>;
    ownsHistory?: (state: any) => boolean;
}
interface Window {
    LwcImageViewer?: {
        open: (image: HTMLImageElement, options?: LwcImageViewerOptions) => Promise<void>;
        close: (fromHistory?: boolean) => void;
        isOpen: () => boolean;
        adopt: (scope: ParentNode) => void;
    };
}

(() => {
    if (window.LwcImageViewer) return;

    // Pictures worth a closer look on existing public markup.
    const RECIPE = [
        '.documentation-document p > img',
        'img.qr-code-img',
    ].join(',');
    const GROUPS = '[data-lwc-view-group], .documentation-document';
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)');
    const FLIGHT_MS = 340;
    const SETTLE_EASE = 'cubic-bezier(0.16, 1, 0.3, 1)';
    const ICONS = {
        close: '<path d="M6 6l12 12M18 6L6 18"/>',
        prev: '<path d="M15 5l-7 7 7 7"/>',
        next: '<path d="M9 5l7 7-7 7"/>',
    };

    type Box = { left: number; top: number; width: number; height: number };
    type Pose = { x: number; y: number; z: number };

    let dialog: HTMLDialogElement | null = null;
    let stage: HTMLElement;
    let scrim: HTMLElement;
    let picture: HTMLImageElement;
    let dots: HTMLElement;
    let prevButton: HTMLButtonElement;
    let nextButton: HTMLButtonElement;

    let set: HTMLImageElement[] = [];
    let index = 0;
    let source: HTMLImageElement | null = null;
    let fit: Box = { left: 0, top: 0, width: 0, height: 0 };
    let pose: Pose = { x: 0, y: 0, z: 1 };
    let maxZoom = 3;
    let busy = false;
    let destination = 0;
    let pendingSelection: number | null = null;
    let pendingZoom: 'in' | 'out' | 'fit' | null = null;
    let historyEntry = false;
    let closePending = false;
    let historyClosePending = false;
    let reopenAfterClose = false;
    let pendingOpen: { image: HTMLImageElement; settings: LwcImageViewerOptions } | null = null;
    let loadGeneration = 0;
    let stopLoad = (): void => {};
    let options: LwcImageViewerOptions = {};
    let loadedImage: HTMLImageElement | null = null;
    let sessionId = 0;
    const sessions = new Map<number, { images: HTMLImageElement[]; options: LwcImageViewerOptions }>();
    let status: HTMLElement;
    let retry: HTMLButtonElement;
    let position: HTMLElement;
    let toolbar: HTMLElement;
    let resetGestures = (): void => {};
    let savedOverflow = '';
    let nextSessionId = 0;
    let proofZoom = 1;
    const proofZoomLevels = [1, 1.5, 2, 3];

    // ---- adoption -----------------------------------------------------------------------
    function viewable(image: HTMLImageElement): boolean {
        return !image.closest('.lwc-viewer, a') && !image.hasAttribute('data-lwc-view-off')
            && (image.hasAttribute('data-lwc-view') || !image.closest('button, dialog, [role="dialog"]'));
    }

    function adopt(scope: ParentNode): void {
        const mark = (image: HTMLImageElement): void => {
            if (image.hasAttribute('data-lwc-viewable') || !viewable(image)) return;
            image.setAttribute('data-lwc-viewable', '');
            if (image.closest('button')) return;
            image.tabIndex = 0;
            image.setAttribute('role', 'button');
            image.setAttribute('aria-haspopup', 'dialog');
            if (!image.alt) image.setAttribute('aria-label', 'Enlarge image');
        };
        const selector = RECIPE + ', img[data-lwc-view]';
        if (scope instanceof HTMLImageElement && scope.matches(selector)) mark(scope);
        scope.querySelectorAll<HTMLImageElement>(selector).forEach(mark);
    }

    function neighbours(image: HTMLImageElement): HTMLImageElement[] {
        const group = image.closest(GROUPS);
        if (!group) return [image];
        const images = Array.from(group.querySelectorAll<HTMLImageElement>('img[data-lwc-viewable]'))
            .filter(candidate => candidate.checkVisibility() && !candidate.closest('[hidden], details:not([open])'));
        return images.includes(image) ? images : [image];
    }

    // ---- geometry -----------------------------------------------------------------------
    function natural(image: HTMLImageElement): { width: number; height: number } {
        const width = image.naturalWidth || image.width || 1;
        const height = image.naturalHeight || image.height || 1;
        return { width, height };
    }

    function measureFit(image: HTMLImageElement): void {
        const toolbarRect = toolbar.getBoundingClientRect();
        const closeRect = dialog!.querySelector('.lwc-viewer-close')!.getBoundingClientRect();
        stage.style.top = `${Math.max(72, closeRect.bottom + 12)}px`;
        stage.style.bottom = `${Math.max(64, window.innerHeight - toolbarRect.top + 10)}px`;
        status.style.top = stage.style.top;
        status.style.bottom = stage.style.bottom;
        const room = stage.getBoundingClientRect();
        const size = natural(image);
        // Fill the room, but never blow a small picture up past twice its own pixels.
        const scale = Math.max(0.001, Math.min(room.width / size.width, room.height / size.height, 2));
        const width = size.width * scale;
        const height = size.height * scale;
        fit = { left: room.left + (room.width - width) / 2, top: room.top + (room.height - height) / 2, width, height };
        // Zoom far enough to read the source's own pixels twice over, within reason.
        maxZoom = Math.min(8, Math.max(2.5, (size.width / width) * 2));
        if (options.kind === 'proof') maxZoom = Math.max(maxZoom, room.width / width * 3);
        picture.style.left = (fit.left - room.left) + 'px';
        picture.style.top = (fit.top - room.top) + 'px';
        picture.style.width = width + 'px';
        picture.style.height = height + 'px';
    }

    function poseOf(rect: DOMRect | Box): Pose {
        // The pose that lays the fitted picture exactly over `rect`.
        return {
            x: rect.left + rect.width / 2 - (fit.left + fit.width / 2),
            y: rect.top + rect.height / 2 - (fit.top + fit.height / 2),
            z: rect.width / fit.width,
        };
    }

    const transformOf = (p: Pose): string => `translate(${p.x}px, ${p.y}px) scale(${p.z})`;

    function clampPose(p: Pose): Pose {
        const z = Math.min(maxZoom, Math.max(1, p.z));
        const spanX = Math.max(0, (fit.width * z - window.innerWidth) / 2 + 24);
        const spanY = Math.max(0, (fit.height * z - window.innerHeight) / 2 + 24);
        const roomX = z > 1 ? Math.max(spanX, (fit.width * (z - 1)) / 2) : 0;
        const roomY = z > 1 ? Math.max(spanY, (fit.height * (z - 1)) / 2) : 0;
        return { x: Math.min(roomX, Math.max(-roomX, p.x)), y: Math.min(roomY, Math.max(-roomY, p.y)), z };
    }

    function apply(p: Pose): void {
        pose = p;
        picture.style.transform = transformOf(p);
        dialog?.toggleAttribute('data-zoomed', p.z > 1.01);
        if (dialog) {
            dialog.querySelector<HTMLButtonElement>('[data-zoom="out"]')!.disabled = !loadedImage || (options.kind === 'proof' ? proofZoom <= 1 : p.z <= 1);
            dialog.querySelector<HTMLButtonElement>('[data-zoom="in"]')!.disabled = !loadedImage || (options.kind === 'proof' ? proofZoom >= 3 : p.z >= maxZoom);
            dialog.querySelector<HTMLButtonElement>('[data-zoom="fit"]')!.disabled = !loadedImage;
            dialog.dataset.proofZoom = String(proofZoom);
            dialog.querySelector('.lwc-viewer-zoom-status')!.textContent = `${Math.round((options.kind === 'proof' ? proofZoom : p.z) * 100)}%`;
        }
    }

    function proofPose(): Pose {
        const room = stage.getBoundingClientRect();
        const z = proofZoom === 1 ? 1 : Math.max(1, room.width / fit.width * proofZoom);
        return clampPose({ x: 0, y: Math.max(0, (fit.height * z - room.height) / 2), z });
    }

    function rememberGestureZoom(target: Pose): Pose {
        if (options.kind === 'proof') proofZoom = target.z <= 1.01 ? 1 : Math.max(1.01, fit.width * target.z / stage.clientWidth);
        return target;
    }

    function changeZoom(direction: 'in' | 'out' | 'fit'): void {
        if (busy) {
            if (dialog?.dataset.state !== 'closing') pendingZoom = direction;
            return;
        }
        if (!loadedImage) return;
        let target: Pose;
        if (options.kind === 'proof') {
            proofZoom = direction === 'fit' ? 1 : direction === 'in'
                ? proofZoomLevels.find(level => level > proofZoom + .01) ?? 3
                : [...proofZoomLevels].reverse().find(level => level < proofZoom - .01) ?? 1;
            target = proofPose();
        } else {
            const z = direction === 'fit' ? 1 : pose.z * (direction === 'in' ? 1.5 : 1 / 1.5);
            target = zoomAt(fit.left + fit.width / 2, fit.top + fit.height / 2, z);
        }
        void animatePicture(pose, target, 220);
    }

    function animatePicture(from: Pose, to: Pose, duration: number, easing = SETTLE_EASE): Promise<void> {
        apply(to);
        if (reduce.matches || duration <= 0) return Promise.resolve();
        return picture.animate([{ transform: transformOf(from) }, { transform: transformOf(to) }], { duration, easing })
            .finished.then(() => undefined, () => undefined);
    }

    function dim(level: number, duration = 0): void {
        const from = getComputedStyle(scrim).opacity;
        scrim.style.opacity = String(level);
        if (duration > 0 && !reduce.matches) {
            scrim.animate([{ opacity: from }, { opacity: String(level) }], { duration, easing: 'ease-out' });
        }
    }

    // Zoom about a screen point, keeping the pixel under it in place.
    function zoomAt(clientX: number, clientY: number, z: number, from: Pose = pose): Pose {
        const target = Math.min(maxZoom, Math.max(1, z));
        const cx = fit.left + fit.width / 2 + from.x;
        const cy = fit.top + fit.height / 2 + from.y;
        const ratio = target / from.z;
        return clampPose({
            x: from.x + (clientX - cx) * (1 - ratio),
            y: from.y + (clientY - cy) * (1 - ratio),
            z: target,
        });
    }

    function onScreen(element: Element): DOMRect | null {
        const rect = element.getBoundingClientRect();
        const visible = rect.width > 0 && rect.bottom > 0 && rect.top < window.innerHeight
            && rect.right > 0 && rect.left < window.innerWidth;
        return visible ? rect : null;
    }

    // ---- viewer shell -------------------------------------------------------------------
    function icon(path: string): string {
        return `<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">${path}</svg>`;
    }

    function build(): HTMLDialogElement {
        const shell = document.createElement('dialog');
        shell.className = 'lwc-viewer';
        shell.setAttribute('role', 'dialog');
        shell.setAttribute('aria-modal', 'true');
        shell.setAttribute('aria-label', 'Image viewer');
        shell.innerHTML = `
            <div class="lwc-viewer-scrim"></div>
            <div class="lwc-viewer-stage" tabindex="0" aria-label="Enlarged image"><img class="lwc-viewer-picture" alt="" draggable="false"></div>
            <button type="button" class="lwc-viewer-button lwc-viewer-close" aria-label="Close">${icon(ICONS.close)}</button>
            <div class="lwc-viewer-feedback"><div role="status"><strong class="lwc-viewer-load-title"></strong><p class="lwc-viewer-load-message" hidden></p></div><button type="button" class="lwc-viewer-retry" hidden>Retry</button></div>
            <div class="lwc-viewer-toolbar">
            <button type="button" class="lwc-viewer-button lwc-viewer-nav lwc-viewer-prev" data-step="-1" aria-label="Previous image">${icon(ICONS.prev)}</button>
            <div class="lwc-viewer-zoom-controls" role="group" aria-label="Image zoom">
            <button type="button" class="lwc-viewer-button lwc-viewer-zoom-out" data-zoom="out" aria-label="Zoom out">${icon('<path d="M5 12h14"/>')}</button>
            <button type="button" class="lwc-viewer-button lwc-viewer-zoom-fit" data-zoom="fit" aria-label="Fit image to viewer">${icon('<path d="M9 4H4v5m11-5h5v5M4 15v5h5m6 0h5v-5"/>')}</button>
            <button type="button" class="lwc-viewer-button lwc-viewer-zoom-in" data-zoom="in" aria-label="Zoom in">${icon('<path d="M5 12h14m-7-7v14"/>')}</button>
            </div>
            <button type="button" class="lwc-viewer-button lwc-viewer-nav lwc-viewer-next" data-step="1" aria-label="Next image">${icon(ICONS.next)}</button>
            </div><div class="lwc-viewer-dots" aria-hidden="true"></div>
            <span class="lwc-viewer-position" aria-live="polite" aria-atomic="true"></span>
            <span class="lwc-viewer-zoom-status" aria-live="polite" aria-atomic="true"></span>`;
        document.body.append(shell);
        stage = shell.querySelector<HTMLElement>('.lwc-viewer-stage')!;
        scrim = shell.querySelector<HTMLElement>('.lwc-viewer-scrim')!;
        picture = shell.querySelector<HTMLImageElement>('.lwc-viewer-picture')!;
        dots = shell.querySelector<HTMLElement>('.lwc-viewer-dots')!;
        status = shell.querySelector<HTMLElement>('.lwc-viewer-feedback')!;
        retry = shell.querySelector<HTMLButtonElement>('.lwc-viewer-retry')!;
        position = shell.querySelector<HTMLElement>('.lwc-viewer-position')!;
        toolbar = shell.querySelector<HTMLElement>('.lwc-viewer-toolbar')!;
        [prevButton, nextButton] = Array.from(shell.querySelectorAll<HTMLButtonElement>('.lwc-viewer-nav')) as [HTMLButtonElement, HTMLButtonElement];

        shell.querySelector('.lwc-viewer-close')!.addEventListener('click', () => requestClose());
        prevButton.addEventListener('click', () => step(-1));
        nextButton.addEventListener('click', () => step(1));
        shell.addEventListener('cancel', event => { event.preventDefault(); requestClose(); });
        shell.addEventListener('click', event => { if (event.target === shell || event.target === scrim) requestClose(); });
        shell.addEventListener('keydown', onKey);
        retry.addEventListener('click', () => { if (set[index]) show(set[index]!); });
        shell.querySelectorAll<HTMLButtonElement>('[data-zoom]').forEach(button => button.addEventListener('click', () => {
            changeZoom(button.dataset.zoom as 'in' | 'out' | 'fit');
        }));
        new ResizeObserver(() => {
            if (!shell.open || busy || !loadedImage) return;
            measureFit(loadedImage);
            apply(clampPose(pose));
        }).observe(toolbar);
        wireGestures();
        return shell;
    }

    function syncChrome(): void {
        const many = set.length > 1;
        dialog!.toggleAttribute('data-many', many);
        prevButton.disabled = index === 0;
        nextButton.disabled = index === set.length - 1;
        const start = Math.max(0, Math.min(index - 4, set.length - 9));
        dots.replaceChildren(...set.slice(start, start + 9).map((_, i) => {
            const dot = document.createElement('span'); dot.toggleAttribute('data-current', start + i === index); return dot;
        }));
        picture.alt = set[index]?.alt ?? '';
        const subject = options.kind === 'proof' ? 'Proof' : options.kind === 'photo' ? 'Photo' : 'Image';
        position.textContent = `${subject} ${index + 1} of ${set.length}`;
        dialog!.setAttribute('aria-label', `${subject} ${index + 1} of ${set.length}`);
        retry.textContent = options.kind === 'proof' ? 'Try again' : 'Retry';
        dialog!.querySelector('[data-zoom="in"]')!.setAttribute('aria-label', options.kind === 'proof' ? 'Zoom proof in' : 'Zoom in');
        dialog!.querySelector('[data-zoom="out"]')!.setAttribute('aria-label', options.kind === 'proof' ? 'Zoom proof out' : 'Zoom out');
        dialog!.querySelector('[data-zoom="fit"]')!.setAttribute('aria-label', options.kind === 'proof' ? 'Fit proof to viewer' : 'Fit image to viewer');
        if (historyEntry && ownsEntry(history.state)) {
            history.replaceState({ ...history.state, ...selectionState() }, '');
        }
    }

    function ownsEntry(state: any): boolean {
        return options.ownsHistory ? options.ownsHistory(state) : state?.lwcViewer?.id === sessionId;
    }

    function selectionState(): Record<string, unknown> {
        return options.historyState?.(set[index]!, index) ?? { lwcViewer: { id: sessionId, index } };
    }

    function show(image: HTMLImageElement): void {
        stopLoad();
        const generation = ++loadGeneration;
        loadedImage = null;
        const fullSrc = image.dataset.lwcViewSrc || image.dataset.fullSrc || image.closest<HTMLButtonElement>('[data-photo-src]')?.dataset.photoSrc || image.currentSrc || image.src;
        const preview = options.kind !== 'proof' && image.complete && image.naturalWidth > 0
            && !image.matches('.portrait-fallback, [data-lwc-portrait-fallback]');
        picture.removeAttribute('srcset');
        picture.hidden = !preview;
        if (preview) { picture.src = image.currentSrc || image.src; loadedImage = image; }
        else picture.removeAttribute('src');
        status.hidden = !!preview;
        status.querySelector('strong')!.textContent = options.kind === 'photo' ? 'Loading photo…' : `Loading ${options.kind === 'proof' ? 'proof' : 'image'}…`;
        status.querySelector('p')!.hidden = true;
        if (document.activeElement === retry) stage.focus({ preventScroll: true });
        retry.hidden = true;
        dialog!.dataset.imageState = preview ? 'ready' : 'loading';
        stage.setAttribute('aria-busy', preview ? 'false' : 'true');
        measureFit(image);
        apply(pose);
        const pending = new Image();
        let timer = 0;
        let settled = false;
        const finish = (ready: boolean): void => {
            if (settled || generation !== loadGeneration || !dialog?.open) return;
            settled = true;
            clearTimeout(timer);
            pending.onload = null; pending.onerror = null;
            if (ready) {
                picture.src = fullSrc;
                picture.hidden = false;
                loadedImage = pending;
                // Opening uses the thumbnail's aspect ratio. Refit only after the flight.
                if (!busy) { measureFit(pending); apply(options.kind === 'proof' ? proofPose() : clampPose(pose)); }
                options.onLoad?.(image);
            }
            if (ready || preview) {
                status.hidden = true;
                dialog.dataset.imageState = 'ready';
            } else {
                dialog.dataset.imageState = 'error';
                status.hidden = false;
                status.querySelector('strong')!.textContent = options.kind === 'photo' ? 'Photo couldn’t load.' : `This ${options.kind === 'proof' ? 'proof' : 'image'} couldn't load`;
                const message = status.querySelector('p')!;
                message.hidden = options.kind === 'photo';
                message.textContent = options.kind === 'proof' && set.length > 1 ? 'Try again, or move to another proof.' : `Try loading this ${options.kind === 'proof' ? 'proof' : 'image'} again.`;
                retry.hidden = false;
            }
            stage.setAttribute('aria-busy', 'false');
            apply(pose);
        };
        stopLoad = () => { ++loadGeneration; clearTimeout(timer); pending.onload = null; pending.onerror = null; pending.removeAttribute('src'); };
        pending.onload = () => { void pending.decode().then(() => finish(true), () => finish(false)); };
        pending.onerror = () => finish(false);
        timer = window.setTimeout(() => { finish(false); pending.removeAttribute('src'); }, 15000);
        pending.src = fullSrc;
    }

    // ---- open / close -------------------------------------------------------------------
    async function open(image: HTMLImageElement, settings: LwcImageViewerOptions = {}, restoring = false): Promise<void> {
        if (busy || dialog?.open) {
            if (closePending || dialog?.dataset.state === 'closing') pendingOpen = { image, settings };
            return;
        }
        dialog ??= build();
        busy = true;
        closePending = false;
        historyClosePending = false;
        pendingOpen = null;
        resetGestures();
        options = settings;
        proofZoom = settings.kind === 'proof' && window.innerWidth <= 768 ? 2 : 1;
        set = settings.images?.includes(image) ? settings.images : neighbours(image);
        index = Math.max(0, set.indexOf(image));
        destination = index;
        pendingSelection = null;
        pendingZoom = null;
        source = image;
        const origin = image.getBoundingClientRect();
        savedOverflow = document.documentElement.style.overflow;
        document.documentElement.style.overflow = 'hidden';
        dialog.showModal();
        dialog.setAttribute('aria-hidden', 'false');
        show(image);
        syncChrome();
        dialog.setAttribute('data-state', 'opening');
        image.setAttribute('data-lwc-view-away', '');
        dim(0);
        dim(1, FLIGHT_MS * 0.8);
        if (!restoring && settings.pushHistory !== false) {
            sessionId = ++nextSessionId;
            sessions.set(sessionId, { images: set, options });
            history.pushState({ ...(history.state ?? {}), ...selectionState() }, '');
        }
        historyEntry = ownsEntry(history.state);
        dialog.querySelector<HTMLButtonElement>('.lwc-viewer-close')!.focus({ preventScroll: true });
        await animatePicture(poseOf(origin), { x: 0, y: 0, z: 1 }, FLIGHT_MS);
        dialog.setAttribute('data-state', 'open');
        completeTransition();
    }

    function completeTransition(): void {
        busy = false;
        dialog!.dataset.state = 'open';
        if (loadedImage) { measureFit(loadedImage); apply(options.kind === 'proof' ? proofPose() : clampPose(pose)); }
        if (closePending) { closePending = false; void close(); return; }
        const selection = pendingSelection;
        pendingSelection = null;
        if (selection !== null && selection !== index) { void step(selection - index); return; }
        const zoom = pendingZoom;
        pendingZoom = null;
        if (zoom) changeZoom(zoom);
    }

    // Close through history when we own an entry, so Back and the close button agree.
    function requestClose(): void {
        if (!dialog?.open || dialog.dataset.state === 'closing' || historyClosePending) return;
        if (historyEntry && ownsEntry(history.state)) {
            historyClosePending = true;
            history.back();
            return;
        }
        void close();
    }

    async function close(fromPose: Pose = pose): Promise<void> {
        if (!dialog?.open || dialog.dataset.state === 'closing') return;
        if (busy) { closePending = true; return; }
        busy = true;
        stopLoad();
        resetGestures();
        historyEntry = false;
        reopenAfterClose = false;
        dialog.setAttribute('data-state', 'closing');
        const home = set[index] ?? source;
        if (home) options.beforeClose?.(home);
        let landing = home ? onScreen(home) : null;
        if (home && !landing && home.isConnected) {
            // Bring the picture you ended on back into view underneath, then land on it.
            home.scrollIntoView({ block: 'center', behavior: 'instant' });
            landing = onScreen(home);
        }
        if (home !== source) {
            source?.removeAttribute('data-lwc-view-away');
            home?.setAttribute('data-lwc-view-away', '');
        }
        dim(0, FLIGHT_MS * 0.85);
        if (landing) {
            await animatePicture(fromPose, poseOf(landing), FLIGHT_MS * 0.9);
        } else if (!reduce.matches) {
            await picture.animate([{ opacity: 1 }, { opacity: 0, transform: transformOf({ ...fromPose, z: fromPose.z * 0.92 }) }],
                { duration: 200, easing: 'ease-in', fill: 'forwards' }).finished.catch(() => undefined);
        }
        home?.removeAttribute('data-lwc-view-away');
        source?.removeAttribute('data-lwc-view-away');
        picture.getAnimations().forEach(animation => animation.cancel());
        dialog.close();
        dialog.setAttribute('aria-hidden', 'true');
        document.documentElement.style.overflow = savedOverflow;
        dialog.removeAttribute('data-state');
        dialog.removeAttribute('data-zoomed');
        (home?.closest<HTMLButtonElement>('button') ?? home)?.focus({ preventScroll: true });
        busy = false;
        historyClosePending = false;
        const queued = pendingOpen;
        pendingOpen = null;
        if (queued?.image.isConnected) {
            void open(queued.image, queued.settings);
            return;
        }
        // A quick Forward while the return flight was still playing must not
        // leave a viewer history entry pointing at a closed dialog.
        if (reopenAfterClose && ownsEntry(history.state) && home?.isConnected) {
            const restored = options.ownsHistory ? home : set[history.state.lwcViewer.index] ?? home;
            void open(restored, { ...options, pushHistory: false }, true);
        }
    }

    // ---- neighbours -----------------------------------------------------------------------
    async function step(direction: number, carried = 0): Promise<void> {
        if (!direction) return;
        if (busy) {
            if (dialog?.dataset.state !== 'closing') pendingSelection = Math.max(0, Math.min(set.length - 1, (pendingSelection ?? destination) + direction));
            return;
        }
        const next = index + direction;
        if (next < 0 || next >= set.length) {
            if (carried) await animatePicture(pose, { x: 0, y: 0, z: 1 }, 260);
            return;
        }
        busy = true;
        destination = next;
        resetGestures();
        dialog!.dataset.state = 'stepping';
        const travel = Math.min(window.innerWidth * 0.32, 260);
        const out = { x: -direction * travel, y: 0, z: 1 };
        if (!reduce.matches) {
            await picture.animate(
                [{ transform: transformOf({ x: carried, y: 0, z: pose.z }), opacity: 1 },
                    { transform: transformOf(out), opacity: 0 }],
                { duration: 150, easing: 'cubic-bezier(0.4, 0, 1, 1)', fill: 'forwards' }).finished.catch(() => undefined);
        }
        index = next;
        const image = set[index]!;
        show(image);
        syncChrome();
        picture.getAnimations().forEach(animation => animation.cancel());
        apply({ x: 0, y: 0, z: 1 });
        if (!reduce.matches) {
            await picture.animate(
                [{ transform: transformOf({ x: direction * travel, y: 0, z: 1 }), opacity: 0 },
                    { transform: transformOf({ x: 0, y: 0, z: 1 }), opacity: 1 }],
                { duration: 280, easing: SETTLE_EASE }).finished.catch(() => undefined);
        }
        completeTransition();
    }

    // ---- keyboard -----------------------------------------------------------------------
    function onKey(event: KeyboardEvent): void {
        event.stopPropagation();
        if (event.key === 'Escape') { event.preventDefault(); requestClose(); return; }
        if (event.key === 'Tab') {
            const controls = Array.from(dialog!.querySelectorAll<HTMLElement>('button:not(:disabled), [tabindex="0"]'))
                .filter(control => control.checkVisibility() && !control.closest('[hidden]'));
            const first = controls[0]; const last = controls.at(-1);
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
            return;
        }
        if (!busy && loadedImage && pose.z > 1.01 && event.target === stage && event.key.startsWith('Arrow')) {
            event.preventDefault();
            const dx = event.key === 'ArrowLeft' ? 64 : event.key === 'ArrowRight' ? -64 : 0;
            const dy = event.key === 'ArrowUp' ? 64 : event.key === 'ArrowDown' ? -64 : 0;
            apply(clampPose({ ...pose, x: pose.x + dx, y: pose.y + dy }));
            return;
        }
        if (['PageUp', 'PageDown', 'ArrowUp', 'ArrowDown'].includes(event.key)
            || (event.key === ' ' && !(event.target instanceof HTMLButtonElement))) { event.preventDefault(); return; }
        switch (event.key) {
            case 'ArrowLeft': event.preventDefault(); void step(-1); break;
            case 'ArrowRight': event.preventDefault(); void step(1); break;
            case 'Home': event.preventDefault(); void step(-(pendingSelection ?? destination)); break;
            case 'End': event.preventDefault(); void step(set.length - 1 - (pendingSelection ?? destination)); break;
            case '+': case '=': event.preventDefault(); changeZoom('in'); break;
            case '-': event.preventDefault(); changeZoom('out'); break;
            case '0': event.preventDefault(); changeZoom('fit'); break;
            default: return;
        }
    }

    // ---- gestures -----------------------------------------------------------------------
    function wireGestures(): void {
        const pointers = new Map<number, { x: number; y: number }>();
        let mode: 'none' | 'pending' | 'pan' | 'dismiss' | 'swipe' | 'pinch' = 'none';
        let start = { x: 0, y: 0 };
        let startPose: Pose = pose;
        let pinchStart = { distance: 1, z: 1, mid: { x: 0, y: 0 } };
        let lastMove = { y: 0, x: 0, t: 0, vy: 0, vx: 0 };
        let lastTap = { t: 0, x: 0, y: 0 };
        let moved = false;
        let startedOnPicture = false;
        let tapTimer = 0;
        resetGestures = () => {
            pointers.forEach((_, id) => { if (stage.hasPointerCapture(id)) stage.releasePointerCapture(id); });
            pointers.clear(); mode = 'none'; lastTap.t = 0;
            clearTimeout(tapTimer);
            dialog?.removeAttribute('data-dragging');
        };

        const midpoint = (): { x: number; y: number; distance: number } => {
            const [a, b] = Array.from(pointers.values()) as [{ x: number; y: number }, { x: number; y: number }];
            return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2, distance: Math.hypot(a.x - b.x, a.y - b.y) || 1 };
        };

        stage.addEventListener('pointerdown', event => {
            if (busy || event.button > 0) return;
            stage.setPointerCapture(event.pointerId);
            pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            picture.getAnimations().forEach(animation => animation.cancel());
            moved = false;
            if (pointers.size === 2) {
                moved = true;
                const m = midpoint();
                mode = 'pinch';
                pinchStart = { distance: m.distance, z: pose.z, mid: { x: m.x, y: m.y } };
                startPose = pose;
                return;
            }
            mode = pose.z > 1.01 ? 'pan' : 'pending';
            startedOnPicture = event.target === picture;
            start = { x: event.clientX, y: event.clientY };
            startPose = pose;
            lastMove = { x: event.clientX, y: event.clientY, t: event.timeStamp, vx: 0, vy: 0 };
        });

        stage.addEventListener('pointermove', event => {
            if (!pointers.has(event.pointerId)) return;
            pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            const dx = event.clientX - start.x;
            const dy = event.clientY - start.y;
            const dt = Math.max(1, event.timeStamp - lastMove.t);
            lastMove = {
                x: event.clientX, y: event.clientY, t: event.timeStamp,
                vx: (event.clientX - lastMove.x) / dt, vy: (event.clientY - lastMove.y) / dt,
            };
            if (Math.hypot(dx, dy) > 6) moved = true;

            if (mode === 'pinch' && pointers.size >= 2) {
                const m = midpoint();
                const z = pinchStart.z * (m.distance / pinchStart.distance);
                const zoomed = zoomAt(pinchStart.mid.x, pinchStart.mid.y, z, startPose);
                apply(rememberGestureZoom(clampPose({ ...zoomed, x: zoomed.x + m.x - pinchStart.mid.x, y: zoomed.y + m.y - pinchStart.mid.y })));
                return;
            }
            if (mode === 'pending' && Math.hypot(dx, dy) > 8) {
                mode = Math.abs(dy) > Math.abs(dx) || set.length < 2 ? 'dismiss' : 'swipe';
                dialog!.setAttribute('data-dragging', '');
            }
            if (mode === 'pan') {
                apply(clampPose({ x: startPose.x + dx, y: startPose.y + dy, z: pose.z }));
            } else if (mode === 'dismiss') {
                // The picture follows the finger and shrinks a little; the page shows through.
                const pull = Math.min(1, Math.abs(dy) / (window.innerHeight * 0.55));
                apply({ x: dx * 0.6, y: dy, z: 1 - pull * 0.28 });
                scrim.style.opacity = String(1 - pull * 0.9);
            } else if (mode === 'swipe') {
                // Past the first or last picture the swipe resists.
                const edge = (index === 0 && dx > 0) || (index === set.length - 1 && dx < 0);
                apply({ x: edge ? dx * 0.3 : dx, y: 0, z: 1 });
            }
        });

        const finish = (event: PointerEvent): void => {
            if (!pointers.has(event.pointerId)) return;
            pointers.delete(event.pointerId);
            if (stage.hasPointerCapture(event.pointerId)) stage.releasePointerCapture(event.pointerId);
            if (event.type === 'pointercancel') {
                resetGestures(); dim(1); void animatePicture(pose, clampPose(pose), 220); return;
            }
            const dx = event.clientX - start.x;
            const dy = event.clientY - start.y;
            dialog!.removeAttribute('data-dragging');

            if (mode === 'pinch') {
                if (pointers.size === 1) {
                    // Carry on as a pan with the remaining finger.
                    const [rest] = Array.from(pointers.values()) as [{ x: number; y: number }];
                    start = rest; startPose = pose; mode = 'pan';
                    lastMove = { ...rest, t: event.timeStamp, vx: 0, vy: 0 };
                    return;
                }
                mode = 'none';
                if (pose.z < 1.05) void animatePicture(pose, { x: 0, y: 0, z: 1 }, 260);
                return;
            }
            if (pointers.size) return;

            const current = mode;
            mode = 'none';
            if (current === 'dismiss') {
                if (Math.abs(dy) > window.innerHeight * 0.16 || Math.abs(lastMove.vy) > 0.6) {
                    if (historyEntry && ownsEntry(history.state)) {
                        pendingDismissPose = pose;
                        requestClose();
                    } else void close(pose);
                } else {
                    dim(1, 220);
                    void animatePicture(pose, { x: 0, y: 0, z: 1 }, 300);
                }
                return;
            }
            if (current === 'swipe') {
                const direction = dx < 0 ? 1 : -1;
                if (Math.abs(dx) > window.innerWidth * 0.18 || Math.abs(lastMove.vx) > 0.5) void step(direction, dx);
                else void animatePicture(pose, { x: 0, y: 0, z: 1 }, 260);
                return;
            }
            if (current === 'pan') {
                // A short glide in the direction of the throw, then settle inside the edges.
                const glide = clampPose({ x: pose.x + lastMove.vx * 120, y: pose.y + lastMove.vy * 120, z: pose.z });
                void animatePicture(pose, glide, 320);
                if (moved) return;
            }
            if (moved) return;

            // A tap: a double tap zooms about the point, a tap on the dim closes.
            const now = event.timeStamp;
            const doubled = now - lastTap.t < 300 && Math.hypot(event.clientX - lastTap.x, event.clientY - lastTap.y) < 30;
            lastTap = { t: doubled ? 0 : now, x: event.clientX, y: event.clientY };
            if (doubled) {
                const target = pose.z > 1.01 ? { x: 0, y: 0, z: 1 } : zoomAt(event.clientX, event.clientY, Math.min(maxZoom, 2.5));
                void animatePicture(pose, rememberGestureZoom(target), 300);
                return;
            }
            if (!startedOnPicture && pose.z <= 1.01) {
                tapTimer = window.setTimeout(() => { if (lastTap.t === now) requestClose(); }, 310);
            }
        };
        stage.addEventListener('pointerup', finish);
        stage.addEventListener('pointercancel', finish);

        stage.addEventListener('wheel', event => {
            event.preventDefault();
            if (busy || !loadedImage) return;
            picture.getAnimations().forEach(animation => animation.cancel());
            const factor = Math.exp(-event.deltaY * (event.ctrlKey ? 0.01 : 0.0022));
            apply(rememberGestureZoom(zoomAt(event.clientX, event.clientY, pose.z * factor)));
        }, { passive: false });

        stage.addEventListener('dblclick', event => event.preventDefault());
    }

    let pendingDismissPose: Pose | null = null;

    window.addEventListener('popstate', event => {
        const state = event.state?.lwcViewer;
        if (dialog?.open && ownsEntry(event.state)) {
            if (dialog.dataset.state === 'closing') reopenAfterClose = true;
            else { closePending = false; historyEntry = true; historyClosePending = false; }
            return;
        }
        if (dialog?.open) {
            historyEntry = false;
            const from = pendingDismissPose ?? pose;
            pendingDismissPose = null;
            void close(from);
        } else if (state) {
            const session = sessions.get(state.id);
            const image = session?.images[state.index];
            if (session && image?.isConnected) {
                sessionId = state.id;
                void open(image, session.options, true);
            }
        }
    });

    window.addEventListener('resize', () => {
        if (!dialog?.open || busy) return;
        const image = loadedImage ?? set[index];
        if (!image) return;
        measureFit(image);
        apply(clampPose(pose));
    });

    // ---- entry points -------------------------------------------------------------------
    document.addEventListener('click', event => {
        const image = event.target instanceof Element ? event.target.closest<HTMLImageElement>('img[data-lwc-viewable]') : null;
        if (!image || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey) return;
        event.preventDefault();
        void open(image);
    });
    document.addEventListener('keydown', event => {
        const image = event.target instanceof HTMLImageElement && event.target.matches('img[data-lwc-viewable]') ? event.target : null;
        if (!image || (event.key !== 'Enter' && event.key !== ' ')) return;
        event.preventDefault();
        void open(image);
    });

    function start(): void {
        adopt(document);
        new MutationObserver(records => records.forEach(record => record.addedNodes.forEach(node => {
            if (node instanceof Element) adopt(node);
        }))).observe(document.body, { childList: true, subtree: true });
    }

    window.LwcImageViewer = {
        open: (image, settings) => open(image, settings),
        close: fromHistory => { if (fromHistory) { historyEntry = false; void close(); } else requestClose(); },
        isOpen: () => !!dialog?.open,
        adopt,
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();
