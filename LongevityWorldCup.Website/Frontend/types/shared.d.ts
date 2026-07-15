type MentionResolver = (slug: string) => unknown;

interface PhenoBiomarker {
    readonly id: string;
    readonly name: string;
    readonly coeff: number;
    readonly cap?: number;
}

interface PhenoAgeApi {
    readonly biomarkers: readonly PhenoBiomarker[];
    parseInput(value: unknown): number;
    calculateLiverScore(markerValues: readonly number[]): number;
    calculateKidneyScore(markerValues: readonly number[]): number;
    calculateMetabolicScore(markerValues: readonly number[]): number;
    calculateInflammationScore(markerValues: readonly number[]): number;
    calculateImmuneScore(markerValues: readonly number[]): number;
    calculatePhenoAge(markerValues: readonly number[]): number;
    calculateLiverPhenoAgeContributor(markerValues: readonly number[]): number;
    calculateKidneyPhenoAgeContributor(markerValues: readonly number[]): number;
    calculateMetabolicPhenoAgeContributor(markerValues: readonly number[]): number;
    calculateInflammationPhenoAgeContributor(markerValues: readonly number[]): number;
    calculateImmunePhenoAgeContributor(markerValues: readonly number[]): number;
}

type BortzCapMode = "floor" | "ceiling";

interface BortzFeature {
    readonly id: string;
    readonly name: string;
    readonly mean: number;
    readonly baaCoeff: number;
    readonly isLog: boolean;
    readonly cap?: number;
    readonly capMode?: BortzCapMode;
}

interface BortzAgeApi {
    readonly features: readonly BortzFeature[];
    readonly biomarkers: readonly BortzFeature[];
    parseInput(value: unknown): number;
    calculateFeatureContribution(rawValue: number, feature: BortzFeature): number;
    calculateBAA(values: readonly number[] | null | undefined): number;
    calculateBortzAgeFromBAA(chronologicalAgeYears: number, baa: number): number;
    calculateBortzAge(rawValuesInFeatureOrder: readonly number[] | null | undefined): number;
    calculateBortzAge(chronologicalAgeYears: number, rawValuesInFeatureOrder: readonly number[]): number;
}

interface FlagAthlete {
    readonly Flag?: unknown;
    readonly flag?: unknown;
    readonly canonicalFlag?: unknown;
}

interface FlagOption {
    readonly key: string;
    readonly name: string;
    count: number;
}

interface LwcFlagsApi {
    buildFlagOptions(flags: unknown, athletes: unknown): FlagOption[];
    countFlagUsage(
        athletes: unknown,
        flagAccessor?: (athlete: FlagAthlete) => unknown
    ): FlagOption[];
    getCanonicalFlagName(flag: unknown): string;
    getFlagFilterKey(flag: unknown): string;
    getFlagHref(flag: unknown): string;
    getFlagIconCode(flag: unknown): string;
    getFlagRouteSlug(flag: unknown): string;
    matchesFlagOption(option: FlagOption, query: unknown): boolean;
    normalizeFlagKey(flag: unknown): string;
    renderFlagIcon(flag: unknown, className?: string): string;
    renderFlagLabel(flag: unknown): string;
    renderFlagOptionLabel(flag: unknown, query?: unknown): string;
}

interface CustomEventMarkupOptions {
    mentionResolver?: MentionResolver | undefined;
    mentionRenderer?: ((slug: string, displayText: string) => unknown) | undefined;
    mentionHrefResolver?: ((slug: string) => unknown) | undefined;
    linkTarget?: string | undefined;
    linkRel?: string | undefined;
    keepHyperlinkLabels?: boolean | undefined;
}

interface CustomEventMarkupApi {
    normalizeNewlines(value: unknown): string;
    splitText(text: unknown): { title: string; content: string };
    containsHyperlink(text: unknown): boolean;
    getSingleHyperlink(text: unknown): string;
    renderMarkup(text: unknown, options?: CustomEventMarkupOptions): string;
    renderMarkupWithBreaks(text: unknown, options?: CustomEventMarkupOptions): string;
    renderWebpageMarkup(text: unknown, options?: CustomEventMarkupOptions): string;
    renderWebpageMarkupWithBreaks(text: unknown, options?: CustomEventMarkupOptions): string;
    renderImageMarkup(text: unknown, options?: CustomEventMarkupOptions): string;
    renderImageMarkupWithBreaks(text: unknown, options?: CustomEventMarkupOptions): string;
    toPlainText(text: unknown, options?: CustomEventMarkupOptions): string;
    setMentionResolver(resolver: MentionResolver | null | undefined): void;
}

