// Image viewer: one way to look closely at any picture on the site.
//
// A viewable picture lifts out of the page and flies to fill the screen, then can be
// pinched, wheeled or double-tapped to zoom, dragged to pan, swiped to its neighbours
// and flicked down (or Back, or Escape) to fly home again. Pages opt in with
// `data-lwc-view` on an <img>; neighbours are the viewable pictures sharing the
// nearest `[data-lwc-view-group]` (documents group automatically).
//
// Presentation only: the page's own <img> stays where it is and keeps its alt text.
interface Window {
    LwcImageViewer?: { open: (image: HTMLImageElement) => Promise<void>; adopt: (scope: ParentNode) => void };
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
    let historyEntry = false;

    // ---- adoption -----------------------------------------------------------------------
    function viewable(image: HTMLImageElement): boolean {
        return !image.closest('a, button, dialog, [role="dialog"]') && !image.hasAttribute('data-lwc-view-off');
    }

    function adopt(scope: ParentNode): void {
        const mark = (image: HTMLImageElement): void => {
            if (image.hasAttribute('data-lwc-viewable') || !viewable(image)) return;
            image.setAttribute('data-lwc-viewable', '');
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
        return Array.from(group.querySelectorAll<HTMLImageElement>('img[data-lwc-viewable]'))
            .filter(candidate => candidate.getClientRects().length > 0);
    }

    // ---- geometry -----------------------------------------------------------------------
    function natural(image: HTMLImageElement): { width: number; height: number } {
        const width = image.naturalWidth || image.width || 1;
        const height = image.naturalHeight || image.height || 1;
        return { width, height };
    }

    function measureFit(image: HTMLImageElement): void {
        const phone = window.innerWidth < 640;
        const padX = phone ? 10 : Math.min(96, window.innerWidth * 0.07);
        const padY = phone ? 64 : 56;
        const room = { width: window.innerWidth - padX * 2, height: window.innerHeight - padY * 2 };
        const size = natural(image);
        // Fill the room, but never blow a small picture up past twice its own pixels.
        const scale = Math.min(room.width / size.width, room.height / size.height, 2);
        const width = size.width * scale;
        const height = size.height * scale;
        fit = { left: (window.innerWidth - width) / 2, top: (window.innerHeight - height) / 2, width, height };
        // Zoom far enough to read the source's own pixels twice over, within reason.
        maxZoom = Math.min(8, Math.max(2.5, (size.width / width) * 2));
        picture.style.left = fit.left + 'px';
        picture.style.top = fit.top + 'px';
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
        shell.setAttribute('aria-label', 'Image viewer');
        shell.innerHTML = `
            <div class="lwc-viewer-scrim"></div>
            <div class="lwc-viewer-stage"><img class="lwc-viewer-picture" alt="" draggable="false"></div>
            <button type="button" class="lwc-viewer-button lwc-viewer-close" aria-label="Close">${icon(ICONS.close)}</button>
            <button type="button" class="lwc-viewer-button lwc-viewer-nav" data-step="-1" aria-label="Previous image">${icon(ICONS.prev)}</button>
            <button type="button" class="lwc-viewer-button lwc-viewer-nav" data-step="1" aria-label="Next image">${icon(ICONS.next)}</button>
            <div class="lwc-viewer-dots" aria-hidden="true"></div>`;
        document.body.append(shell);
        stage = shell.querySelector<HTMLElement>('.lwc-viewer-stage')!;
        scrim = shell.querySelector<HTMLElement>('.lwc-viewer-scrim')!;
        picture = shell.querySelector<HTMLImageElement>('.lwc-viewer-picture')!;
        dots = shell.querySelector<HTMLElement>('.lwc-viewer-dots')!;
        [prevButton, nextButton] = Array.from(shell.querySelectorAll<HTMLButtonElement>('.lwc-viewer-nav')) as [HTMLButtonElement, HTMLButtonElement];

        shell.querySelector('.lwc-viewer-close')!.addEventListener('click', () => requestClose());
        prevButton.addEventListener('click', () => step(-1));
        nextButton.addEventListener('click', () => step(1));
        shell.addEventListener('cancel', event => { event.preventDefault(); requestClose(); });
        shell.addEventListener('keydown', onKey);
        wireGestures();
        return shell;
    }

    function syncChrome(): void {
        const many = set.length > 1;
        dialog!.toggleAttribute('data-many', many);
        prevButton.disabled = index === 0;
        nextButton.disabled = index === set.length - 1;
        if (dots.childElementCount !== set.length) {
            dots.replaceChildren(...set.map(() => document.createElement('span')));
        }
        Array.from(dots.children).forEach((dot, i) => dot.toggleAttribute('data-current', i === index));
        picture.alt = set[index]?.alt ?? '';
    }

    function show(image: HTMLImageElement): void {
        picture.src = image.currentSrc || image.src;
        if (image.srcset) picture.srcset = image.srcset; else picture.removeAttribute('srcset');
        measureFit(image);
    }

    // ---- open / close -------------------------------------------------------------------
    async function open(image: HTMLImageElement): Promise<void> {
        if (busy || dialog?.open) return;
        if (!image.complete || !image.naturalWidth) return;
        dialog ??= build();
        busy = true;
        set = neighbours(image);
        index = Math.max(0, set.indexOf(image));
        source = image;
        show(image);
        syncChrome();

        const origin = image.getBoundingClientRect();
        dialog.showModal();
        dialog.setAttribute('data-state', 'opening');
        image.setAttribute('data-lwc-view-away', '');
        dim(0);
        dim(1, FLIGHT_MS * 0.8);
        if (!historyEntry) {
            history.pushState({ ...(history.state ?? {}), lwcViewer: true }, '');
            historyEntry = true;
        }
        await animatePicture(poseOf(origin), { x: 0, y: 0, z: 1 }, FLIGHT_MS);
        dialog.setAttribute('data-state', 'open');
        busy = false;
    }

    // Close through history when we own an entry, so Back and the close button agree.
    function requestClose(): void {
        if (!dialog?.open || busy) return;
        if (historyEntry && history.state?.lwcViewer) {
            history.back();
            return;
        }
        void close();
    }

    async function close(fromPose: Pose = pose): Promise<void> {
        if (!dialog?.open || busy) return;
        busy = true;
        historyEntry = false;
        dialog.setAttribute('data-state', 'closing');
        const home = set[index] ?? source;
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
        dialog.removeAttribute('data-state');
        dialog.removeAttribute('data-zoomed');
        home?.focus({ preventScroll: true });
        busy = false;
    }

    // ---- neighbours -----------------------------------------------------------------------
    async function step(direction: number, carried = 0): Promise<void> {
        const next = index + direction;
        if (busy || next < 0 || next >= set.length) {
            if (carried) await animatePicture(pose, { x: 0, y: 0, z: 1 }, 260);
            return;
        }
        busy = true;
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
        busy = false;
    }

    // ---- keyboard -----------------------------------------------------------------------
    function onKey(event: KeyboardEvent): void {
        const centre = { x: window.innerWidth / 2, y: window.innerHeight / 2 };
        switch (event.key) {
            case 'ArrowLeft': if (pose.z > 1.01) return; event.preventDefault(); void step(-1); break;
            case 'ArrowRight': if (pose.z > 1.01) return; event.preventDefault(); void step(1); break;
            case '+': case '=': event.preventDefault(); void animatePicture(pose, zoomAt(centre.x, centre.y, pose.z * 1.6), 220); break;
            case '-': event.preventDefault(); void animatePicture(pose, zoomAt(centre.x, centre.y, pose.z / 1.6), 220); break;
            case '0': event.preventDefault(); void animatePicture(pose, { x: 0, y: 0, z: 1 }, 220); break;
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
                const m = midpoint();
                mode = 'pinch';
                pinchStart = { distance: m.distance, z: pose.z, mid: { x: m.x, y: m.y } };
                startPose = pose;
                return;
            }
            mode = pose.z > 1.01 ? 'pan' : 'pending';
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
                apply(clampPose({ ...zoomed, x: zoomed.x + m.x - pinchStart.mid.x, y: zoomed.y + m.y - pinchStart.mid.y }));
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
            const dx = event.clientX - start.x;
            const dy = event.clientY - start.y;
            dialog!.removeAttribute('data-dragging');

            if (mode === 'pinch') {
                if (pointers.size === 1) {
                    // Carry on as a pan with the remaining finger.
                    const [rest] = Array.from(pointers.values()) as [{ x: number; y: number }];
                    start = rest; startPose = pose; mode = 'pan';
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
                    if (historyEntry && history.state?.lwcViewer) {
                        pendingDismissPose = pose;
                        history.back();
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
                void animatePicture(pose, target, 300);
                return;
            }
            if (event.target !== picture && pose.z <= 1.01) {
                window.setTimeout(() => { if (lastTap.t === now) requestClose(); }, 260);
            }
        };
        stage.addEventListener('pointerup', finish);
        stage.addEventListener('pointercancel', finish);

        stage.addEventListener('wheel', event => {
            event.preventDefault();
            if (busy) return;
            const factor = Math.exp(-event.deltaY * (event.ctrlKey ? 0.01 : 0.0022));
            apply(zoomAt(event.clientX, event.clientY, pose.z * factor));
        }, { passive: false });

        stage.addEventListener('dblclick', event => event.preventDefault());
    }

    let pendingDismissPose: Pose | null = null;

    window.addEventListener('popstate', () => {
        if (!dialog?.open || !historyEntry) return;
        historyEntry = false;
        const from = pendingDismissPose ?? pose;
        pendingDismissPose = null;
        void close(from);
    });

    window.addEventListener('resize', () => {
        if (!dialog?.open || busy) return;
        const image = set[index];
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

    window.LwcImageViewer = { open: (image: HTMLImageElement) => open(image), adopt };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();
