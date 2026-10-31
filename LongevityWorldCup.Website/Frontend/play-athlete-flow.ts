const DEFAULT_HEADSHOT_WEBP_FALLBACK = "/assets/content-images/play-athlete-placeholder.webp";
const DEFAULT_HEADSHOT_JPEG_FALLBACK = "/assets/content-images/play-athlete-placeholder.jpg";
const ATHLETE_PICTURE_TRANSITION_MS = 180;
const MIN_USABLE_ATHLETE_PICTURE_SIDE = 16;
const PENDING_PAYMENT_OFFER_KEY = "pendingPaymentOffer";

const pictureTransitionTokens = new WeakMap<HTMLElement, number>();
const pictureReadyPromises = new WeakMap<HTMLElement, Promise<unknown>>();

function isPlayObject(value: unknown): value is object {
    return typeof value === "object" && value !== null && !Array.isArray(value);
}

function getDefaultHeadshotWebp(): string {
    return document.body?.dataset.defaultHeadshotWebp || DEFAULT_HEADSHOT_WEBP_FALLBACK;
}

function getDefaultHeadshotJpeg(): string {
    return document.body?.dataset.defaultHeadshotJpeg || DEFAULT_HEADSHOT_JPEG_FALLBACK;
}

function getBrowserStorageItem(storageName: PlayBrowserStorageName, key: string): string | null {
    try {
        return window[storageName].getItem(key);
    } catch (_) {
        return null;
    }
}

function setBrowserStorageItem(storageName: PlayBrowserStorageName, key: string, value: string): boolean {
    try {
        window[storageName].setItem(key, value);
        return true;
    } catch (_) {
        return false;
    }
}

function removeBrowserStorageItem(storageName: PlayBrowserStorageName, key: string): void {
    try {
        window[storageName].removeItem(key);
    } catch (_) {
    }
}

function getLocalItem(key: string): string | null { return getBrowserStorageItem("localStorage", key); }
function setLocalItem(key: string, value: string): boolean { return setBrowserStorageItem("localStorage", key, value); }
function removeLocalItem(key: string): void { removeBrowserStorageItem("localStorage", key); }
function getSessionItem(key: string): string | null { return getBrowserStorageItem("sessionStorage", key); }
function setSessionItem(key: string, value: string): boolean { return setBrowserStorageItem("sessionStorage", key, value); }
function removeSessionItem(key: string): void { removeBrowserStorageItem("sessionStorage", key); }

function hasSubmittedApplication(): boolean {
    return getLocalItem("hasApplication") === "true";
}

function focusWithoutScrolling(element: HTMLElement | null | undefined): void {
    try {
        element?.focus({ preventScroll: true });
    } catch (_) {
        element?.focus();
    }
}

function getAthleteDisplayName(athlete: PlayAthlete | null | undefined): string {
    if (athlete && typeof athlete.DisplayName === "string" && athlete.DisplayName.trim()) {
        return athlete.DisplayName.trim();
    }

    return athlete && typeof athlete.Name === "string" ? athlete.Name : "";
}

function getAthleteCanonicalName(athlete: PlayAthlete | null | undefined): string {
    return athlete && typeof athlete.Name === "string" ? athlete.Name.trim() : "";
}

function isAthleteInputValue(
    athlete: PlayAthlete | null | undefined,
    value: string | null | undefined
): boolean {
    const query = (value || "").trim().toLowerCase();
    const canonicalName = getAthleteCanonicalName(athlete);
    if (!query || !canonicalName) return false;

    return canonicalName.toLowerCase() === query
        || getAthleteDisplayName(athlete).toLowerCase() === query;
}

