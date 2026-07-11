const DEFAULT_HEADSHOT_WEBP_FALLBACK = "/assets/content-images/headshot.webp";
const DEFAULT_HEADSHOT_JPEG_FALLBACK = "/assets/content-images/headshot.jpg";
const ATHLETE_PICTURE_TRANSITION_MS = 180;
const MIN_USABLE_ATHLETE_PICTURE_SIDE = 16;
const PENDING_PAYMENT_OFFER_KEY = "pendingPaymentOffer";

const pictureTransitionTokens = new WeakMap();
const pictureReadyPromises = new WeakMap();

function getDefaultHeadshotWebp() {
    return document.body?.dataset.defaultHeadshotWebp || DEFAULT_HEADSHOT_WEBP_FALLBACK;
}

function getDefaultHeadshotJpeg() {
    return document.body?.dataset.defaultHeadshotJpeg || DEFAULT_HEADSHOT_JPEG_FALLBACK;
}

function getBrowserStorageItem(storageName, key) {
    try {
        return window[storageName].getItem(key);
    } catch (_) {
        return null;
    }
}

function setBrowserStorageItem(storageName, key, value) {
    try {
        window[storageName].setItem(key, value);
        return true;
    } catch (_) {
        return false;
    }
}

function removeBrowserStorageItem(storageName, key) {
    try {
        window[storageName].removeItem(key);
    } catch (_) {
    }
}

function getLocalItem(key) { return getBrowserStorageItem("localStorage", key); }
function setLocalItem(key, value) { return setBrowserStorageItem("localStorage", key, value); }
function removeLocalItem(key) { removeBrowserStorageItem("localStorage", key); }
function getSessionItem(key) { return getBrowserStorageItem("sessionStorage", key); }
function setSessionItem(key, value) { return setBrowserStorageItem("sessionStorage", key, value); }
function removeSessionItem(key) { removeBrowserStorageItem("sessionStorage", key); }

function hasSubmittedApplication() {
    return getLocalItem("hasApplication") === "true";
}

function focusWithoutScrolling(element) {
    try {
        element?.focus({ preventScroll: true });
    } catch (_) {
        element?.focus();
    }
}

function getAthleteDisplayName(athlete) {
    if (athlete && typeof athlete.DisplayName === "string" && athlete.DisplayName.trim()) {
        return athlete.DisplayName.trim();
    }

    return athlete && typeof athlete.Name === "string" ? athlete.Name : "";
}

function getAthleteCanonicalName(athlete) {
    return athlete && typeof athlete.Name === "string" ? athlete.Name.trim() : "";
}

function isAthleteInputValue(athlete, value) {
    const query = (value || "").trim().toLowerCase();
    const canonicalName = getAthleteCanonicalName(athlete);
    if (!query || !canonicalName) return false;

    return canonicalName.toLowerCase() === query
        || getAthleteDisplayName(athlete).toLowerCase() === query;
}

function getAthleteSearchText(athlete) {
    return `${getAthleteCanonicalName(athlete)} ${getAthleteDisplayName(athlete)}`.toLowerCase();
}

function getAthletePictureImageSrc(athlete) {
    return athlete && (athlete.ProfilePic || athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb)
        ? athlete.ProfilePic || athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb
        : getDefaultHeadshotJpeg();
}