interface RankedAthlete {
    readonly name: string;
    readonly dateOfBirth: string;
    readonly ageReduction?: number | null;
    readonly bortzAgeReduction?: number | null;
    readonly phenoAgeImprovement?: number | null;
    readonly bortzAgeImprovement?: number | null;
    readonly crowdAgeReduction?: number | null;
    readonly crowdCount?: number | null;
}

interface ImageOptimizationOptions {
    readonly contentType?: string;
    readonly quality?: number;
    readonly maxSize?: number;
    readonly targetMaxBytes?: number;
    readonly minQuality?: number;
    readonly minResizeQuality?: number;
    readonly minMaxSize?: number;
}

interface OptimizedImageResult {
    readonly dataUrl: string | null;
    readonly contentType: string | null;
    readonly extension: string | null;
}

interface ApplicationSubmissionReport {
    readonly submissionId: string;
    readonly phase: string;
    readonly pagePath: string;
    readonly submissionKind: string;
    readonly proofCount: number;
    readonly proofDataUrlLengths: number[];
    readonly profilePicDataUrlLength: number | null;
    readonly jsonBodyLength: number | null;
    readonly errorType: unknown;
    readonly errorMessage: string | null;
}

interface HypotheticalRankOptions {
    readonly containerId: string;
    readonly calculator: string;
    readonly chronologicalAge: number;
    readonly biologicalAge: number;
    readonly birthYear: number;
    readonly birthMonth: number;
    readonly birthDay: number;
}

interface HypotheticalRankNearbyItem {
    readonly rank?: number;
    readonly name?: string;
    readonly category?: string;
    readonly ageDifference?: number;
    readonly isHypothetical?: boolean;
}

interface HypotheticalRankResult {
    readonly rank: number;
    readonly fieldSize: number;
    readonly currentFieldSize?: number;
    readonly category?: string;
    readonly leagueName?: string;
    readonly nearby?: readonly HypotheticalRankNearbyItem[];
}

interface LwcSiteStatisticsMetadata {
    athleteSlug?: string;
    league?: string;
    targetKind?: string;
    eventType?: string;
    highlightSelection?: string;
    primarySlug?: string;
    progressBucket?: string;
    completionSource?: string;
    ageReductionBucket?: string;
    fileCountBucket?: string;
    fileTypeBucket?: string;
    fileSizeBucket?: string;
    sourceControl?: string;
    track?: string;
    identityMode?: string;
    pledgeBucket?: string;
    checkinKind?: string;
    commitmentState?: string;
    clock?: "bortz" | "pheno";
    stageNumber?: string;
}

interface LwcSiteStatisticsTrackOptions {
    flow?: string;
    component?: string;
    step?: string;
    outcome?: string;
    errorCode?: string | null;
    durationMs?: number;
    metadata?: LwcSiteStatisticsMetadata;
}

interface LwcSiteStatisticsApi {
    track(eventName: string | null, options?: LwcSiteStatisticsTrackOptions): void;
    amountBucket(raw: unknown): string;
    fileSizeBucket(file: File | null | undefined): string;
    fileTypeBucket(file: File | null | undefined): string;
    countBucket(count: number): string;
}

interface LwcFlowActionDockApi {
    refresh?: (() => void) | undefined;
}

interface AgeVisualizationAthlete {
    readonly bestBiomarkerValues?: readonly number[];
    readonly bestBortzValues?: readonly number[];
    readonly chronoAtLowestPhenoAge?: number;
    readonly chronoAtLowestBortzAge?: number;
    readonly lowestPhenoAge?: number;
    readonly lowestBortzAge?: number;
}

interface RadarData {
    readonly labels: string[];
    readonly values: number[];
    readonly tooltipContributors: string[][];
}