function normalizeAthleteSearch(value: string): string {
    return value.normalize("NFKD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
}

function getAthleteSearchText(athlete: PlayAthlete): string {
    return normalizeAthleteSearch(`${getAthleteCanonicalName(athlete)} ${getAthleteDisplayName(athlete)}`);
}

function getAthletePictureImageSrc(athlete: PlayAthlete | null | undefined): string {
    const pictureSource = athlete
        ? athlete.ProfilePic || athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb
        : null;
    return pictureSource || getDefaultHeadshotJpeg();
}

function getStoredSelectedAthlete(): PlayAthlete | null {
    const json = getSessionItem("selectedAthlete");
    if (!json) return null;

    try {
        const athlete = JSON.parse(json);
        if (isValidSelectedAthlete(athlete)) {
            return athlete;
        }
    } catch (_) {
    }

    removeSessionItem("selectedAthlete");
    removeSessionItem("tempAthlete");
    return null;
}

function getSavedSelectedAthleteName(): string {
    const selectedAthleteName = getLocalItem("selectedAthleteName");
    return typeof selectedAthleteName === "string" ? selectedAthleteName.trim() : "";
}

function isValidSelectedAthlete(value: unknown): value is PlayAthlete {
    if (!isPlayObject(value)) return false;
    const name = Reflect.get(value, "Name");
    return typeof name === "string" && name.trim().length > 0;
}

function readRequiredSelectedAthlete(): PlayAthlete | null {
    let athlete: unknown = null;

    try {
        const selectedAthleteJson = getSessionItem("selectedAthlete");
        athlete = selectedAthleteJson ? JSON.parse(selectedAthleteJson) : null;
    } catch {
        athlete = null;
    }

    if (!isValidSelectedAthlete(athlete)) {
        removeSessionItem("selectedAthlete");
        removeSessionItem("tempAthlete");
        window.location.replace("/select-athlete");
        return null;
    }

    return athlete;
}

function clearStaleTempAthlete(selectedAthleteName: string): void {
    const tempJson = getSessionItem("tempAthlete");
    if (!tempJson) return;

    try {
        const tempAthlete = JSON.parse(tempJson);
        if (isPlayObject(tempAthlete) && Reflect.get(tempAthlete, "Name") === selectedAthleteName) {
            return;
        }
    } catch (_) {
    }

    removeSessionItem("tempAthlete");
}

function persistSelectedAthlete(athlete: PlayAthlete | null): boolean {
    if (!athlete || !athlete.Name) return false;

    const prevName = getLocalItem("selectedAthleteName");
    if (!isAthleteInputValue(athlete, prevName)) {
        removeSessionItem("biomarkerData");
        removeSessionItem("bioageClock");
        removeSessionItem("chronoPhenoDifference");
        removeSessionItem("chronoBortzDifference");
        removeSessionItem("biomarkerDraftKey");
        removeSessionItem("contactEmail");
        removeLocalItem("contactEmail");
    }

    if (!setSessionItem("selectedAthlete", JSON.stringify(athlete))) {
        return false;
    }

    clearStaleTempAthlete(athlete.Name);
    setLocalItem("selectedAthleteName", athlete.Name);
    return true;
}

function createAthleteOptionThumb(athlete: PlayAthlete): HTMLImageElement {
    const thumb = document.createElement("img");
    thumb.className = "athlete-option-thumb";
    thumb.dataset["athleteName"] = athlete.DisplayName || athlete.Name || "";
    thumb.alt = "";
    thumb.width = 40;
    thumb.height = 40;
    thumb.loading = "lazy";
    thumb.decoding = "async";
    const inspectLoadedImage = watchAthleteImageLoad(thumb, () => {});
    const source = athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb || athlete.ProfilePic || getDefaultHeadshotJpeg();
    thumb.dataset["lwcImageRole"] = source === getDefaultHeadshotJpeg() || source === getDefaultHeadshotWebp()
        ? "illustration" : "athlete";
    thumb.src = source;
    if (thumb.complete) inspectLoadedImage();
    return thumb;
}

function createDefaultAthletePicture(): HTMLPictureElement {
    const picture = document.createElement("picture");
    const webpSource = document.createElement("source");
    webpSource.srcset = getDefaultHeadshotWebp();
    webpSource.type = "image/webp";

    const jpegSource = document.createElement("source");
    jpegSource.srcset = getDefaultHeadshotJpeg();
    jpegSource.type = "image/jpeg";

    const image = document.createElement("img");
    image.src = getDefaultHeadshotJpeg();
    image.alt = "Headshot";
    image.className = "illustration athlete-picture-placeholder";
    image.loading = "lazy";
    image.decoding = "async";
    picture.replaceChildren(webpSource, jpegSource, image);
    return picture;
}

function createDefaultAthleteImage(): HTMLImageElement {
    const image = document.createElement("img");
    image.src = getDefaultHeadshotJpeg();
    image.alt = "Headshot";
    image.className = "illustration athlete-picture-placeholder athlete-picture-next";
    image.loading = "eager";
    image.decoding = "async";
    return image;
}

function createAthletePictureImage(
    altText: string,
    loading: "eager" | "lazy" = "eager"
): HTMLImageElement {
    const image = document.createElement("img");
    image.alt = altText;
    image.className = "illustration athlete-picture-next";
    image.loading = loading;
    image.decoding = "async";
    return image;
}

function isDefaultHeadshotSrc(src: string): boolean {
    if (!src) return false;

    try {
        return new URL(src, window.location.href).pathname.endsWith(DEFAULT_HEADSHOT_JPEG_FALLBACK);
    } catch (_) {
        return src.endsWith(DEFAULT_HEADSHOT_JPEG_FALLBACK);
    }
}

function shouldUseDefaultForLoadedAthleteImage(image: HTMLImageElement | null | undefined): boolean {
    return Boolean(image
        && image.naturalWidth > 0
        && image.naturalHeight > 0
        && !isDefaultHeadshotSrc(image.currentSrc || image.src)
        && (image.naturalWidth < MIN_USABLE_ATHLETE_PICTURE_SIDE || image.naturalHeight < MIN_USABLE_ATHLETE_PICTURE_SIDE));
}

function setDefaultAthleteImageSource(image: HTMLImageElement | null | undefined): boolean {
    if (!image) return false;
    image.dataset["lwcImageRole"] = "illustration";
    if (isDefaultHeadshotSrc(image.currentSrc || image.src)) return false;

    image.classList.add("athlete-picture-placeholder");
    image.src = getDefaultHeadshotJpeg();
    return true;
}

function watchAthleteImageLoad(image: HTMLImageElement, onLoaded: () => void): () => void {
    let hasCompleted = false;
    let fallbackRequested = false;

    function cleanupImageLoadListeners() {
        image.removeEventListener("load", handleImageLoad);
        image.removeEventListener("error", handleImageError);
    }

    function completeImageLoad() {
        if (hasCompleted) return;
        hasCompleted = true;
        cleanupImageLoadListeners();
        onLoaded();
    }

    function scheduleCompletedImageInspection() {
        const inspectCompletedImage = () => {
            if (!hasCompleted && image.complete) {
                handleImageLoad();
            }
        };

        Promise.resolve().then(inspectCompletedImage);
        if (typeof image.decode === "function") {
            image.decode().catch(() => {}).then(inspectCompletedImage);
        }
    }

    function handleImageLoad() {
        if (!fallbackRequested
            && shouldUseDefaultForLoadedAthleteImage(image)
            && setDefaultAthleteImageSource(image)) {
            fallbackRequested = true;
            scheduleCompletedImageInspection();
            return;
        }

        completeImageLoad();
    }

    function handleImageError() {
        if (!fallbackRequested && setDefaultAthleteImageSource(image)) {
            fallbackRequested = true;
            scheduleCompletedImageInspection();
            return;
        }

        completeImageLoad();
    }

    image.addEventListener("load", handleImageLoad);
    image.addEventListener("error", handleImageError);
    return handleImageLoad;
}

function waitForImageElementReady(image: HTMLImageElement): Promise<HTMLImageElement | void> {
    const loaded = image.complete
        ? Promise.resolve()
        : new Promise<void>(resolve => {
            function done(): void {
                image.removeEventListener("load", done);
                image.removeEventListener("error", done);
                resolve();
            }

            image.addEventListener("load", done);
            image.addEventListener("error", done);
        });

    // Decoding is the readiness gate. These frames often belong to a hidden
    // destination panel; waiting for paints here adds idle frames before it
    // can be shown and can stall it entirely in a backgrounded document.
    return loaded.then(() => {
        if (typeof image.decode === "function" && image.complete && image.naturalWidth > 0) {
            return image.decode().catch(() => {});
        }
        return undefined;
    });
}

function waitForAthletePictureFrameReady(frame: HTMLElement | null): Promise<unknown> {
    if (!frame) return Promise.resolve();

    const readyPromise = pictureReadyPromises.get(frame);
    if (readyPromise) return readyPromise;

    const image = frame.querySelector<HTMLImageElement>("img");
    return image ? waitForImageElementReady(image) : Promise.resolve();
}

function nextPictureTransitionToken(frame: HTMLElement): number {
    const token = (pictureTransitionTokens.get(frame) || 0) + 1;
    pictureTransitionTokens.set(frame, token);
    return token;
}

function transitionAthletePicture(frame: HTMLElement, image: HTMLImageElement, src: string): Promise<HTMLImageElement | void> {
    const transitionToken = nextPictureTransitionToken(frame);
    let resolveReady: (value?: HTMLImageElement | void) => void = () => {};
    const readyPromise = new Promise<HTMLImageElement | void>(resolve => { resolveReady = resolve; });
    pictureReadyPromises.set(frame, readyPromise);
    let hasFinished = false;

    function finishReady(): void {
        resolveReady(image);
    }

    function finishImageSwap(): void {
        if (hasFinished) return;
        if (transitionToken !== pictureTransitionTokens.get(frame)) {
            resolveReady();
            return;
        }
        hasFinished = true;

        waitForImageElementReady(image).then(() => {
            if (transitionToken !== pictureTransitionTokens.get(frame)) {
                resolveReady();
                return;
            }

            const currentMedia = Array.from(frame.children).find(child => child !== image);
            if (!currentMedia) {
                image.classList.remove("athlete-picture-next", "is-visible");
                frame.replaceChildren(image);
                finishReady();
                return;
            }

            frame.appendChild(image);
            requestAnimationFrame(() => {
                if (transitionToken !== pictureTransitionTokens.get(frame)) {
                    resolveReady();
                    return;
                }

                image.classList.add("is-visible");
                currentMedia.classList.add("is-exiting");
                window.setTimeout(() => {
                    if (transitionToken !== pictureTransitionTokens.get(frame)) {
                        resolveReady();
                        return;
                    }

                    image.classList.remove("athlete-picture-next", "is-visible");
                    frame.replaceChildren(image);
                    finishReady();
                }, ATHLETE_PICTURE_TRANSITION_MS);
            });
        });
    }

    const inspectLoadedImage = watchAthleteImageLoad(image, finishImageSwap);
    image.src = src || getDefaultHeadshotJpeg();

    if (image.complete) {
        inspectLoadedImage();
    }

    return readyPromise;
}

function replaceAthletePictureImmediately(frame: HTMLElement, image: HTMLImageElement, src: string): Promise<HTMLImageElement | void> {
    const transitionToken = nextPictureTransitionToken(frame);
    let resolveReady: (value?: HTMLImageElement | void) => void = () => {};
    const readyPromise = new Promise<HTMLImageElement | void>(resolve => { resolveReady = resolve; });
    pictureReadyPromises.set(frame, readyPromise);
    let hasFinished = false;
    image.classList.remove("athlete-picture-next", "is-visible");
    function finishImageSwap(): void {
        if (hasFinished) return;
        if (transitionToken !== pictureTransitionTokens.get(frame)) {
            resolveReady();
            return;
        }
        hasFinished = true;

        waitForImageElementReady(image).then(() => {
            if (transitionToken !== pictureTransitionTokens.get(frame)) {
                resolveReady();
                return;
            }

            frame.replaceChildren(image);
            resolveReady(image);
        });
    }

    const inspectLoadedImage = watchAthleteImageLoad(image, finishImageSwap);
    image.src = src || getDefaultHeadshotJpeg();
    if (image.complete) {
        inspectLoadedImage();
    }

    return readyPromise;
}

function renderAthletePicture(
    frame: HTMLElement,
    athlete: PlayAthlete,
    altText: string
): Promise<HTMLImageElement | void> {
    const image = createAthletePictureImage(altText, "eager");
    return replaceAthletePictureImmediately(frame, image, getAthletePictureImageSrc(athlete));
}

function resetAthletePreview({ titleElement, frameElement, defaultTitle }: AthletePictureTargets & { defaultTitle: string }): void {
    titleElement.textContent = defaultTitle;
    transitionAthletePicture(frameElement, createDefaultAthleteImage(), getDefaultHeadshotJpeg());
}

function appendHighlightedText(container: HTMLElement, text: string, terms: string[]): void {
    const searchText = normalizeAthleteSearch(text);
    const sourceSpans: { start: number; end: number }[] = [];
    let offset = 0;
    // Folding can remove accents or expand a ligature. Keep highlights on the original text.
    for (const character of text) {
        const span = { start: offset, end: offset + character.length };
        const foldedLength = normalizeAthleteSearch(character).length;
        if (foldedLength) {
            for (let index = 0; index < foldedLength; index++) sourceSpans.push(span);
        } else {
            const previous = sourceSpans.at(-1);
            if (previous) previous.end = span.end;
        }
        offset = span.end;
    }

    const highlighted = new Array<boolean>(text.length).fill(false);
    for (const term of terms) {
        for (let match = searchText.indexOf(term); match >= 0; match = searchText.indexOf(term, match + term.length)) {
            const start = sourceSpans[match]?.start;
            const end = sourceSpans[match + term.length - 1]?.end;
            if (start !== undefined && end !== undefined) highlighted.fill(true, start, end);
        }
    }

    for (let start = 0; start < text.length;) {
        let end = start + 1;
        while (end < text.length && highlighted[end] === highlighted[start]) end++;
        const content = text.slice(start, end);
        if (highlighted[start]) {
            const strong = document.createElement("strong");
            strong.textContent = content;
            container.append(strong);
        } else {
            container.append(document.createTextNode(content));
        }
        start = end;
    }
}

const SEARCH_ENTER_MS = 180;
const SEARCH_EXIT_MS = 100;
const SEARCH_MOVE_MS = 200;
const SEARCH_EASE_ENTER = "cubic-bezier(0, 0, 0, 1)";
const SEARCH_EASE_EXIT = "cubic-bezier(0.3, 0, 1, 1)";
const SEARCH_EASE_STANDARD = "cubic-bezier(0.2, 0, 0, 1)";

function searchMotionAllowed(): boolean {
    return typeof Element.prototype.animate === "function"
        && !(window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches);
}

function animateAthleteSearch(element: Element, keyframes: Keyframe[], options: KeyframeAnimationOptions): Animation | null {
    if (!searchMotionAllowed()) return null;
    try {
        return element.animate(keyframes, options);
    } catch (_) {
        return null;
    }
}

function createAthleteSelectionController(
    options: AthleteSelectionControllerOptions
): AthleteSelectionController {
    const input = options.input;
    const errorElement = options.errorElement;
    const confirmButton = options.confirmButton;
    const titleElement = options.titleElement;
    const frameElement = options.frameElement;
    const defaultTitle = options.defaultTitle || "Athlete selection";
    const athleteApiPath = options.athleteApiPath || "/api/data/athletes";
    const maxItems = options.maxItems || 5;
    const autocompleteRootSelector = options.autocompleteRootSelector || null;

    let currentAthlete = options.initialAthlete || null;
    let athletes: PlayAthlete[] = [];
    let currentFocus = -1;
    let athleteAutocompleteReady = false;
    let athleteLoadPromise: Promise<PlayAthlete[]> | null = null;
    let athleteLoadController: AbortController | null = null;
    let resumeAthleteLoad = false;
    let isBound = false;
    let hasUserEditedInput = false;
    const geometryObserver = new ResizeObserver(positionAthleteMatches);

    const autocompleteListId = `${input.id}-autocomplete-list`;
    input.setAttribute("role", "combobox");
    input.setAttribute("aria-autocomplete", "list");
    input.setAttribute("aria-controls", autocompleteListId);
    input.setAttribute("aria-expanded", "false");

    function closeAllLists(): void {
        geometryObserver.disconnect();
        const list = document.getElementById(autocompleteListId);
        if (list) dismissAthleteMatches(list);
        input.closest(".play-athlete-panel")?.classList.remove("athlete-search-open");
        currentFocus = -1;
        input.setAttribute("aria-expanded", "false");
        input.removeAttribute("aria-activedescendant");
    }

    function dismissAthleteMatches(list: HTMLElement): void {
        // The closed list leaves the accessibility tree and stops taking input
        // at once; only its picture fades out.
        list.removeAttribute("id");
        list.removeAttribute("role");
        list.setAttribute("aria-hidden", "true");
        list.inert = true;
        list.classList.add("autocomplete-items--leaving");
        list.querySelectorAll("[id], [role]").forEach(item => {
            item.removeAttribute("id");
            item.removeAttribute("role");
        });
        const exit = animateAthleteSearch(list, [
                { opacity: 1, transform: "none" },
                { opacity: 0, transform: "translateY(-4px)" }
            ], { duration: SEARCH_EXIT_MS, easing: SEARCH_EASE_EXIT, fill: "forwards" });
        if (!exit) {
            list.remove();
            return;
        }
        exit.finished.catch(() => {}).finally(() => list.remove());
    }

    function getMatchOptions(list: HTMLElement | null): HTMLDivElement[] {
        return list ? Array.from(list.querySelectorAll<HTMLDivElement>(":scope > [role=option]")) : [];
    }

    function positionAthleteMatches(): void {
        const list = document.getElementById(autocompleteListId);
        const parent = input.parentElement;
        if (!list || !parent) return;

        const viewport = window.visualViewport;
        const gap = 8;
        const visibleTop = (viewport?.offsetTop ?? 0) + gap;
        let visibleBottom = (viewport?.offsetTop ?? 0) + (viewport?.height ?? innerHeight) - gap;
        const dock = document.querySelector(".flow-action-stack--docked")?.getBoundingClientRect();
        if (dock && dock.height > 0 && dock.bottom > visibleTop && dock.top < visibleBottom) {
            visibleBottom = dock.top - gap;
        }

        let inputBox = input.getBoundingClientRect();
        if (document.activeElement === input && inputBox.bottom > visibleBottom) {
            window.scrollBy({ top: inputBox.bottom - visibleBottom + gap, behavior: "instant" });
            inputBox = input.getBoundingClientRect();
        }
        list.style.maxHeight = "";
        const preferredHeight = list.getBoundingClientRect().height;
        const desiredHeight = Math.min(preferredHeight,
            Math.max(0, visibleBottom - visibleTop - inputBox.height - gap * 2));
        const headerBottom = document.querySelector("header")?.getBoundingClientRect().bottom ?? 0;
        const currentAbove = Math.max(0, inputBox.top - Math.max(visibleTop, headerBottom + gap) - gap);
        const currentBelow = Math.max(0, visibleBottom - inputBox.bottom - gap);
        if (document.activeElement === input && Math.max(currentAbove, currentBelow) + 1 < desiredHeight) {
            // Keep the preferred result space when the viewport can provide it,
            // rather than settling for a sliver above or below the field.
            const clearance = Math.min(desiredHeight - currentBelow, inputBox.top - visibleTop);
            if (clearance > 1) {
                window.scrollBy({ top: Math.ceil(clearance), behavior: "instant" });
                inputBox = input.getBoundingClientRect();
            }
        }
        const settledHeaderBottom = document.querySelector("header")?.getBoundingClientRect().bottom ?? 0;
        const listTop = Math.max(visibleTop, settledHeaderBottom + gap);
        const parentBox = parent.getBoundingClientRect();
        const above = Math.max(0, inputBox.top - listTop - gap);
        const below = Math.max(0, visibleBottom - inputBox.bottom - gap);
        const openAbove = below < preferredHeight && above > below;
        list.style.maxHeight = Math.min(preferredHeight, openAbove ? above : below) + "px";
        const inputTop = inputBox.top - parentBox.top - parent.clientTop + parent.scrollTop;
        list.style.top = openAbove ? "auto" : inputTop + inputBox.height + gap + "px";
        list.style.bottom = openAbove ? parent.clientHeight - inputTop + gap + "px" : "auto";
        keepActiveOptionVisible(list);
    }

    function keepActiveOptionVisible(list: HTMLElement): void {
        const activeItem = list.querySelector<HTMLElement>('[aria-selected="true"]');
        if (!activeItem) return;
        const itemBox = activeItem.getBoundingClientRect();
        const listBox = list.getBoundingClientRect();
        const top = listBox.top + list.clientTop;
        const bottom = top + list.clientHeight;
        if (itemBox.top < top) list.scrollTop -= top - itemBox.top;
        else if (itemBox.bottom > bottom) list.scrollTop += itemBox.bottom - bottom;
    }

    function addActive(items: ArrayLike<HTMLDivElement> | null): void {
        if (!items) return;
        removeActive(items);
        if (currentFocus >= items.length) currentFocus = 0;
        if (currentFocus < 0) currentFocus = items.length - 1;
        const activeItem = items[currentFocus];
        if (!activeItem) return;
        activeItem.classList.add("autocomplete-active");
        activeItem.setAttribute("aria-selected", "true");
        input.setAttribute("aria-activedescendant", activeItem.id);
        if (activeItem.parentElement) keepActiveOptionVisible(activeItem.parentElement);
    }

    function removeActive(items: ArrayLike<HTMLDivElement>): void {
        Array.from(items).forEach((item: HTMLDivElement) => {
            item.classList.remove("autocomplete-active");
            item.setAttribute("aria-selected", "false");
        });
        input.removeAttribute("aria-activedescendant");
    }

    function clearCurrentAthleteSelectionIfInputChanged(value: string): void {
        if (!currentAthlete || isAthleteInputValue(currentAthlete, value)) return;

        currentAthlete = null;
        confirmButton.disabled = true;
        resetAthletePreview({ titleElement, frameElement, defaultTitle });
    }

    function findUniqueAthleteMatch(value: string): PlayAthlete | null {
        const query = normalizeAthleteSearch(value.trim());
        if (!query) return null;
        const foldedMatches = athletes.filter(athlete =>
            normalizeAthleteSearch(getAthleteCanonicalName(athlete)) === query
            || normalizeAthleteSearch(getAthleteDisplayName(athlete)) === query);
        return foldedMatches.length === 1 ? foldedMatches[0] ?? null : null;
    }

    function renderSelectedAthletePreview(
        athlete: PlayAthlete,
        selectionOptions: AthleteSelectionRenderOptions = {}
    ): Promise<HTMLImageElement | void> {
        clearSearchFeedback();
        const displayName = getAthleteDisplayName(athlete);
        input.value = displayName;
        titleElement.textContent = displayName;
        const image = createAthletePictureImage(`${displayName} headshot`);
        const pictureReady = selectionOptions.transition === false
            ? replaceAthletePictureImmediately(frameElement, image, getAthletePictureImageSrc(athlete))
            : transitionAthletePicture(frameElement, image, getAthletePictureImageSrc(athlete));

        confirmButton.disabled = false;
        currentAthlete = athlete;
        return pictureReady;
    }

    function selectAthlete(
        athlete: PlayAthlete,
        selectionOptions: AthleteSelectionRenderOptions = {}
    ): void {
        closeAllLists();
        renderSelectedAthletePreview(athlete, selectionOptions.transition === undefined ? { transition: true } : selectionOptions);
        if (typeof options.onAthleteSelected === "function") {
            options.onAthleteSelected(athlete, api);
        }
    }

    function hydrateStoredAthleteSelection(): boolean {
        const storedAthlete = currentAthlete || getStoredSelectedAthlete();
        if (!storedAthlete || !storedAthlete.Name) return false;

        renderSelectedAthletePreview(storedAthlete, { transition: false });
        return true;
    }

    function clearSearchFeedback(): void {
        if (!errorElement.classList.contains("athlete-search-empty")) return;
        errorElement.replaceChildren();
        errorElement.classList.remove("athlete-search-empty");
    }

    function createAthleteMatch(athlete: PlayAthlete): HTMLDivElement {
        const item = document.createElement("div");
        item.setAttribute("role", "option");
        item.setAttribute("aria-selected", "false");
        const name = document.createElement("span");
        name.className = "athlete-option-name";
        item.append(createAthleteOptionThumb(athlete), name);
        item.dataset.value = athlete.Name;
        if (typeof athlete.ProfilePic === "string") {
            item.dataset.profilePic = athlete.ProfilePic;
        }

        item.addEventListener("mousedown", event => {
            event.preventDefault();
            if (!item.hasAttribute("role")) return;
            selectAthlete(athlete);
            closeAllLists();
            if (options.focusConfirmAfterSelection !== false) {
                focusWithoutScrolling(confirmButton);
            }
        });
        return item;
    }

    function renderAthleteMatches(): boolean {
        const query = normalizeAthleteSearch(input.value.trim());
        const terms = query.split(/\s+/).filter(term => term);
        clearCurrentAthleteSelectionIfInputChanged(input.value);

        clearSearchFeedback();
        const existingList = document.getElementById(autocompleteListId);
        const closeMatches = (): false => {
            closeAllLists();
            return false;
        };
        if (!athleteAutocompleteReady) return closeMatches();
        if (currentAthlete && isAthleteInputValue(currentAthlete, input.value)) return closeMatches();
        if (!terms.length) return closeMatches();

        const matches: PlayAthlete[] = [];
        for (const athlete of athletes) {
            if (matches.length >= maxItems) break;
            if (!getAthleteCanonicalName(athlete)) continue;
            if (terms.every(term => getAthleteSearchText(athlete).includes(term))) matches.push(athlete);
        }

        if (matches.length === 0) {
            closeAllLists();
            errorElement.classList.add("athlete-search-empty");
            errorElement.setAttribute("role", "status");
            errorElement.textContent = "No matching athlete.";
            return false;
        }

        // Keep the open list and its rows across keystrokes: rows that still
        // match slide to their new place, new rows fade in, and rows that no
        // longer match fade out where they were instead of the list redrawing.
        const animate = searchMotionAllowed();
        currentFocus = -1;
        input.removeAttribute("aria-activedescendant");
        const list = existingList || document.createElement("div");
        const isNewList = !existingList;
        if (isNewList) {
            list.setAttribute("id", autocompleteListId);
            list.setAttribute("class", "autocomplete-items");
            list.setAttribute("role", "listbox");
            list.setAttribute("aria-label", "Athlete suggestions");
            input.parentNode?.appendChild(list);
        }

        const previous = new Map<string, HTMLDivElement>();
        const previousTops = new Map<HTMLDivElement, number>();
        const previousBoxes = new Map<HTMLDivElement, { top: number; left: number; width: number; height: number }>();
        getMatchOptions(list).forEach(item => {
            previous.set(item.dataset.value || "", item);
            item.getAnimations?.().forEach(animation => animation.cancel());
            if (animate) {
                previousTops.set(item, item.offsetTop);
                previousBoxes.set(item, { top: item.offsetTop, left: item.offsetLeft, width: item.offsetWidth, height: item.offsetHeight });
            }
        });
        list.querySelectorAll(".autocomplete-item--leaving").forEach(item => item.remove());

        const ordered = matches.map((athlete, index) => {
            const item = previous.get(athlete.Name) || createAthleteMatch(athlete);
            previous.delete(athlete.Name);
            item.id = `${autocompleteListId}-option-${index}`;
            item.classList.remove("autocomplete-active");
            item.setAttribute("aria-selected", "false");
            const name = item.querySelector<HTMLElement>(".athlete-option-name");
            if (name) {
                name.replaceChildren();
                appendHighlightedText(name, getAthleteDisplayName(athlete), terms);
            }
            return item;
        });

        const leaving = Array.from(previous.values());
        // Only current options determine the popup's size and sibling spacing.
        // Departing rows may animate inside that content, never extend its scroll area.
        leaving.forEach(item => item.remove());
        ordered.forEach(item => list.appendChild(item));
        input.setAttribute("aria-expanded", "true");
        input.closest(".play-athlete-panel")?.classList.add("athlete-search-open");
        positionAthleteMatches();
        const contentTop = ordered[0]?.offsetTop ?? 0;
        const last = ordered[ordered.length - 1];
        const contentBottom = last ? last.offsetTop + last.offsetHeight : contentTop;
        leaving.forEach(item => {
            item.removeAttribute("role");
            item.removeAttribute("id");
            item.removeAttribute("aria-selected");
            item.setAttribute("aria-hidden", "true");
            item.inert = true;
            const box = previousBoxes.get(item);
            if (!animate || !box || box.top < contentTop || box.top + box.height > contentBottom
                || box.left + box.width > list.clientWidth) return;
            item.classList.add("autocomplete-item--leaving");
            item.style.top = `${box.top}px`;
            item.style.left = `${box.left}px`;
            item.style.width = `${box.width}px`;
            list.appendChild(item);
            const exit = animateAthleteSearch(item, [
                { opacity: 1, transform: "none" },
                { opacity: 0, transform: "translateX(-10px) scale(0.98)" }
            ], { duration: SEARCH_EXIT_MS, easing: SEARCH_EASE_EXIT, fill: "forwards" });
            if (exit) exit.finished.catch(() => {}).finally(() => item.remove());
            else item.remove();
        });

        if (animate) {
            let entering = 0;
            ordered.forEach(item => {
                const before = previousTops.get(item);
                if (before === undefined) {
                    animateAthleteSearch(item, [
                        { opacity: 0, transform: "translateY(-6px)" },
                        { opacity: 1, transform: "none" }
                    ], { duration: SEARCH_ENTER_MS, delay: (isNewList ? 0 : 60) + Math.min(entering, 4) * 28, easing: SEARCH_EASE_ENTER, fill: "backwards" });
                    entering += 1;
                    return;
                }
                // A retained row can move only within the new content bounds;
                // its old position may be below a now much shorter popup.
                const startTop = Math.max(contentTop, Math.min(before, contentBottom - item.offsetHeight));
                const shift = startTop - item.offsetTop;
                if (Math.abs(shift) < 1) return;
                animateAthleteSearch(item, [
                    { transform: `translateY(${shift}px)` },
                    { transform: "none" }
                ], { duration: SEARCH_MOVE_MS, easing: SEARCH_EASE_STANDARD });
            });
        }

        geometryObserver.observe(input);
        geometryObserver.observe(input.parentElement || input);
        const panel = input.closest(".play-athlete-panel");
        if (panel) geometryObserver.observe(panel);
        const visual = panel?.querySelector(".play-athlete-visual");
        if (visual) geometryObserver.observe(visual);
        document.querySelectorAll(".flow-action-stack, .flow-action-dock-placeholder").forEach(element => {
            geometryObserver.observe(element);
        });
        positionAthleteMatches();
        if (isNewList && animate) {
            const opensAbove = list.style.bottom !== "auto" && list.style.bottom !== "";
            animateAthleteSearch(list, [
                { opacity: 0, transform: `translateY(${opensAbove ? 6 : -6}px)` },
                { opacity: 1, transform: "none" }
            ], { duration: SEARCH_ENTER_MS, easing: SEARCH_EASE_ENTER, fill: "backwards" });
        }
        return true;
    }

    function parentForMatches(): Element {
        return input.parentElement || input;
    }

    function renderAthleteLoadError(): void {
        closeAllLists();
        clearSearchFeedback();
        errorElement.replaceChildren();
        errorElement.setAttribute("role", "alert");

        const message = document.createElement("span");
        message.textContent = "Athlete list could not load. Check your connection and try again.";

        const retryButton = document.createElement("button");
        retryButton.type = "button";
        retryButton.className = "athlete-load-retry";
        retryButton.textContent = "Retry";
        retryButton.addEventListener("click", () => {
            retryAthleteLoad();
            input.focus({ preventScroll: true });
        });

        errorElement.append(message, retryButton);
    }

    function loadAthletes(loadOptions: AthleteSelectionLoadOptions = {}): Promise<PlayAthlete[]> {
        if (athleteAutocompleteReady) return Promise.resolve(athletes);
        if (athleteLoadPromise) return athleteLoadPromise;
        errorElement.replaceChildren();
        errorElement.setAttribute("role", "status");
        const controller = new AbortController();
        athleteLoadController = controller;
        const request = fetch(athleteApiPath, { signal: controller.signal })
            .then(response => response.ok ? response.json() : Promise.reject(new Error("Athlete list request failed")))
            .then((data: unknown) => {
                if (controller.signal.aborted) return athletes;
                if (!Array.isArray(data)) {
                    throw new Error("Athlete list response was invalid");
                }

                athletes = data.filter(isValidSelectedAthlete);
                athleteAutocompleteReady = true;
                const saved = getSavedSelectedAthleteName();
                if (saved && !currentAthlete && !hasUserEditedInput) {
                    const match = athletes.find(athlete => isAthleteInputValue(athlete, saved));
                    if (match) {
                        selectAthlete(match, { transition: loadOptions.savedSelectionTransition !== false });
                    }
                }

                if (!currentAthlete && document.activeElement === input) {
                    renderAthleteMatches();
                }

                return athletes;
            })
            .catch((error: unknown) => {
                // Only our own page-lifecycle cancellation is quiet. Transport
                // and data errors on the active page still expose Retry.
                if (controller.signal.aborted) return athletes;
                console.error("Error fetching athletes:", error);
                renderAthleteLoadError();
                throw error;
            })
            .finally(() => {
                if (athleteLoadPromise === request) athleteLoadPromise = null;
                if (athleteLoadController === controller) athleteLoadController = null;
            });
        athleteLoadPromise = request;
        return athleteLoadPromise;
    }

    function retryAthleteLoad(): void {
        if (!athleteAutocompleteReady) {
            loadAthletes().catch(() => {});
        }
    }

    function bind(): AthleteSelectionController {
        if (isBound) return api;
        isBound = true;
        window.addEventListener('pagehide', () => {
            resumeAthleteLoad = athleteLoadController !== null;
            athleteLoadController?.abort();
            closeAllLists();
        });
        window.addEventListener('pageshow', event => {
            if (!event.persisted || !resumeAthleteLoad) return;
            resumeAthleteLoad = false;
            if (athleteLoadPromise) void athleteLoadPromise.then(retryAthleteLoad, retryAthleteLoad);
            else retryAthleteLoad();
        });

        input.addEventListener("focus", () => {
            retryAthleteLoad();
            if (athleteAutocompleteReady) renderAthleteMatches();
        });
        input.addEventListener("blur", closeAllLists);
        input.addEventListener("input", () => {
            hasUserEditedInput = true;
            retryAthleteLoad();
            if (athleteAutocompleteReady) {
                renderAthleteMatches();
            }
        });

        input.addEventListener("keydown", event => {
            if (event.isComposing) return;
            if ((event.key === "ArrowDown" || event.key === "ArrowUp")
                && !document.getElementById(autocompleteListId) && athleteAutocompleteReady) {
                renderAthleteMatches();
            }
            const listElement = document.getElementById(autocompleteListId);
            const list = listElement ? getMatchOptions(listElement) : null;
            if (event.key === "ArrowDown") {
                event.preventDefault();
                currentFocus++;
                addActive(list);
            } else if (event.key === "ArrowUp") {
                event.preventDefault();
                currentFocus--;
                addActive(list);
            } else if (event.key === "Enter") {
                event.preventDefault();
                if (currentFocus > -1 && list) {
                    list[currentFocus]?.dispatchEvent(new MouseEvent("mousedown"));
                    return;
                }

                const uniqueMatch = findUniqueAthleteMatch(input.value);
                if (uniqueMatch) {
                    selectAthlete(uniqueMatch);
                    closeAllLists();
                    if (options.focusConfirmAfterSelection !== false) {
                        focusWithoutScrolling(confirmButton);
                    }
                }
            } else if (event.key === "Escape") {
                if (listElement) event.preventDefault();
                closeAllLists();
            } else if (event.key === "Tab") {
                closeAllLists();
            }
        });

        document.addEventListener("click", event => {
            if (autocompleteRootSelector
                && event.target instanceof Element
                && event.target.closest(autocompleteRootSelector)) return;
            closeAllLists();
        });
        window.addEventListener("resize", positionAthleteMatches);
        window.addEventListener("scroll", event => {
            if (event.target !== document.getElementById(autocompleteListId)) positionAthleteMatches();
        }, true);
        window.visualViewport?.addEventListener("resize", positionAthleteMatches);
        window.visualViewport?.addEventListener("scroll", positionAthleteMatches);

        return api;
    }

    function start(startOptions: AthleteSelectionStartOptions = {}): AthleteSelectionController {
        bind();
        if (startOptions.hydrate !== false) {
            hydrateStoredAthleteSelection();
        }
        if (startOptions.load !== false) {
            loadAthletes().catch(() => {});
        }
        return api;
    }

    function setCurrentAthlete(athlete: PlayAthlete | null): void {
        currentAthlete = athlete;
    }

    const api: AthleteSelectionController = {
        bind,
        start,
        loadAthletes,
        retryAthleteLoad,
        renderAthleteMatches,
        hydrateStoredAthleteSelection,
        hasPendingSavedSelection: () => Boolean(getSavedSelectedAthleteName()) && !currentAthlete,
        getCurrentAthlete: () => currentAthlete,
        getPreviewReady: () => waitForAthletePictureFrameReady(frameElement),
        setCurrentAthlete,
        selectAthlete,
        closeAllLists
    };

    return api;
}

function serializePendingPaymentOffer(offer: unknown): string | null {
    if (!isUsablePaymentOffer(offer)) return null;

    try {
        const serializedOffer = JSON.stringify(offer);
        return typeof serializedOffer === "string" && serializedOffer
            ? serializedOffer
            : null;
    } catch (_) {
        return null;
    }
}

function isUsablePaymentOffer(paymentOffer: unknown): paymentOffer is PendingPaymentOffer {
    if (!paymentOffer || typeof paymentOffer !== "object" || Array.isArray(paymentOffer)) return false;
    if (!("source" in paymentOffer)
        || !("offerType" in paymentOffer)
        || !("currency" in paymentOffer)
        || !("amountUsd" in paymentOffer)) {
        return false;
    }

    return typeof paymentOffer.source === "string"
        && paymentOffer.source.trim().length > 0
        && typeof paymentOffer.offerType === "string"
        && paymentOffer.offerType.trim().length > 0
        && typeof paymentOffer.currency === "string"
        && paymentOffer.currency.trim().length > 0
        && typeof paymentOffer.amountUsd === "number"
        && Number.isFinite(paymentOffer.amountUsd)
        && paymentOffer.amountUsd >= 0;
}

function preserveAppliedDiscountMetadata(offer: PendingPaymentOffer, result: DiscountBreakdown | null): PendingPaymentOffer | null {
    const adjustedOffer: PendingPaymentOffer = {
        ...offer,
        perfectGuessDiscount: Boolean(
            result
            && Array.isArray(result.components)
            && result.components.some(component => component && component.kind === "perfectGuess")
        )
    };
    const hasDiscountCode = result
        && Array.isArray(result.components)
        && result.components.some(component => component && component.kind === "discountCode");
    if (!hasDiscountCode || !window.addActiveDiscountMetadataToPaymentOffer) return adjustedOffer;

    try {
        const offerWithDiscount = window.addActiveDiscountMetadataToPaymentOffer(adjustedOffer);
        return isUsablePaymentOffer(offerWithDiscount) ? offerWithDiscount : null;
    } catch (_) {
        return null;
    }
}

function notifyPaymentPreparationFailure(retryButton?: HTMLButtonElement | null): void {
    const message = "Payment details could not be prepared. Refresh the page and try again.";
    const alertPromise = typeof window.customAlert === "function"
        ? window.customAlert(message)
        : Promise.resolve(window.alert(message));
    alertPromise?.then?.(() => retryButton?.focus());
}

function notifyPaymentStorageFailure(retryButton?: HTMLButtonElement | null): void {
    const message = "Payment details could not be saved. Enable browser storage and try again.";
    const alertPromise = typeof window.customAlert === "function"
        ? window.customAlert(message)
        : Promise.resolve(window.alert(message));
    alertPromise?.then?.(() => retryButton?.focus());
}

function setPendingPaymentOffer(offer: unknown, retryButton?: HTMLButtonElement | null): boolean {
    if (!isUsablePaymentOffer(offer)) {
        notifyPaymentPreparationFailure(retryButton);
        return false;
    }

    let effectiveOffer: unknown = offer;
    try {
        effectiveOffer = window.applyPaymentAdjustmentsToPaymentOffer
            ? window.applyPaymentAdjustmentsToPaymentOffer(offer)
            : window.applyFreePassToPaymentOffer
                ? window.applyFreePassToPaymentOffer(offer)
                : offer;
    } catch (_) {
        notifyPaymentPreparationFailure(retryButton);
        return false;
    }

    const serializedOffer = serializePendingPaymentOffer(effectiveOffer);
    if (!serializedOffer) {
        notifyPaymentPreparationFailure(retryButton);
        return false;
    }

    if (setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedOffer)) return true;

    notifyPaymentStorageFailure(retryButton);
    return false;
}

