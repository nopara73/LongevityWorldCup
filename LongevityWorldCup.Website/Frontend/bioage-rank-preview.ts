type BioageRankClock = 'bortz' | 'pheno';

interface BioageRankPreviewOptions {
    clock?: BioageRankClock;
    ageReduction: unknown;
    dateOfBirth: Date;
}

interface BioageRankPreviewApi {
    render: (targetId: string, options: BioageRankPreviewOptions | null | undefined) => Promise<void>;
    clear: (targetId: string) => void;
}

interface RankPreviewTrackOptions {
    component: string;
    step: string;
    outcome: string;
    metadata: { clock: BioageRankClock };
}

declare global {
    interface Window {
        LwcBioAgeRankPreview: BioageRankPreviewApi;
    }
}

(function () {
    interface AthleteDateOfBirth {
        Year: number;
        Month: number;
        Day: number;
    }

    interface AthleteSource {
        AthleteSlug?: string;
        Name?: string;
        DisplayName?: string;
        ProfilePicThumb?: string;
        ProfilePicLeaderboardThumb?: string;
        ProfilePic?: string;
        DateOfBirth: AthleteDateOfBirth;
        Biomarkers: unknown[];
    }

    interface PhenoBiomarkerSet {
        Date: string;
        Wbc1000cellsuL: number;
        LymPc: number;
        McvFL: number;
        RdwPc: number;
        AlbGL: number;
        AlpUL: number;
        CreatUmolL: number;
        GluMmolL: number;
        CrpMgL: number;
    }

    interface BortzBiomarkerSet extends PhenoBiomarkerSet {
        UreaMmolL: number;
        CholesterolMmolL: number;
        CystatinCMgL: number;
        Hba1cMmolMol: number;
        GgtUL: number;
        Rbc10e12L: number;
        MonocytePc: number;
        NeutrophilPc: number;
        AltUL: number;
        ShbgNmolL: number;
        VitaminDNmolL: number;
        MchPg: number;
        ApoA1GL: number;
    }

    interface RankRow {
        slug?: string;
        name: string;
        displayName: string;
        profilePicThumb?: string;
        dateOfBirth: Date;
        ageReduction: number | null;
        bortzAgeReduction: number | null;
        isYou?: boolean;
    }

    interface AgeSummary {
        ageReduction: number;
    }

    interface BiologicalAgeClock {
        calculatePhenoAge?: (values: number[]) => number;
        calculateBortzAge?: (chronologicalAge: number, values: number[]) => number;
    }

    function isObject(value: unknown): value is object {
        return typeof value === 'object' && value !== null && !Array.isArray(value);
    }

    function getOptionalString(value: object, property: string): string | undefined {
        const candidate = Reflect.get(value, property);
        return typeof candidate === 'string' ? candidate : undefined;
    }

    function isAthleteDateOfBirth(value: unknown): value is AthleteDateOfBirth {
        if (!isObject(value)) return false;
        return ['Year', 'Month', 'Day'].every(function (property) {
            return typeof Reflect.get(value, property) === 'number';
        });
    }

    function toAthleteSource(value: unknown): AthleteSource | null {
        if (!isObject(value)) return null;
        const dateOfBirth = Reflect.get(value, 'DateOfBirth');
        const biomarkers = Reflect.get(value, 'Biomarkers');
        if (!isAthleteDateOfBirth(dateOfBirth) || !Array.isArray(biomarkers)) return null;

        const athlete: AthleteSource = { DateOfBirth: dateOfBirth, Biomarkers: biomarkers };
        const optionalProperties = [
            'AthleteSlug',
            'Name',
            'DisplayName',
            'ProfilePicThumb',
            'ProfilePicLeaderboardThumb',
            'ProfilePic'
        ] as const;
        optionalProperties.forEach(function (property) {
            const candidate = getOptionalString(value, property);
            if (candidate !== undefined) athlete[property] = candidate;
        });
        return athlete;
    }

    function hasFiniteProperties(value: object, properties: readonly string[]): boolean {
        return properties.every(function (property) {
            return Number.isFinite(Reflect.get(value, property));
        });
    }

    'use strict';

    var athletePlaceholderImage = '/assets/content-images/headshot.webp';
    var sharedAthletesPromise: Promise<unknown[]> | null = null;
    var targetRenderTokens = new Map<string, number>();

    function advanceTargetRenderToken(targetId: string): number {
        var nextToken = (targetRenderTokens.get(targetId) || 0) + 1;
        targetRenderTokens.set(targetId, nextToken);
        return nextToken;
    }

    function isCurrentTargetRenderToken(targetId: string, token: number): boolean {
        return targetRenderTokens.get(targetId) === token;
    }

    function getSharedAthletes(): Promise<unknown[]> {
        var sharedLoader = Reflect.get(window, 'getSharedAthletes');
        if (typeof sharedLoader === 'function') {
            const loaded: unknown = Reflect.apply(sharedLoader, window, []);
            return Promise.resolve(loaded).then(function (payload: unknown) {
                if (!Array.isArray(payload)) throw new TypeError('Athlete response was not an array.');
                return payload;
            });
        }
        if (!sharedAthletesPromise) {
            sharedAthletesPromise = fetch('/api/data/athletes', {
                cache: 'no-store',
                headers: { accept: 'application/json' }
            })
                .then(function (response) {
                    return response.ok ? response.json() : Promise.reject(response.status);
                })
                .then(function (payload: unknown) {
                    if (!Array.isArray(payload)) throw new TypeError('Athlete response was not an array.');
                    return payload;
                })
                .catch(function (error) {
                    sharedAthletesPromise = null;
                    throw error;
                });
        }
        return sharedAthletesPromise;
    }

    function isCompletePhenoBiomarkerSet(set: unknown): set is PhenoBiomarkerSet {
        if (!isObject(set) || !Reflect.get(set, 'Date')) return false;
        var properties = [
            'Wbc1000cellsuL', 'LymPc', 'McvFL', 'RdwPc', 'AlbGL',
            'AlpUL', 'CreatUmolL', 'GluMmolL', 'CrpMgL'
        ];
        return hasFiniteProperties(set, properties) && Number(Reflect.get(set, 'CrpMgL')) > 0;
    }

    function isCompleteBortzBiomarkerSet(set: unknown): set is BortzBiomarkerSet {
        if (!isCompletePhenoBiomarkerSet(set)) return false;
        var properties = [
            'UreaMmolL', 'CholesterolMmolL', 'CystatinCMgL', 'Hba1cMmolMol',
            'GgtUL', 'Rbc10e12L', 'MonocytePc', 'NeutrophilPc', 'AltUL',
            'ShbgNmolL', 'VitaminDNmolL', 'MchPg', 'ApoA1GL'
        ];
        return hasFiniteProperties(set, properties);
    }

    function calculateAgeAtDate(dob: Date, date: Date): number {
        var sharedCalculator = Reflect.get(window, 'calculateAgeAtDate');
        if (typeof sharedCalculator === 'function') {
            return (sharedCalculator as (dob: Date, date: Date) => number).call(window, dob, date);
        }

        var age = date.getFullYear() - dob.getFullYear();
        var monthDiff = date.getMonth() - dob.getMonth();
        if (monthDiff < 0 || (monthDiff === 0 && date.getDate() < dob.getDate())) {
            age--;
        }
        return age;
    }

    function getDisplayName(athlete: AthleteSource): string {
        return athlete && typeof athlete.DisplayName === 'string' && athlete.DisplayName.trim()
            ? athlete.DisplayName.trim()
            : athlete && typeof athlete.Name === 'string' ? athlete.Name : '';
    }

    function mapAthlete(value: unknown): RankRow | null {
        var athlete = toAthleteSource(value);
        if (!athlete) return null;
        var dob = new Date(athlete.DateOfBirth.Year, athlete.DateOfBirth.Month - 1, athlete.DateOfBirth.Day);
        var displayName = getDisplayName(athlete);
        var phenoSummary = getPhenoSummary(athlete, dob);
        var bortzSummary = getBortzSummary(athlete, dob);

        if (!phenoSummary && !bortzSummary) return null;

        return {
            slug: athlete.AthleteSlug || '',
            name: athlete.Name || displayName,
            displayName: displayName,
            profilePicThumb: athlete.ProfilePicThumb || athlete.ProfilePicLeaderboardThumb || athlete.ProfilePic || '',
            dateOfBirth: dob,
            ageReduction: phenoSummary ? phenoSummary.ageReduction : null,
            bortzAgeReduction: bortzSummary ? bortzSummary.ageReduction : null
        };
    }

    function getPhenoSummary(athlete: AthleteSource, dob: Date): AgeSummary | null {
        var clock = Reflect.get(window, 'PhenoAge') as BiologicalAgeClock | undefined;
        const calculatePhenoAge = clock?.calculatePhenoAge;
        if (typeof calculatePhenoAge !== 'function') return null;

        var bestAge = Infinity;
        var bestChrono: number | null = null;
        athlete.Biomarkers.forEach(function (entry: unknown) {
            if (!isCompletePhenoBiomarkerSet(entry)) return;

            var ageAtEntry = calculateAgeAtDate(dob, new Date(entry.Date));
            var values = [
                ageAtEntry,
                entry.AlbGL,
                entry.CreatUmolL,
                entry.GluMmolL,
                Math.log(entry.CrpMgL / 10),
                entry.Wbc1000cellsuL,
                entry.LymPc,
                entry.McvFL,
                entry.RdwPc,
                entry.AlpUL
            ];
            if (!values.every(Number.isFinite)) return;

            var phenoAge = calculatePhenoAge.call(clock, values);
            if (Number.isFinite(phenoAge) && phenoAge < bestAge) {
                bestAge = phenoAge;
                bestChrono = ageAtEntry;
            }
        });

        return Number.isFinite(bestAge) && typeof bestChrono === 'number' && Number.isFinite(bestChrono)
            ? { ageReduction: bestAge - bestChrono }
            : null;
    }

    function getBortzSummary(athlete: AthleteSource, dob: Date): AgeSummary | null {
        var clock = Reflect.get(window, 'BortzAge') as BiologicalAgeClock | undefined;
        const calculateBortzAge = clock?.calculateBortzAge;
        if (typeof calculateBortzAge !== 'function') return null;

        var bestAge = Infinity;
        var bestChrono: number | null = null;
        athlete.Biomarkers.forEach(function (entry: unknown) {
            if (!isCompleteBortzBiomarkerSet(entry)) return;

            var ageAtEntry = calculateAgeAtDate(dob, new Date(entry.Date));
            var wbc = entry.Wbc1000cellsuL;
            var values = [
                ageAtEntry,
                entry.AlbGL,
                entry.AlpUL,
                entry.UreaMmolL,
                entry.CholesterolMmolL,
                entry.CreatUmolL,
                entry.CystatinCMgL,
                entry.Hba1cMmolMol,
                entry.CrpMgL,
                entry.GgtUL,
                entry.Rbc10e12L,
                entry.McvFL,
                entry.RdwPc,
                wbc * entry.MonocytePc / 100,
                wbc * entry.NeutrophilPc / 100,
                entry.LymPc,
                entry.AltUL,
                entry.ShbgNmolL,
                entry.VitaminDNmolL,
                entry.GluMmolL,
                entry.MchPg,
                entry.ApoA1GL
            ];
            if (!values.every(Number.isFinite)) return;

            var bortzAge = calculateBortzAge.call(clock, ageAtEntry, values);
            if (Number.isFinite(bortzAge) && bortzAge < bestAge) {
                bestAge = bortzAge;
                bestChrono = ageAtEntry;
            }
        });

        return Number.isFinite(bestAge) && typeof bestChrono === 'number' && Number.isFinite(bestChrono)
            ? { ageReduction: bestAge - bestChrono }
            : null;
    }

    function compareByClock(clock: BioageRankClock): (a: RankRow, b: RankRow) => number {
        return function (a: RankRow, b: RankRow): number {
            var aReduction = getReduction(a, clock);
            var bReduction = getReduction(b, clock);

            if (aReduction === null) return bReduction === null ? 0 : 1;
            if (bReduction === null) return -1;

            if (aReduction < bReduction) return -1;
            if (aReduction > bReduction) return 1;
            if (a.dateOfBirth < b.dateOfBirth) return -1;
            if (a.dateOfBirth > b.dateOfBirth) return 1;
            if (a.name < b.name) return -1;
            if (a.name > b.name) return 1;
            return 0;
        };
    }

    function formatReduction(value: number | null, precision: number): string {
        if (typeof value !== 'number' || !Number.isFinite(value)) return '';
        return (value > 0 ? '+' : '') + value.toFixed(precision) + 'y';
    }

    function getReduction(row: RankRow, clock: BioageRankClock): number | null {
        return clock === 'bortz' ? row.bortzAgeReduction : row.ageReduction;
    }

    function chooseReductionPrecision(rows: RankRow[], clock: BioageRankClock): number {
        var values = rows
            .map(function (row) { return getReduction(row, clock); })
            .filter(function (value): value is number { return Number.isFinite(value); });
        var maxPrecision = 6;

        for (var precision = 1; precision <= maxPrecision; precision++) {
            var buckets = new Map<string, number[]>();
            var hasHiddenDifference = values.some(function (value: number) {
                var key = value.toFixed(precision);
                var bucket = buckets.get(key) || [];
                if (!buckets.has(key)) buckets.set(key, bucket);
                var differs = bucket.some(function (existing: number) {
                    return Math.abs(existing - value) > 1e-9;
                });
                bucket.push(value);
                return differs;
            });

            if (!hasHiddenDifference) return precision;
        }

        return maxPrecision;
    }

    function escapeHtml(text: unknown): string {
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function ordinal(value: number): string {
        var n = Math.abs(Math.round(value));
        var mod100 = n % 100;
        if (mod100 >= 11 && mod100 <= 13) return n + 'th';
        switch (n % 10) {
            case 1: return n + 'st';
            case 2: return n + 'nd';
            case 3: return n + 'rd';
            default: return n + 'th';
        }
    }

    function initialsFromName(name: string): string {
        return String(name || '')
            .trim()
            .split(/\s+/)
            .filter(Boolean)
            .slice(0, 2)
            .map(function (part: string) { return part.charAt(0).toUpperCase(); })
            .join('') || '?';
    }

    function buildAvatar(row: RankRow): string {
        if (row.isYou) {
            return '<span class="bioage-rank-row-avatar bioage-rank-row-avatar-placeholder" aria-hidden="true">' +
                '<img src="' + athletePlaceholderImage + '" alt="" loading="lazy" decoding="async">' +
                '</span>';
        }

        var name = row.displayName || row.name || '';
        var image = row.profilePicThumb || '';
        if (image) {
            return '<span class="bioage-rank-row-avatar" aria-hidden="true">' +
                '<img src="' + escapeHtml(image) + '" alt="" loading="lazy" decoding="async" referrerpolicy="no-referrer">' +
                '</span>';
        }

        return '<span class="bioage-rank-row-avatar bioage-rank-row-avatar-fallback" aria-hidden="true">' +
            escapeHtml(initialsFromName(name)) +
            '</span>';
    }

    function buildNearbyRows(rows: RankRow[], youIndex: number, clock: BioageRankClock): string {
        var start = Math.max(0, youIndex - 2);
        var end = Math.min(rows.length, youIndex + 3);
        var visibleRows = rows.slice(start, end);
        var precision = chooseReductionPrecision(visibleRows, clock);
        var html = '';
        for (var i = start; i < end; i++) {
            var row = rows[i];
            if (!row) continue;
            var isYou = row.isYou;
            var reduction = getReduction(row, clock);
            var nameHtml = escapeHtml(isYou ? 'You' : row.displayName);
            html += '<div class="bioage-rank-row' + (isYou ? ' current' : '') + '">' +
                '<span class="bioage-rank-row-place">#' + (i + 1) + '</span>' +
                buildAvatar(row) +
                '<span class="bioage-rank-row-name">' + nameHtml + '</span>' +
                '<span class="bioage-rank-row-score">' + escapeHtml(formatReduction(reduction, precision)) + '</span>' +
                '</div>';
        }
        return html;
    }

    function buildHtml(
        clock: BioageRankClock,
        rank: number,
        rankedFieldSize: number,
        topPercent: number,
        rows: RankRow[],
        youIndex: number
    ): string {
        var title = clock === 'bortz' ? 'Hypothetical Bortz Age rank' : 'Hypothetical Pheno Age rank';
        var leagueName = clock === 'bortz' ? 'Bortz Age' : 'Pheno Age';
        return '<div class="bioage-rank-summary">' +
            '<div class="bioage-rank-number">#' + rank + '</div>' +
            '<div class="bioage-rank-copy">' +
            '<div class="bioage-rank-label">' + title + '</div>' +
            '<div class="bioage-rank-meta">' + leagueName + ' &middot; ' + rank + ' of ' + rankedFieldSize + '</div>' +
            '<div class="bioage-rank-percentile">Top ' + topPercent + '%</div>' +
            '</div>' +
            '</div>' +
            '<div class="bioage-rank-neighbors">' + buildNearbyRows(rows, youIndex, clock) + '</div>';
    }

    function trackRankPreview(eventName: string, clock: BioageRankClock, outcome: string): void {
        var tracker = Reflect.get(window, 'LwcSiteStats') as LwcSiteStatisticsApi | undefined;
        if (!tracker || typeof tracker.track !== 'function') return;
        tracker.track(eventName, {
            component: 'rank_preview',
            step: 'field_rank',
            outcome: outcome,
            metadata: { clock: clock }
        } satisfies RankPreviewTrackOptions);
    }

    function render(
        targetId: string,
        options: BioageRankPreviewOptions | null | undefined
    ): Promise<void> {
        const target = document.getElementById(targetId);
        if (!(target instanceof HTMLElement) || !options) return Promise.resolve();
        var renderToken = advanceTargetRenderToken(targetId);

        var clock: BioageRankClock = options.clock === 'bortz' ? 'bortz' : 'pheno';
        var ageReduction = Number(options.ageReduction);
        if (!Number.isFinite(ageReduction) || !(options.dateOfBirth instanceof Date)) {
            target.hidden = true;
            target.innerHTML = '';
            return Promise.resolve();
        }

        target.hidden = false;
        target.innerHTML = '<div class="bioage-rank-loading">Calculating rank...</div>';
        trackRankPreview('rank_preview_requested', clock, 'requested');

        return getSharedAthletes()
            .then(function (athletes: unknown[]) {
                if (!isCurrentTargetRenderToken(targetId, renderToken)) return;

                var summaries = athletes.map(mapAthlete).filter(function (athlete): athlete is RankRow {
                    return athlete !== null;
                });
                var field = summaries.filter(function (athlete: RankRow) {
                    var value = clock === 'bortz' ? athlete.bortzAgeReduction : athlete.ageReduction;
                    return Number.isFinite(value);
                });

                var you: RankRow = {
                    name: 'You',
                    displayName: 'You',
                    dateOfBirth: options.dateOfBirth,
                    ageReduction: clock === 'pheno' ? ageReduction : null,
                    bortzAgeReduction: clock === 'bortz' ? ageReduction : null,
                    isYou: true
                };
                var rows = field.concat([you]).sort(compareByClock(clock));
                var youIndex = rows.indexOf(you);
                if (youIndex < 0) throw new Error('Unable to place rank preview row.');

                var rank = youIndex + 1;
                var rankedFieldSize = rows.length;
                var topPercent = Math.max(1, Math.ceil(rank / rankedFieldSize * 100));
                target.innerHTML = buildHtml(clock, rank, rankedFieldSize, topPercent, rows, youIndex);
            })
            .catch(function () {
                if (!isCurrentTargetRenderToken(targetId, renderToken)) return;

                target.hidden = true;
                target.innerHTML = '';
                trackRankPreview('rank_preview_failed', clock, 'failed');
            });
    }

    function clear(targetId: string): void {
        var target = document.getElementById(targetId);
        if (!(target instanceof HTMLElement)) return;
        advanceTargetRenderToken(targetId);
        target.hidden = true;
        target.innerHTML = '';
    }

    window.LwcBioAgeRankPreview = {
        render: render,
        clear: clear
    };
})();

export {};