interface RadarChartInstance {
    data: {
        labels: string[];
        datasets: Array<{ data: number[] }>;
    };
    _radarTooltipContributors?: string[][];
    resize(): void;
    destroy(): void;
    update(mode?: string): void;
}

interface RadarChartConstructor {
    new (context: CanvasRenderingContext2D, configuration: unknown): RadarChartInstance;
}

interface ServerBadge {
    readonly BadgeLabel?: unknown;
    readonly Label?: unknown;
    readonly LeagueCategory?: unknown;
    readonly Category?: unknown;
    readonly LeagueValue?: unknown;
    readonly Value?: unknown;
    readonly Place?: unknown;
    readonly order?: unknown;
}

interface BadgeAthlete extends AgeVisualizationAthlete {
    readonly Badges?: readonly ServerBadge[];
    readonly badges?: readonly ServerBadge[];
    readonly PersonalLink?: unknown;
    readonly personalLink?: unknown;
    readonly Name?: unknown;
    readonly name?: unknown;
    readonly displayName?: unknown;
    readonly AthleteSlug?: unknown;
    readonly athleteSlug?: unknown;
    readonly Slug?: unknown;
    readonly slug?: unknown;
    readonly DisplayName?: unknown;
    readonly podcastLink?: unknown;
    readonly PodcastLink?: unknown;
    readonly BestBortzValues?: readonly number[];
    readonly BestMarkerValues?: readonly number[];
    readonly ChronoAge?: unknown;
    readonly chronologicalAge?: unknown;
    readonly chronological_age?: unknown;
    readonly crowdAge?: unknown;
    readonly CrowdAge?: unknown;
    readonly CrowdCount?: unknown;
    readonly LowestBortzAge?: unknown;
    readonly LowestPhenoAge?: unknown;
    readonly lowestPhenoAge?: number;
    readonly SubmissionCount?: unknown;
    readonly submissionCount?: unknown;
    readonly crowdCount?: unknown;
    readonly bortzAgeDifference?: unknown;
    readonly BortzAgeDiffFromBaseline?: unknown;
    readonly phenoAgeDifference?: unknown;
    readonly PhenoAgeDiffFromBaseline?: unknown;
}

type DiscountComponentKind =
    | "leaderboard"
    | "discountCode"
    | "personalLink"
    | "perfectGuess"
    | "serverBadge";

interface DiscountComponent {
    readonly label: string;
    readonly percent: number;
    readonly isBadge: boolean;
    readonly kind: DiscountComponentKind;
    readonly code?: string;
    readonly badge?: ServerBadge;
    readonly athlete?: BadgeAthlete | null | undefined;
}

interface DiscountBreakdown {
    readonly basePriceUsd: number;
    readonly components: DiscountComponent[];
    readonly rawDiscount: number;
    readonly totalDiscount: number;
    readonly finalPriceUsd: number;
    readonly finalPriceText: string;
}

interface ProDiscountsApi {
    readonly PERFECT_GUESS_KEY: string;
    setPerfectGuessMarker(): void;
    weightForBadge(badge: ServerBadge): number;
    buildDiscountBreakdown(
        athlete: BadgeAthlete | null | undefined,
        options?: { readonly isOnLeaderboard?: boolean }
    ): DiscountBreakdown;
    createBreakdownText(result: DiscountBreakdown): string;
    createBreakdownHtml(result: DiscountBreakdown): string;
    createPriceHtml(result: DiscountBreakdown): string;
}

interface HTMLElement {
    __hypotheticalRankRequestId?: number;
}