function clearPendingPaymentOffer(): void {
    removeSessionItem(PENDING_PAYMENT_OFFER_KEY);
}

function createPriceHtmlFallback(result: DiscountBreakdown): string {
    const oldText = `$${result.basePriceUsd}`;
    if (result.finalPriceUsd < result.basePriceUsd) {
        return `<span class="pro-old-price">${oldText}</span> <span class="pro-new-price">${result.finalPriceText}</span>`;
    }
    return `<span class="pro-new-price">${oldText}</span>`;
}

function createDashboardButton(
    label: string,
    className: string,
    href: string,
    beforeNavigate?: (button: HTMLButtonElement) => boolean | void
): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = className;
    button.innerHTML = label;
    button.addEventListener("click", () => {
        if (typeof beforeNavigate === "function" && beforeNavigate(button) === false) return;
        window.location.href = href;
    });
    return button;
}

function createGoProDiscountSummary(result: DiscountBreakdown): HTMLDivElement | null {
    if (!window.proDiscounts || typeof window.proDiscounts.buildDiscountBreakdown !== "function") {
        return null;
    }
    const wrapper = document.createElement("div");
    wrapper.className = "pro-discount-box";

    const breakdownLine = document.createElement("p");
    breakdownLine.className = "pro-discount-breakdown";
    if (Array.isArray(result.components) && result.components.some(component => component && component.isBadge)) {
        breakdownLine.classList.add("pro-discount-breakdown--with-badges");
    }
    if (typeof window.proDiscounts.createBreakdownHtml === "function") {
        breakdownLine.innerHTML = window.proDiscounts.createBreakdownHtml(result);
    } else {
        breakdownLine.textContent = window.proDiscounts.createBreakdownText(result);
    }

    wrapper.appendChild(breakdownLine);
    return wrapper;
}

