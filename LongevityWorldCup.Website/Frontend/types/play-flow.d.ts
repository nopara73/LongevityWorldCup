type PlayBrowserStorageName = "localStorage" | "sessionStorage";

interface PlayAthlete {
    Name: string;
    DisplayName?: string;
    ProfilePic?: string;
    ProfilePicLeaderboardThumb?: string;
    ProfilePicThumb?: string;
    Biomarkers?: unknown[];
}

interface PendingPaymentOffer {
    source: string;
    offerType: string;
    currency: string;
    amountUsd: number;
}

interface AthletePictureTargets {
    titleElement: HTMLElement;
    frameElement: HTMLElement;
}

interface AthleteSelectionRenderOptions {
    transition?: boolean;
}

interface AthleteSelectionLoadOptions {
    savedSelectionTransition?: boolean;
}

interface AthleteSelectionStartOptions {
    hydrate?: boolean;
    load?: boolean;
}

interface AthleteSelectionControllerOptions extends AthletePictureTargets {
    input: HTMLInputElement;
    errorElement: HTMLElement;
    confirmButton: HTMLButtonElement;
    defaultTitle?: string;
    athleteApiPath?: string;
    maxItems?: number;
    autocompleteRootSelector?: string | null;
    initialAthlete?: PlayAthlete | null;
    focusConfirmAfterSelection?: boolean;
    onAthleteSelected?: (athlete: PlayAthlete, controller: AthleteSelectionController) => void;
}

interface AthleteSelectionController {
    bind(): AthleteSelectionController;
    start(options?: AthleteSelectionStartOptions): AthleteSelectionController;
    loadAthletes(options?: AthleteSelectionLoadOptions): Promise<PlayAthlete[]>;
    retryAthleteLoad(): void;
    renderAthleteMatches(): boolean;
    hydrateStoredAthleteSelection(): boolean;
    hasPendingSavedSelection(): boolean;
    getCurrentAthlete(): PlayAthlete | null;
    getPreviewReady(): Promise<unknown>;
    setCurrentAthlete(athlete: PlayAthlete | null): void;
    selectAthlete(athlete: PlayAthlete, options?: AthleteSelectionRenderOptions): void;
    closeAllLists(): void;
}

interface AthleteDashboardActionsOptions {
    dynamicActionsElement: HTMLElement;
    discountElement?: HTMLElement | null;
    demoPro?: boolean;
}

interface PlayAthleteFlowApi {
    getBrowserStorageItem(storageName: PlayBrowserStorageName, key: string): string | null;
    setBrowserStorageItem(storageName: PlayBrowserStorageName, key: string, value: string): boolean;
    removeBrowserStorageItem(storageName: PlayBrowserStorageName, key: string): void;
    getLocalItem(key: string): string | null;
    setLocalItem(key: string, value: string): boolean;
    removeLocalItem(key: string): void;
    getSessionItem(key: string): string | null;
    setSessionItem(key: string, value: string): boolean;
    removeSessionItem(key: string): void;
    hasSubmittedApplication(): boolean;
    focusWithoutScrolling(element: HTMLElement | null | undefined): void;
    getAthleteDisplayName(athlete: PlayAthlete | null | undefined): string;
    getAthleteCanonicalName(athlete: PlayAthlete | null | undefined): string;
    isAthleteInputValue(athlete: PlayAthlete | null | undefined, value: string | null | undefined): boolean;
    getAthleteSearchText(athlete: PlayAthlete): string;
    getAthletePictureImageSrc(athlete: PlayAthlete | null | undefined): string;
    getStoredSelectedAthlete(): PlayAthlete | null;
    getSavedSelectedAthleteName(): string;
    isValidSelectedAthlete(value: unknown): value is PlayAthlete;
    readRequiredSelectedAthlete(): PlayAthlete | null;
    clearStaleTempAthlete(selectedAthleteName: string): void;
    persistSelectedAthlete(athlete: PlayAthlete | null): boolean;
    createDefaultAthletePicture(): HTMLPictureElement;
    createDefaultAthleteImage(): HTMLImageElement;
    createAthletePictureImage(altText: string, loading?: "eager" | "lazy"): HTMLImageElement;
    transitionAthletePicture(frame: HTMLElement, image: HTMLImageElement, src: string): Promise<HTMLImageElement | void>;
    replaceAthletePictureImmediately(frame: HTMLElement, image: HTMLImageElement, src: string): Promise<HTMLImageElement | void>;
    waitForAthletePictureFrameReady(frame: HTMLElement | null): Promise<unknown>;
    renderAthletePicture(frame: HTMLElement, athlete: PlayAthlete, altText: string): Promise<HTMLImageElement | void>;
    createAthleteSelectionController(options: AthleteSelectionControllerOptions): AthleteSelectionController;
    serializePendingPaymentOffer(offer: unknown): string | null;
    isUsablePaymentOffer(value: unknown): value is PendingPaymentOffer;
    preserveAppliedDiscountMetadata(
        offer: PendingPaymentOffer,
        result: DiscountBreakdown | null
    ): PendingPaymentOffer | null;
    createPriceHtmlFallback(result: DiscountBreakdown): string;
    setPendingPaymentOffer(offer: unknown, retryButton?: HTMLButtonElement | null): boolean;
    clearPendingPaymentOffer(): void;
    renderAthleteDashboardHeader(athlete: PlayAthlete, targets: AthletePictureTargets): Promise<HTMLImageElement | void>;
    renderDashboardActions(athlete: PlayAthlete, options: AthleteDashboardActionsOptions): Promise<void>;
}

interface LwcFlowActionDockApi {
    refreshNow?: (() => void) | undefined;
    ensureClear?: ((element: Element | null | undefined, options?: {
        followingVisibleSiblingCount?: number;
        includeNextVisibleSibling?: boolean;
        margin?: number;
        behavior?: ScrollBehavior;
    }) => void) | undefined;
}

interface Window {
    modulesReady?: Promise<unknown[]>;
    playAthleteFlow: PlayAthleteFlowApi;
    addActiveDiscountMetadataToPaymentOffer?: (offer: PendingPaymentOffer) => unknown;
    applyPaymentAdjustmentsToPaymentOffer?: (offer: unknown) => unknown;
    applyFreePassToPaymentOffer?: (offer: unknown) => unknown;
    hasFreePass?: () => boolean;
    getFreePassValue?: () => string | null;
    getDiscountValue?: () => string | null;
    captureFreePassFromUrl?: () => void;
    captureDiscountFromUrl?: () => void;
}
