const DEFAULT_HEADSHOT_WEBP_FALLBACK = "/assets/content-images/headshot.webp";
const DEFAULT_HEADSHOT_JPEG_FALLBACK = "/assets/content-images/headshot.jpg";
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

function getAthleteSearchText(athlete: PlayAthlete): string {
    return `${getAthleteCanonicalName(athlete)} ${getAthleteDisplayName(athlete)}`.toLowerCase();
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
        removeSessionItem("chronoPhenoDifference");
        removeSessionItem("chronoBortzDifference");
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
        return new URL(src, window.location.href).pathname.endsWith("/assets/content-images/headshot.jpg");
    } catch (_) {
        return src.endsWith(DEFAULT_HEADSHOT_JPEG_FALLBACK) || src.endsWith("/assets/content-images/headshot.jpg");
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
    if (!image || isDefaultHeadshotSrc(image.currentSrc || image.src)) {
        return false;
    }

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

function waitForNextPaint<T>(value: T): Promise<T> {
    return new Promise<T>(resolve => {
        requestAnimationFrame(() => {
            requestAnimationFrame(() => resolve(value));
        });
    });
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

    return loaded
        .then(() => {
            if (typeof image.decode === "function" && image.complete && image.naturalWidth > 0) {
                return image.decode().catch(() => {});
            }
            return undefined;
        })
        .then(() => waitForNextPaint(image));
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
        waitForNextPaint(image).then(() => resolveReady(image));
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
            waitForNextPaint(image).then(() => resolveReady(image));
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

function appendHighlightedText(container: HTMLElement, text: string, query: string): void {
    const lowerText = text.toLowerCase();
    const idx = lowerText.indexOf(query);
    if (idx < 0) {
        container.textContent = text;
        return;
    }

    container.append(document.createTextNode(text.slice(0, idx)));
    const strong = document.createElement("strong");
    strong.textContent = text.slice(idx, idx + query.length);
    container.append(strong, document.createTextNode(text.slice(idx + query.length)));
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
    let isBound = false;
    let hasUserEditedInput = false;

    const autocompleteListId = `${input.id}-autocomplete-list`;
    input.setAttribute("role", "combobox");
    input.setAttribute("aria-autocomplete", "list");
    input.setAttribute("aria-controls", autocompleteListId);
    input.setAttribute("aria-expanded", "false");

    function closeAllLists(): void {
        document.querySelectorAll<HTMLElement>(".autocomplete-items")
            .forEach(list => list.remove());
        currentFocus = -1;
        input.setAttribute("aria-expanded", "false");
        input.removeAttribute("aria-activedescendant");
    }

    function addActive(items: HTMLCollectionOf<HTMLDivElement> | null): void {
        if (!items) return;
        removeActive(items);
        if (currentFocus >= items.length) currentFocus = 0;
        if (currentFocus < 0) currentFocus = items.length - 1;
        const activeItem = items[currentFocus];
        if (!activeItem) return;
        activeItem.classList.add("autocomplete-active");
        activeItem.setAttribute("aria-selected", "true");
        input.setAttribute("aria-activedescendant", activeItem.id);
        activeItem.scrollIntoView({ block: "nearest" });
    }

    function removeActive(items: HTMLCollectionOf<HTMLDivElement>): void {
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

    function findExactAthleteMatch(value: string): PlayAthlete | null {
        return athletes.find(athlete => isAthleteInputValue(athlete, value)) || null;
    }

    function renderSelectedAthletePreview(
        athlete: PlayAthlete,
        selectionOptions: AthleteSelectionRenderOptions = {}
    ): Promise<HTMLImageElement | void> {
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

    function renderAthleteMatches(): boolean {
        const query = input.value.trim().toLowerCase();
        const terms = query.split(/\s+/).filter(term => term);
        clearCurrentAthleteSelectionIfInputChanged(input.value);

        closeAllLists();
        if (currentAthlete && isAthleteInputValue(currentAthlete, input.value)) return false;
        if (!terms.length) return false;

        const list = document.createElement("div");
        list.setAttribute("id", autocompleteListId);
        list.setAttribute("class", "autocomplete-items");
        list.setAttribute("role", "listbox");
        list.setAttribute("aria-label", "Athlete suggestions");
        input.parentNode?.appendChild(list);

        let count = 0;
        athletes.forEach((athlete: PlayAthlete) => {
            if (count >= maxItems) return;
            if (!getAthleteCanonicalName(athlete)) return;
            const searchText = getAthleteSearchText(athlete);
            if (terms.every(term => searchText.includes(term))) {
                const first = terms[0];
                if (!first) return;
                const displayName = getAthleteDisplayName(athlete);
                const item = document.createElement("div");
                item.id = `${autocompleteListId}-option-${count}`;
                item.setAttribute("role", "option");
                item.setAttribute("aria-selected", "false");
                appendHighlightedText(item, displayName, first);
                item.dataset.value = athlete.Name;
                if (typeof athlete.ProfilePic === "string") {
                    item.dataset.profilePic = athlete.ProfilePic;
                }

                item.addEventListener("mousedown", event => {
                    event.preventDefault();
                    selectAthlete(athlete);
                    closeAllLists();
                    if (options.focusConfirmAfterSelection !== false) {
                        focusWithoutScrolling(confirmButton);
                    }
                });

                list.appendChild(item);
                count++;
            }
        });

        if (count === 0) {
            list.remove();
            input.setAttribute("aria-expanded", "false");
            return false;
        }

        input.setAttribute("aria-expanded", "true");
        return count > 0;
    }

    function renderAthleteLoadError(): void {
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
        athleteLoadPromise = fetch(athleteApiPath)
            .then(response => response.ok ? response.json() : Promise.reject(new Error("Athlete list request failed")))
            .then((data: unknown) => {
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

                if (!currentAthlete) {
                    renderAthleteMatches();
                }

                return athletes;
            })
            .catch((error: unknown) => {
                console.error("Error fetching athletes:", error);
                renderAthleteLoadError();
                throw error;
            })
            .finally(() => {
                athleteLoadPromise = null;
            });

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

        input.addEventListener("focus", retryAthleteLoad);
        input.addEventListener("input", () => {
            hasUserEditedInput = true;
            retryAthleteLoad();
            if (athleteAutocompleteReady) {
                renderAthleteMatches();
            }
        });

        input.addEventListener("keydown", event => {
            const listElement = document.getElementById(autocompleteListId);
            const list = listElement?.getElementsByTagName("div") ?? null;
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

                const exactMatch = findExactAthleteMatch(input.value);
                if (exactMatch) {
                    selectAthlete(exactMatch);
                    closeAllLists();
                    if (options.focusConfirmAfterSelection !== false) {
                        focusWithoutScrolling(confirmButton);
                    }
                }
            } else if (event.key === "Escape") {
                closeAllLists();
            }
        });

        document.addEventListener("click", event => {
            if (autocompleteRootSelector
                && event.target instanceof Element
                && event.target.closest(autocompleteRootSelector)) return;
            closeAllLists();
        });

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
    const hasDiscountCode = result
        && Array.isArray(result.components)
        && result.components.some(component => component && component.kind === "discountCode");
    if (!hasDiscountCode || !window.addActiveDiscountMetadataToPaymentOffer) return offer;

    try {
        const adjustedOffer = window.addActiveDiscountMetadataToPaymentOffer(offer);
        return isUsablePaymentOffer(adjustedOffer) ? adjustedOffer : null;
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
            "/bortz-age?update=1",
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

export {};