function renderAthleteDashboardHeader(
    athlete: PlayAthlete,
    { titleElement, frameElement }: AthletePictureTargets
): Promise<HTMLImageElement | void> {
    const athleteDisplayName = getAthleteDisplayName(athlete);
    titleElement.textContent = athleteDisplayName;
    return renderAthletePicture(frameElement, athlete, `${athleteDisplayName} headshot`);
}

function refreshFlowActionDock(): void {
    const dock = window.LwcFlowActionDock;
    if (typeof dock?.refreshNow === "function") {
        dock.refreshNow();
        return;
    }

    dock?.refresh?.();
}

function renderDashboardActions(
    athlete: PlayAthlete,
    options: AthleteDashboardActionsOptions
): Promise<void> {
    const dynamicActions = options.dynamicActionsElement;
    const discountElement = options.discountElement;
    dynamicActions.replaceChildren();
    discountElement?.replaceChildren();
    const ready = Promise.resolve(window.modulesReady || undefined).catch(() => {});

    return ready.catch(() => {}).then(() => {
        dynamicActions.replaceChildren();
        discountElement?.replaceChildren();

        const isDemoPro = Boolean(options.demoPro);
        const isPro = isDemoPro || (window.isAthletePro ? window.isAthletePro(athlete) : false);
        const challengeButton = createDashboardButton(
            "<span class=\"dashboard-action-label flow-action__label\">Longevitymaxxing</span><i class=\"fas fa-calendar-check\" aria-hidden=\"true\"></i>",
            "option-button grey flow-action flow-action--secondary",
            "/longevitymaxxing",
            () => clearPendingPaymentOffer()
        );

        if (isPro) {
            const phenoButton = createDashboardButton(
                "<span class=\"dashboard-action-label flow-action__label\">Update Pheno&nbsp;Age</span><i class=\"fas fa-rocket\" aria-hidden=\"true\"></i>",
                "option-button grey flow-action flow-action--secondary",
                "/pheno-age?update=1",
                () => clearPendingPaymentOffer()
            );
            const bortzButton = createDashboardButton(
                "<span class=\"dashboard-action-label flow-action__label\">Update Bortz&nbsp;Age</span><i class=\"fas fa-rocket\" aria-hidden=\"true\"></i>",
                "option-button grey flow-action flow-action--secondary",
                "/bortz-age?update=1",
                () => clearPendingPaymentOffer()
            );
            dynamicActions.append(challengeButton, phenoButton, bortzButton);
            refreshFlowActionDock();
            return;
        }

        const submitButton = createDashboardButton(
            "<span class=\"dashboard-action-label flow-action__label\">Submit new results</span><i class=\"fas fa-rocket\" aria-hidden=\"true\"></i>",
            "option-button grey flow-action flow-action--secondary",
            "/pheno-age?update=1",
            () => clearPendingPaymentOffer()
        );
        const goProDiscountResult =
            window.proDiscounts && typeof window.proDiscounts.buildDiscountBreakdown === "function"
                ? window.proDiscounts.buildDiscountBreakdown(athlete, { isOnLeaderboard: true })
                : null;
        const goProPriceHtml = window.hasFreePass && window.hasFreePass()
            ? "<span class=\"pro-new-price\">free</span>"
            : goProDiscountResult
                ? (typeof window.proDiscounts.createPriceHtml === "function" ? window.proDiscounts.createPriceHtml(goProDiscountResult) : createPriceHtmlFallback(goProDiscountResult))
                : null;
        const goProButton = createDashboardButton(
            goProPriceHtml
                ? `<span class="dashboard-action-label flow-action__label">Go pro for&nbsp;${goProPriceHtml}</span><i class="fas fa-bolt" aria-hidden="true"></i>`
                : "<span class=\"dashboard-action-label flow-action__label\">Go pro</span><i class=\"fas fa-bolt\" aria-hidden=\"true\"></i>",
            "option-button green flow-action",
            "/bortz-age?update=1&upgrade=1",
            button => {
                const amountUsd =
                    goProDiscountResult && Number.isFinite(goProDiscountResult.finalPriceUsd)
                        ? goProDiscountResult.finalPriceUsd
                        : 100;
                const paymentOffer = preserveAppliedDiscountMetadata({
                    source: "go-pro-upgrade",
                    offerType: "pro",
                    currency: "USD",
                    amountUsd
                }, goProDiscountResult);
                return setPendingPaymentOffer(paymentOffer, button);
            }
        );

        dynamicActions.append(challengeButton, submitButton, goProButton);
        const goProDiscountSummary = goProDiscountResult ? createGoProDiscountSummary(goProDiscountResult) : null;
        if (goProDiscountSummary && discountElement) {
            discountElement.append(goProDiscountSummary);
        }
        refreshFlowActionDock();
    });
}