function getStoredSelectedAthlete() {
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

function getSavedSelectedAthleteName() {
    const selectedAthleteName = getLocalItem("selectedAthleteName");
    return typeof selectedAthleteName === "string" ? selectedAthleteName.trim() : "";
}

function isValidSelectedAthlete(value) {
    return value
        && typeof value === "object"
        && !Array.isArray(value)
        && typeof value.Name === "string"
        && value.Name.trim();
}

function readRequiredSelectedAthlete() {
    let athlete = null;

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

function clearStaleTempAthlete(selectedAthleteName) {
    const tempJson = getSessionItem("tempAthlete");
    if (!tempJson) return;

    try {
        const tempAthlete = JSON.parse(tempJson);
        if (tempAthlete && typeof tempAthlete === "object" && tempAthlete.Name === selectedAthleteName) {
            return;
        }
    } catch (_) {
    }

    removeSessionItem("tempAthlete");
}

function persistSelectedAthlete(athlete) {
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

function createDefaultAthletePicture() {
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

function createDefaultAthleteImage() {
    const image = document.createElement("img");
    image.src = getDefaultHeadshotJpeg();
    image.alt = "Headshot";
    image.className = "illustration athlete-picture-placeholder athlete-picture-next";
    image.loading = "eager";
    image.decoding = "async";
    return image;
}

function createAthletePictureImage(altText, loading = "eager") {
    const image = document.createElement("img");
    image.alt = altText;
    image.className = "illustration athlete-picture-next";
    image.loading = loading;
    image.decoding = "async";
    return image;
}

function isDefaultHeadshotSrc(src) {
    if (!src) return false;

    try {
        return new URL(src, window.location.href).pathname.endsWith("/assets/content-images/headshot.jpg");
    } catch (_) {
        return src.endsWith(DEFAULT_HEADSHOT_JPEG_FALLBACK) || src.endsWith("/assets/content-images/headshot.jpg");
    }
}

function shouldUseDefaultForLoadedAthleteImage(image) {
    return image
        && image.naturalWidth > 0
        && image.naturalHeight > 0
        && !isDefaultHeadshotSrc(image.currentSrc || image.src)
        && (image.naturalWidth < MIN_USABLE_ATHLETE_PICTURE_SIDE || image.naturalHeight < MIN_USABLE_ATHLETE_PICTURE_SIDE);
}

function setDefaultAthleteImageSource(image) {
    if (!image || isDefaultHeadshotSrc(image.currentSrc || image.src)) {
        return false;
    }

    image.classList.add("athlete-picture-placeholder");
    image.src = getDefaultHeadshotJpeg();
    return true;
}

function watchAthleteImageLoad(image, onLoaded) {
    function cleanupImageLoadListeners() {
        image.removeEventListener("load", handleImageLoad);
        image.removeEventListener("error", handleImageError);
    }

    function handleImageLoad() {
        if (shouldUseDefaultForLoadedAthleteImage(image) && setDefaultAthleteImageSource(image)) {
            return;
        }

        cleanupImageLoadListeners();
        onLoaded();
    }

    function handleImageError() {
        if (setDefaultAthleteImageSource(image)) {
            return;
        }

        cleanupImageLoadListeners();
        onLoaded();
    }

    image.addEventListener("load", handleImageLoad);
    image.addEventListener("error", handleImageError);
    return handleImageLoad;
}

function waitForNextPaint(value) {
    return new Promise(resolve => {
        requestAnimationFrame(() => {
            requestAnimationFrame(() => resolve(value));
        });
    });
}

function waitForImageElementReady(image) {
    if (!image) return Promise.resolve();

    const loaded = image.complete
        ? Promise.resolve()
        : new Promise(resolve => {
            function done() {
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
        })
        .then(() => waitForNextPaint(image));
}

function waitForAthletePictureFrameReady(frame) {
    if (!frame) return Promise.resolve();

    const readyPromise = pictureReadyPromises.get(frame);
    if (readyPromise) return readyPromise;

    return waitForImageElementReady(frame.querySelector("img"));
}

function nextPictureTransitionToken(frame) {
    const token = (pictureTransitionTokens.get(frame) || 0) + 1;
    pictureTransitionTokens.set(frame, token);
    return token;
}

function transitionAthletePicture(frame, image, src) {
    const transitionToken = nextPictureTransitionToken(frame);
    let resolveReady;
    const readyPromise = new Promise(resolve => { resolveReady = resolve; });
    pictureReadyPromises.set(frame, readyPromise);
    let hasFinished = false;

    function finishReady() {
        waitForNextPaint(image).then(() => resolveReady(image));
    }

    function finishImageSwap() {
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

function replaceAthletePictureImmediately(frame, image, src) {
    const transitionToken = nextPictureTransitionToken(frame);
    let resolveReady;
    const readyPromise = new Promise(resolve => { resolveReady = resolve; });
    pictureReadyPromises.set(frame, readyPromise);
    let hasFinished = false;
    image.classList.remove("athlete-picture-next", "is-visible");
    function finishImageSwap() {
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

function renderAthletePicture(frame, athlete, altText) {
    const image = createAthletePictureImage(altText, "eager");
    return replaceAthletePictureImmediately(frame, image, getAthletePictureImageSrc(athlete));
}

function resetAthletePreview({ titleElement, frameElement, defaultTitle }) {
    titleElement.textContent = defaultTitle;
    transitionAthletePicture(frameElement, createDefaultAthleteImage(), getDefaultHeadshotJpeg());
}

function appendHighlightedText(container, text, query) {
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

function createAthleteSelectionController(options) {
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
    let athletes = [];
    let currentFocus = -1;
    let athleteAutocompleteReady = false;
    let athleteLoadPromise = null;
    let isBound = false;
    let hasUserEditedInput = false;

    function closeAllLists() {
        document.querySelectorAll(".autocomplete-items")
            .forEach(list => list.remove());
        currentFocus = -1;
    }

    function addActive(items) {
        if (!items) return;
        removeActive(items);
        if (currentFocus >= items.length) currentFocus = 0;
        if (currentFocus < 0) currentFocus = items.length - 1;
        items[currentFocus].classList.add("autocomplete-active");
        items[currentFocus].scrollIntoView({ block: "nearest" });
    }

    function removeActive(items) {
        Array.from(items).forEach(item => item.classList.remove("autocomplete-active"));
    }

    function clearCurrentAthleteSelectionIfInputChanged(value) {
        if (!currentAthlete || isAthleteInputValue(currentAthlete, value)) return;

        currentAthlete = null;
        confirmButton.disabled = true;
        resetAthletePreview({ titleElement, frameElement, defaultTitle });
    }

    function findExactAthleteMatch(value) {
        return athletes.find(athlete => isAthleteInputValue(athlete, value)) || null;
    }

    function renderSelectedAthletePreview(athlete, selectionOptions = {}) {
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

    function selectAthlete(athlete, selectionOptions = {}) {
        renderSelectedAthletePreview(athlete, selectionOptions.transition === undefined ? { transition: true } : selectionOptions);
        if (typeof options.onAthleteSelected === "function") {
            options.onAthleteSelected(athlete, api);
        }
    }

    function hydrateStoredAthleteSelection() {
        const storedAthlete = currentAthlete || getStoredSelectedAthlete();
        if (!storedAthlete || !storedAthlete.Name) return false;

        renderSelectedAthletePreview(storedAthlete, { transition: false });
        return true;
    }

    function renderAthleteMatches() {
        const query = input.value.trim().toLowerCase();
        const terms = query.split(/\s+/).filter(term => term);
        clearCurrentAthleteSelectionIfInputChanged(input.value);

        closeAllLists();
        if (currentAthlete && isAthleteInputValue(currentAthlete, input.value)) return false;
        if (!terms.length) return false;

        const list = document.createElement("div");
        list.setAttribute("id", `${input.id}-autocomplete-list`);
        list.setAttribute("class", "autocomplete-items");
        input.parentNode.appendChild(list);

        let count = 0;
        athletes.forEach(athlete => {
            if (count >= maxItems) return;
            if (!getAthleteCanonicalName(athlete)) return;
            const searchText = getAthleteSearchText(athlete);
            if (terms.every(term => searchText.includes(term))) {
                const first = terms[0];
                const displayName = getAthleteDisplayName(athlete);
                const item = document.createElement("div");
                appendHighlightedText(item, displayName, first);
                item.dataset.value = athlete.Name;
                item.dataset.profilePic = athlete.ProfilePic;

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

        return count > 0;
    }

    function loadAthletes(loadOptions = {}) {
        if (athleteAutocompleteReady) return Promise.resolve(athletes);
        if (athleteLoadPromise) return athleteLoadPromise;
        errorElement.textContent = "";
        athleteLoadPromise = fetch(athleteApiPath)
            .then(response => response.ok ? response.json() : Promise.reject(new Error("Athlete list request failed")))
            .then(data => {
                if (!Array.isArray(data)) {
                    throw new Error("Athlete list response was invalid");
                }

                athletes = data;
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
            .catch(error => {
                console.error("Error fetching athletes:", error);
                errorElement.textContent = "Athlete list could not load. Check your connection and try again.";
                throw error;
            })
            .finally(() => {
                athleteLoadPromise = null;
            });

        return athleteLoadPromise;
    }

    function retryAthleteLoad() {
        if (!athleteAutocompleteReady) {
            loadAthletes().catch(() => {});
        }
    }

    function bind() {
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
            let list = document.getElementById(`${input.id}-autocomplete-list`);
            if (list) list = list.getElementsByTagName("div");
            if (event.keyCode === 40) {
                currentFocus++;
                addActive(list);
            } else if (event.keyCode === 38) {
                currentFocus--;
                addActive(list);
            } else if (event.keyCode === 13) {
                event.preventDefault();
                if (currentFocus > -1 && list) {
                    list[currentFocus].dispatchEvent(new MouseEvent("mousedown"));
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
            }
        });

        document.addEventListener("click", event => {
            if (autocompleteRootSelector && event.target.closest(autocompleteRootSelector)) return;
            closeAllLists();
        });

        return api;
    }

    function start(startOptions = {}) {
        bind();
        if (startOptions.hydrate !== false) {
            hydrateStoredAthleteSelection();
        }
        if (startOptions.load !== false) {
            loadAthletes().catch(() => {});
        }
        return api;
    }

    function setCurrentAthlete(athlete) {
        currentAthlete = athlete;
    }

    const api = {
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

function serializePendingPaymentOffer(offer) {
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

function isUsablePaymentOffer(paymentOffer) {
    return paymentOffer
        && typeof paymentOffer === "object"
        && !Array.isArray(paymentOffer)
        && typeof paymentOffer.source === "string"
        && paymentOffer.source.trim()
        && typeof paymentOffer.offerType === "string"
        && paymentOffer.offerType.trim()
        && typeof paymentOffer.currency === "string"
        && paymentOffer.currency.trim()
        && typeof paymentOffer.amountUsd === "number"
        && Number.isFinite(paymentOffer.amountUsd)
        && paymentOffer.amountUsd >= 0;
}

function preserveAppliedDiscountMetadata(offer, result) {
    const hasDiscountCode = result
        && Array.isArray(result.components)
        && result.components.some(component => component && component.kind === "discountCode");
    if (!hasDiscountCode || !window.addActiveDiscountMetadataToPaymentOffer) return offer;

    try {
        return window.addActiveDiscountMetadataToPaymentOffer(offer);
    } catch (_) {
        return null;
    }
}

function notifyPaymentStorageFailure(retryButton) {
    const message = "Payment details could not be saved. Enable browser storage and try again.";
    const alertPromise = typeof window.customAlert === "function"
        ? window.customAlert(message)
        : Promise.resolve(window.alert(message));
    alertPromise?.then?.(() => retryButton?.focus());
}

function setPendingPaymentOffer(offer, retryButton) {
    let effectiveOffer = offer;
    try {
        effectiveOffer = window.applyPaymentAdjustmentsToPaymentOffer
            ? window.applyPaymentAdjustmentsToPaymentOffer(offer)
            : window.applyFreePassToPaymentOffer
                ? window.applyFreePassToPaymentOffer(offer)
                : offer;
    } catch (_) {
        notifyPaymentStorageFailure(retryButton);
        return false;
    }

    const serializedOffer = serializePendingPaymentOffer(effectiveOffer);
    if (serializedOffer && setSessionItem(PENDING_PAYMENT_OFFER_KEY, serializedOffer)) {
        return true;
    }

    notifyPaymentStorageFailure(retryButton);
    return false;
}

function clearPendingPaymentOffer() {
    removeSessionItem(PENDING_PAYMENT_OFFER_KEY);
}

function createPriceHtmlFallback(result) {
    const oldText = `$${result.basePriceUsd}`;
    if (result.finalPriceUsd < result.basePriceUsd) {
        return `<span class="pro-old-price">${oldText}</span> <span class="pro-new-price">${result.finalPriceText}</span>`;
    }
    return `<span class="pro-new-price">${oldText}</span>`;
}

function createDashboardButton(label, className, href, beforeNavigate) {
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

function createGoProDiscountSummary(result) {
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

function renderAthleteDashboardHeader(athlete, { titleElement, frameElement }) {
    const athleteDisplayName = getAthleteDisplayName(athlete);
    titleElement.textContent = athleteDisplayName;
    return renderAthletePicture(frameElement, athlete, `${athleteDisplayName} headshot`);
}

function refreshFlowActionDock() {
    const dock = window.LwcFlowActionDock;
    if (typeof dock?.refreshNow === "function") {
        dock.refreshNow();
        return;
    }

    dock?.refresh?.();
}

function renderDashboardActions(athlete, options) {
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