interface Window {
    PhenoAge?: PhenoAgeApi;
    BortzAge?: BortzAgeApi;
    TryGetDivisionIcon?: ((division: string) => string) | undefined;
    TryGetLeagueTrackIcon?: ((leagueTrack: string | null | undefined) => string) | undefined;
    TryGetGenerationIcon?: ((generation: string) => string) | undefined;
    TryGetDivisionFaIcon?: ((division: string) => string) | undefined;
    TryGetGenerationFaIcon?: ((generation: string) => string) | undefined;
    LwcFlags?: LwcFlagsApi;
    CustomEventMarkup?: CustomEventMarkupApi;
    __lwcPlayFlowScrollInitialized?: boolean;
    LwcFlowActionDock?: LwcFlowActionDockApi;
    getIcon(link: string): string;
    slugifyName(name: string, encode?: boolean): string;
    normalizeString?: ((value: string) => string) | undefined;
    escapeHTML?: ((value: string | null) => string) | undefined;
    compareAthleteRank(a: RankedAthlete, b: RankedAthlete): number;
    compareAthleteRankPhenoOnly(a: RankedAthlete, b: RankedAthlete): number;
    compareAthleteRankPhenoImprovement(a: RankedAthlete, b: RankedAthlete): number;
    compareAthleteRankBortzImprovement(a: RankedAthlete, b: RankedAthlete): number;
    compareAthleteRankCrowdAge(a: RankedAthlete, b: RankedAthlete): number;
    getGeneration(birthYear: number): string;
    getRankText(rank: number): string;
    showWithDelay(element: HTMLElement): void;
    calculateAgeAtDate(birthDate: Date, atDate: Date): number;
    calculateCompletedYearsAtDate(birthDate: Date, atDate: Date): number;
    removeAllHighlights(): void;
    highlightText(element: HTMLElement, searchTerms: readonly string[]): void;
    goBackOrHome(): void;
    navigateToFlowDestination(destination: unknown): void;
    optimizeImageClient(
        dataUri: string,
        options?: ImageOptimizationOptions | null
    ): Promise<OptimizedImageResult>;
    PROFILE_IMAGE_OPTIMIZATION_OPTIONS: ImageOptimizationOptions;
    __pendingApplicationSubmissionId?: string;
    createApplicationSubmissionId(): string;
    APPLICATION_SUBMISSION_TIMEOUT_MS: number;
    APPLICATION_SUBMISSION_REPORT_TIMEOUT_MS: number;
    readApplicationErrorMessage(response: Response | null | undefined): Promise<string>;
    extractApplicationErrorMessage(text: unknown, fallback?: string): string;
    buildApplicationSubmissionReport(
        applicantData: unknown,
        submissionId: string,
        phase: string,
        submissionKind: string,
        error: unknown
    ): ApplicationSubmissionReport;
    sendApplicationSubmissionReport(report: ApplicationSubmissionReport): Promise<void>;
    trySendApplicationSubmissionReport(
        applicantData: unknown,
        submissionId: string,
        phase: string,
        submissionKind: string,
        error: unknown
    ): void;
    __hypotheticalRankRequestSequence?: number;
    LwcSiteStats?: LwcSiteStatisticsApi;
    updateHypotheticalRankResult(options: HypotheticalRankOptions): Promise<void>;
    renderHypotheticalRankResult(container: HTMLElement, result: unknown): void;
    Chart?: RadarChartConstructor;
    generateAgeVisualization?(
        bioAge: number,
        chronoAge: number,
        athleteData: AgeVisualizationAthlete | null | undefined,
        athleteResults: readonly AgeVisualizationAthlete[] | null | undefined
    ): void;
    generateAgeVisualizationInternal?(
        visualizationContainer: HTMLElement,
        bioAge: number,
        chronoAge: number
    ): void;
    destroyAgeRadarChart?(): void;
    getBestDomainBiomarkerTooltip?(
        badgeLabel: string,
        bestBortzValues: readonly number[] | null | undefined
    ): string | null;
    getActiveProDiscount?: (() => unknown) | undefined;
    proDiscounts: ProDiscountsApi;
    pickIconForServerBadge?(badge: ServerBadge): string;
    pickBackgroundForServerBadge?(badge: ServerBadge): string;
    makeTooltipFromServerBadge?(
        badge: ServerBadge,
        athlete?: BadgeAthlete | null,
        options?: unknown
    ): string;
    pickClickUrl?(badge: ServerBadge, athlete?: BadgeAthlete | null): string | null;
    getBadgeFamilyClass?(badge: ServerBadge): string;
    styleWithBadgeVars?(style: string): string;
    setBadges(athlete: BadgeAthlete, athleteCell: HTMLElement | null | undefined): void;
    computeBadges: (...args: unknown[]) => unknown;
    openAthleteModalBySlug?(
        slug: string,
        options: { readonly suppressGuessMyAge: boolean }
    ): boolean;
}