window.playAthleteFlow = {
    getBrowserStorageItem,
    setBrowserStorageItem,
    removeBrowserStorageItem,
    getLocalItem,
    setLocalItem,
    removeLocalItem,
    getSessionItem,
    setSessionItem,
    removeSessionItem,
    hasSubmittedApplication,
    focusWithoutScrolling,
    getAthleteDisplayName,
    getAthleteCanonicalName,
    isAthleteInputValue,
    getAthleteSearchText,
    getAthletePictureImageSrc,
    getStoredSelectedAthlete,
    getSavedSelectedAthleteName,
    isValidSelectedAthlete,
    readRequiredSelectedAthlete,
    clearStaleTempAthlete,
    persistSelectedAthlete,
    createDefaultAthletePicture,
    createDefaultAthleteImage,
    createAthletePictureImage,
    transitionAthletePicture,
    replaceAthletePictureImmediately,
    waitForAthletePictureFrameReady,
    renderAthletePicture,
    createAthleteSelectionController,
    serializePendingPaymentOffer,
    isUsablePaymentOffer,
    preserveAppliedDiscountMetadata,
    createPriceHtmlFallback,
    setPendingPaymentOffer,
    clearPendingPaymentOffer,
    renderAthleteDashboardHeader,
    renderDashboardActions
};


// ---------------------------------------------------------------- player stage
// The player card is a stage: the portrait sits over a blurred, darkened copy
// of itself, so a portrait never floats between grey bars. While the reader
// searches, the stage eases into a slim banner and spotlights whichever
// athlete is highlighted in the suggestions; the caption rolls over when the
// choice lands. Everything here watches the DOM the flow already renders.

function stageMotionAllowed(): boolean {
    return !window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

function cssImageUrl(src: string): string {
    return `url("${src.replace(/["\\\n]/g, encodeURIComponent)}")`;
}

function visibleStageSource(frame: HTMLElement): string {
    const images = Array.from(frame.querySelectorAll<HTMLImageElement>("img.illustration"))
        .filter(image => !image.classList.contains("is-exiting") && !image.closest(".is-exiting"));
    const image = images[images.length - 1];
    return image ? image.currentSrc || image.src : "";
}

function bindStageBackdrop(frame: HTMLElement): void {
    let shown = "";
    const update = (): void => {
        if (frame.dataset["stagePreview"]) return;
        const source = visibleStageSource(frame);
        if (!source || source === shown) return;
        shown = source;
        frame.style.setProperty("--stage-image", cssImageUrl(source));
    };
    new MutationObserver(update).observe(frame, { childList: true, subtree: true, attributes: true, attributeFilter: ["src", "class"] });
    frame.addEventListener("load", update, true);
    frame.addEventListener("stage-restore", () => { shown = ""; update(); });
    update();
}

function bindStageCaption(title: HTMLElement | null): void {
    if (!title) return;
    let text = title.textContent || "";
    new MutationObserver(() => {
        const next = title.textContent || "";
        if (next === text) return;
        text = next;
        if (!stageMotionAllowed() || title.closest("[hidden]")) return;
        title.animate([
            { opacity: 0, transform: "translateY(10px)", filter: "blur(2px)" },
            { opacity: 1, transform: "none", filter: "none" }
        ], { duration: 260, easing: "cubic-bezier(0, 0, 0, 1)" });
    }).observe(title, { childList: true, characterData: true, subtree: true });
}

function bindStageSpotlight(input: HTMLInputElement, card: HTMLElement, frame: HTMLElement): void {
    const spotlight = document.createElement("span");
    spotlight.className = "athlete-stage-spotlight";
    spotlight.setAttribute("aria-hidden", "true");
    card.appendChild(spotlight);
    let current = "";

    const show = (source: string): void => {
        if (source === current) return;
        current = source;
        if (!source) {
            delete frame.dataset["stagePreview"];
            card.classList.remove("is-spotlit");
            frame.dispatchEvent(new Event("stage-restore"));
            return;
        }
        frame.dataset["stagePreview"] = "true";
        frame.style.setProperty("--stage-image", cssImageUrl(source));
        spotlight.style.setProperty("--stage-spotlight", cssImageUrl(source));
        card.classList.add("is-spotlit");
        if (stageMotionAllowed()) {
            spotlight.animate([
                { transform: "scale(0.82)", opacity: 0.4 },
                { transform: "none", opacity: 1 }
            ], { duration: 240, easing: "cubic-bezier(0.34, 1.4, 0.64, 1)" });
        }
    };

    const sourceOf = (option: Element | null): string => {
        const thumb = option?.querySelector<HTMLImageElement>(".athlete-option-thumb");
        return thumb && thumb.dataset["lwcImageRole"] !== "illustration" ? thumb.currentSrc || thumb.src : "";
    };

    const sync = (): void => {
        if (input.getAttribute("aria-expanded") !== "true") {
            show("");
            return;
        }
        const activeId = input.getAttribute("aria-activedescendant");
        show(activeId ? sourceOf(document.getElementById(activeId)) : "");
    };

    new MutationObserver(sync).observe(input, { attributes: true, attributeFilter: ["aria-activedescendant", "aria-expanded"] });
    document.addEventListener("pointerover", event => {
        if (!(event.target instanceof Element) || event.pointerType === "touch") return;
        const option = event.target.closest(".autocomplete-items:not(.autocomplete-items--leaving) [role=option]");
        if (option) show(sourceOf(option));
    });
    document.addEventListener("pointerout", event => {
        if (!(event.target instanceof Element) || !event.target.closest(".autocomplete-items")) return;
        if (event.relatedTarget instanceof Element && event.relatedTarget.closest(".autocomplete-items")) return;
        sync();
    });
}

// The Select command lights up with a sweep the moment a choice makes it usable.
function bindStageArming(button: HTMLButtonElement | null): void {
    if (!button) return;
    let wasDisabled = button.disabled;
    new MutationObserver(() => {
        const disabled = button.disabled;
        if (wasDisabled && !disabled && stageMotionAllowed()) {
            button.classList.remove("is-arming");
            void button.offsetWidth;
            button.classList.add("is-arming");
            window.setTimeout(() => button.classList.remove("is-arming"), 900);
        }
        wasDisabled = disabled;
    }).observe(button, { attributes: true, attributeFilter: ["disabled"] });
}

function initPlayerStage(): void {
    document.querySelectorAll<HTMLElement>(".play-athlete-card .athlete-picture-frame, .play-dashboard-card .athlete-picture-frame")
        .forEach(bindStageBackdrop);
    bindStageCaption(document.getElementById("athleteSelectionTitle"));
    bindStageCaption(document.getElementById("athleteDashboardTitle"));
    const input = document.getElementById("playAthleteInput");
    const card = document.querySelector<HTMLElement>(".play-athlete-card");
    const frame = document.getElementById("athleteSelectionPicture");
    if (input instanceof HTMLInputElement && card && frame) bindStageSpotlight(input, card, frame);
    bindStageArming(document.getElementById("playConfirmAthleteBtn") as HTMLButtonElement | null);
}

if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initPlayerStage, { once: true });
else initPlayerStage();

export {};
