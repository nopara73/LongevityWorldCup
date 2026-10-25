/**
 * Age Visualization Module
 * Radar chart of domain percentiles (pheno age or Bortz) with age reduction in the center.
 */

(function () {
    'use strict';

    type PhenoDomainKey = 'immune' | 'liver' | 'kidney' | 'metabolic' | 'inflammation';
    type PhenoContributor =
        | 'calculateImmunePhenoAgeContributor'
        | 'calculateLiverPhenoAgeContributor'
        | 'calculateKidneyPhenoAgeContributor'
        | 'calculateMetabolicPhenoAgeContributor'
        | 'calculateInflammationPhenoAgeContributor';
    type BortzDomain = 'Immune' | 'Liver' | 'Kidney' | 'Metabolism' | 'Inflammation' | 'Vitamin D';
    type ChronologicalAgeKey = 'chronoAtLowestPhenoAge' | 'chronoAtLowestBortzAge';

    interface PhenoDomain {
        readonly key: PhenoDomainKey;
        readonly label: string;
        readonly contributor: PhenoContributor;
    }

    interface BiomarkerDisplay {
        readonly short: string;
        readonly unit: string;
        readonly fromStored?: (value: number) => number;
    }

    interface ClockPayload {
        readonly data: RadarData;
        readonly bio: number;
        readonly chrono: number;
        readonly date: string | null;
    }

    interface ClockState {
        readonly readings: readonly ClockPayload[];
        selectedIndex: number;
    }

    interface RadarTooltipItem {
        readonly raw: unknown;
        readonly dataIndex: number;
    }

    interface RadarPoint {
        readonly x: number;
        readonly y: number;
    }

    interface RadarScale {
        readonly xCenter: number;
        readonly yCenter: number;
        getPointPositionForValue(index: number, value: number): RadarPoint;
    }

    interface RadarPluginChart {
        readonly ctx: CanvasRenderingContext2D;
        readonly data: { readonly labels?: readonly unknown[] };
        readonly scales?: { readonly r?: RadarScale };
    }

    // Pheno Age: 5 domains (Immune first for radar; aligned with Best domain badges)
    var PHENO_DOMAINS: readonly PhenoDomain[] = [
        { key: 'immune', label: 'Immune', contributor: 'calculateImmunePhenoAgeContributor' },
        { key: 'liver', label: 'Liver', contributor: 'calculateLiverPhenoAgeContributor' },
        { key: 'kidney', label: 'Kidney', contributor: 'calculateKidneyPhenoAgeContributor' },
        { key: 'metabolic', label: 'Metabolism', contributor: 'calculateMetabolicPhenoAgeContributor' },
        { key: 'inflammation', label: 'Inflammation', contributor: 'calculateInflammationPhenoAgeContributor' }
    ];
    // Pheno biomarker index -> { shortName, unit, fromStored? } for tooltips. Stored order: age, albumin, creatinine, glucose, log(crp/10), wbc, lymphocyte, mcv, rcdw, ap
    var PHENO_BIOMARKER_DISPLAY: Readonly<Record<number, BiomarkerDisplay>> = {
        1: { short: 'Albumin', unit: 'g/L' },
        2: { short: 'Creatinine', unit: 'µmol/L' },
        3: { short: 'Glucose', unit: 'mmol/L' },
        4: { short: 'CRP', unit: 'mg/L', fromStored: function (v: number) { return 10 * Math.exp(v); } },
        5: { short: 'WBC', unit: '10⁹/L' },
        6: { short: 'Lymphocytes', unit: '%' },
        7: { short: 'MCV', unit: 'fL' },
        8: { short: 'RDW', unit: '%' },
        9: { short: 'ALP', unit: 'U/L' }
    };
    var PHENO_DOMAIN_BIOMARKER_INDICES: Readonly<Record<PhenoDomainKey, readonly number[]>> = {
        immune: [5, 6, 7, 8],
        liver: [1, 9],
        kidney: [2],
        metabolic: [3],
        inflammation: [4]
    };

    // Bortz: 6 domains used for athlete profile radar and Best domain badges (aligned with BadgeDataService.cs).
    // Feature indices in window.BortzAge.features: 0=age, 1=albumin, 2=alp, 3=urea, 4=cholesterol, 5=creatinine, 6=cystatin_c, 7=hba1c, 8=crp, 9=ggt, 10=rbc, 11=mcv, 12=rdw, 13=monocyte, 14=neutrophil, 15=lymphocyte, 16=alt, 17=shbg, 18=vitamin_d, 19=glucose, 20=mch, 21=apoa1
    // Excluded from contribution only (controversial direction): urea, cholesterol, creatinine, alt, shbg, mch
    var BORTZ_CONTRIBUTION_EXCLUDED: Readonly<Record<number, 1>> = { 3: 1, 4: 1, 5: 1, 16: 1, 17: 1, 20: 1 };
    var BORTZ_DOMAIN_INDICES: Readonly<Record<BortzDomain, readonly number[]>> = {
        Immune: [15, 14, 13, 10, 11, 20, 12],
        Liver: [1, 16, 2, 9],
        Kidney: [3, 5, 6],
        Metabolism: [19, 7, 4, 21],
        Inflammation: [8],
        'Vitamin D': [17, 18]
    };
    var BORTZ_DOMAIN_LABELS: readonly BortzDomain[] = ['Immune', 'Liver', 'Kidney', 'Metabolism', 'Inflammation', 'Vitamin D'];
    // Bortz feature index -> unit for tooltips (indices match window.BortzAge.features)
    var BORTZ_BIOMARKER_UNITS: Readonly<Record<number, string>> = {
        0: 'years', 1: 'g/L', 2: 'U/L', 3: 'mmol/L', 4: 'mmol/L', 5: 'µmol/L', 6: 'mg/L', 7: 'mmol/mol',
        8: 'mg/L', 9: 'U/L', 10: '10¹²/L', 11: 'fL', 12: '%', 13: '10⁹/L', 14: '10⁹/L', 15: '%',
        16: 'U/L', 17: 'nmol/L', 18: 'nmol/L', 19: 'mmol/L', 20: 'pg', 21: 'g/L'
    };

    /** Bortz domain contribution (sum of (x-mean)*coeff for indices in that domain, excluding controversial biomarkers). Lower = better. */
    function getBortzDomainContribution(
        values: readonly number[],
        featureIndices: readonly number[]
    ): number {
        var bortzAge = window.BortzAge;
        if (!bortzAge || !values || values.length !== bortzAge.features.length)
            return NaN;
        var sum = 0;
        for (var i = 0; i < featureIndices.length; i++) {
            var idx = featureIndices[i];
            if (idx === undefined) continue;
            if (BORTZ_CONTRIBUTION_EXCLUDED[idx]) continue;
            var f = bortzAge.features[idx];
            var x = values[idx];
            if (!f || x === undefined) return NaN;
            var contribution = bortzAge.calculateFeatureContribution(x, f);
            if (!Number.isFinite(contribution)) return NaN;
            sum += contribution;
        }
        return sum * 10;
    }

    function formatTooltipValue(val: number | undefined): string {
        if (val !== val || val === undefined) return '—';
        if (Number.isInteger(val)) return String(val);
        return Number(val).toFixed(2).replace(/\.?0+$/, '');
    }

    /** Capitalize biomarker id for tooltip (e.g. "glucose" -> "Glucose", "vitamin_d" -> "Vitamin D"). */
    function formatBortzLabel(id: string): string {
        if (!id) return '—';
        return id.split('_').map(function (part: string) { return part.charAt(0).toUpperCase() + part.slice(1).toLowerCase(); }).join(' ');
    }

    /** For a list of athletes with scores (lower = better), compute percentile for the current athlete. 0–100, higher = better. */
    function scoreToPercentile(currentScore: number | undefined, allScores: readonly number[]): number | null {
        if (allScores.length === 0 || currentScore === undefined || currentScore !== currentScore) return null;
        var sorted = allScores.slice().filter(function (s: number) { return s === s; }).sort(function (a: number, b: number) { return a - b; });
        var n = sorted.length;
        if (n === 0) return null;
        var rank = 1;
        for (var i = 0; i < sorted.length; i++) {
            var score = sorted[i];
            if (score !== undefined && score < currentScore) rank++;
            else break;
        }
        return Math.round((n - rank + 1) / n * 100);
    }

    /** Returns up to 100 athletes closest by chronological age (absolute difference). */
    function getClosestAthletesByAge(
        currentChronoAge: number,
        athletes: readonly AgeVisualizationAthlete[],
        chronoKey: ChronologicalAgeKey
    ): AgeVisualizationAthlete[] {
        if (!Number.isFinite(currentChronoAge) || !Array.isArray(athletes)) return [];

        return athletes
            .filter(function (a) {
                return typeof a[chronoKey] === 'number' && Number.isFinite(a[chronoKey]);
            })
            .sort(function (a, b) {
                var aAge = a[chronoKey];
                var bAge = b[chronoKey];
                return Math.abs((aAge ?? currentChronoAge) - currentChronoAge)
                    - Math.abs((bAge ?? currentChronoAge) - currentChronoAge);
            })
            .slice(0, 100);
    }


    function getPhenoRadarData(
        athleteData: AgeVisualizationAthlete | null | undefined,
        athleteResults: readonly AgeVisualizationAthlete[],
        currentChronoAge: number
    ): RadarData | null {
        var labels: string[] = [];
        var values: number[] = [];
        var tooltipContributors: string[][] = [];
        var athletesWithPheno = getClosestAthletesByAge(
            currentChronoAge,
            (athleteResults || []).filter(function (a) { return a.bestBiomarkerValues && a.bestBiomarkerValues.length; }),
            'chronoAtLowestPhenoAge'
        );
        if (athletesWithPheno.length === 0 || !athleteData || !athleteData.bestBiomarkerValues) return null;

        var mv = athleteData.bestBiomarkerValues;
        for (var d = 0; d < PHENO_DOMAINS.length; d++) {
            var dom = PHENO_DOMAINS[d];
            if (!dom) continue;
            labels.push(dom.label);
            var phenoAge = window.PhenoAge;
            var contributorFn = phenoAge && phenoAge[dom.contributor];
            if (typeof contributorFn !== 'function') {
                values.push(50);
                tooltipContributors.push([]);
                continue;
            }
            const contributor = contributorFn;
            var myScore = contributor(mv);
            var allScores = athletesWithPheno.map(function (a) {
                return a.bestBiomarkerValues ? contributor(a.bestBiomarkerValues) : NaN;
            });
            var pct = scoreToPercentile(myScore, allScores);
            values.push(pct !== null ? pct : 50);
            var indices = PHENO_DOMAIN_BIOMARKER_INDICES[dom.key];
            var parts: string[] = [];
            if (indices) {
                for (var i = 0; i < indices.length; i++) {
                    var idx = indices[i];
                    if (idx === undefined) continue;
                    var disp = PHENO_BIOMARKER_DISPLAY[idx];
                    var v = mv[idx];
                    if (disp) {
                        var displayVal = disp.fromStored && v !== undefined ? disp.fromStored(v) : v;
                        parts.push(disp.short + ': ' + formatTooltipValue(displayVal) + ' ' + disp.unit);
                    }
                }
            }
            tooltipContributors.push(parts);
        }
        return { labels: labels, values: values, tooltipContributors: tooltipContributors };
    }

    function getBortzRadarData(
        athleteData: AgeVisualizationAthlete | null | undefined,
        athleteResults: readonly AgeVisualizationAthlete[],
        currentChronoAge: number
    ): RadarData | null {
        var labels = BORTZ_DOMAIN_LABELS.slice();
        var values: number[] = [];
        var tooltipContributors: string[][] = [];
        var athletesWithBortz = getClosestAthletesByAge(
            currentChronoAge,
            (athleteResults || []).filter(function (a) { return a.bestBortzValues && a.bestBortzValues.length; }),
            'chronoAtLowestBortzAge'
        );
        if (athletesWithBortz.length === 0 || !athleteData || !athleteData.bestBortzValues) return null;

        var bv = athleteData.bestBortzValues;
        var features = window.BortzAge?.features;
        for (var i = 0; i < BORTZ_DOMAIN_LABELS.length; i++) {
            var name = BORTZ_DOMAIN_LABELS[i];
            if (!name) continue;
            var indices = BORTZ_DOMAIN_INDICES[name];
            var myScore = getBortzDomainContribution(bv, indices);
            var allScores = athletesWithBortz.map(function (a) {
                return a.bestBortzValues ? getBortzDomainContribution(a.bestBortzValues, indices) : NaN;
            });
            var pct = scoreToPercentile(myScore, allScores);
            values.push(pct !== null ? pct : 50);
            var parts: string[] = [];
            if (indices && features) {
                for (var j = 0; j < indices.length; j++) {
                    var idx = indices[j];
                    if (idx === undefined) continue;
                    if (BORTZ_CONTRIBUTION_EXCLUDED[idx]) continue;
                    var f = features[idx];
                    var v = bv[idx];
                    var shortName = f && f.id ? (f.id === 'alp' ? 'ALP' : f.id === 'crp' ? 'CRP' : f.id === 'hba1c' ? 'HbA1c' : f.id === 'ggt' ? 'GGT' : f.id === 'rbc' ? 'RBC' : f.id === 'mcv' ? 'MCV' : f.id === 'mch' ? 'MCH' : f.id === 'rdw' ? 'RDW' : f.id === 'alt' ? 'ALT' : f.id === 'apoa1' ? 'ApoA1' : f.id === 'monocyte_percentage' ? 'Monocytes' : f.id === 'neutrophil_percentage' ? 'Neutrophils' : f.id === 'lymphocyte_percentage' ? 'Lymphocytes' : f.id === 'cystatin_c' ? 'Cystatin C' : f.id === 'vitamin_d' ? 'Vitamin D' : f.id === 'shbg' ? 'SHBG' : formatBortzLabel(f.id)) : '—';
                    var unit = BORTZ_BIOMARKER_UNITS[idx] || '';
                    parts.push(shortName + ': ' + formatTooltipValue(v) + (unit ? ' ' + unit : ''));
                }
            }
            tooltipContributors.push(parts);
        }
        return { labels: labels, values: values, tooltipContributors: tooltipContributors };
    }

    var radarChartInstance: RadarChartInstance | null = null;
    var radarResizeObserver: ResizeObserver | null = null;
    var radarResizeFrame = 0;
    var radarBuildGeneration = 0;
    var radarBuildFrame = 0;
    var radarBuildTimer = 0;
    const RADAR_ANIMATION_DURATION_MS = 220;
    const RADAR_WEB_BOUNDARY_PERCENTILE = 80;
    const RADAR_SCALE_MAX_PERCENTILE = 100;

    function getRadarAnimationDuration(): number {
        if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
            return RADAR_ANIMATION_DURATION_MS;
        }

        return window.matchMedia('(prefers-reduced-motion: reduce)').matches
            ? 0
            : RADAR_ANIMATION_DURATION_MS;
    }

    function scheduleRadarResize() {
        if (radarResizeFrame) return;
        var requestFrame = window.requestAnimationFrame || function (callback) { return window.setTimeout(callback, 0); };
        radarResizeFrame = requestFrame(function () {
            radarResizeFrame = 0;
            if (radarChartInstance) radarChartInstance.resize();
        });
    }

    function cancelRadarResize() {
        if (!radarResizeFrame) return;
        if (window.cancelAnimationFrame) window.cancelAnimationFrame(radarResizeFrame);
        else window.clearTimeout(radarResizeFrame);
        radarResizeFrame = 0;
    }

    function cancelRadarBuild(): void {
        radarBuildGeneration++;
        cancelAnimationFrame(radarBuildFrame);
        window.clearTimeout(radarBuildTimer);
        radarBuildFrame = 0;
        radarBuildTimer = 0;
    }

    function destroyRadarChart() {
        cancelRadarBuild();
        cancelRadarResize();
        if (radarResizeObserver) {
            var wrapper = document.getElementById('ageRadarWrapper');
            if (wrapper) radarResizeObserver.disconnect();
            radarResizeObserver = null;
        }
        if (radarChartInstance) {
            radarChartInstance.destroy();
            radarChartInstance = null;
        }
    }

    function updateCenterAndFallback(bioAge: number, chronoAge: number, useBortz: boolean): void {
        var diff = bioAge - chronoAge;
        // True minus signs, matching every other figure on the site.
        var diffText = (diff > 0 ? '+' : diff < 0 ? '−' : '') + Math.abs(diff).toFixed(1) + ' years';
        var label = diff < 0 ? 'age reduction' : 'age acceleration';
        var bioText = 'biological age: ' + (Number.isFinite(bioAge) ? bioAge.toFixed(1) : '—');

        var centerVal = document.getElementById('ageRadarCenterValue');
        var centerLbl = document.getElementById('ageRadarCenterLabel');
        var centerBio = document.getElementById('ageRadarCenterBio');
        var centerEl = document.getElementById('ageRadarCenter');
        var fallbackVal = document.getElementById('ageRadarFallbackValue');
        var fallbackLbl = document.getElementById('ageRadarFallbackLabel');
        var fallbackBio = document.getElementById('ageRadarFallbackBio');
        if (centerVal) centerVal.textContent = diffText;
        if (centerLbl) centerLbl.textContent = label;
        if (centerBio) centerBio.textContent = bioText;
        if (centerEl) {
            centerEl.classList.remove('positive', 'negative');
            if (diff < 0) centerEl.classList.add('positive');
            else if (diff > 0) centerEl.classList.add('negative');
            centerEl.setAttribute('data-diff-sign', diff < 0 ? 'negative' : diff > 0 ? 'positive' : '');
        }
        if (fallbackVal) fallbackVal.textContent = diffText;
        if (fallbackLbl) fallbackLbl.textContent = label;
        if (fallbackBio) fallbackBio.textContent = bioText;
    }

    function applyRadarData(data: RadarData | null): void {
        if (!radarChartInstance || !data) return;
        radarChartInstance.data.labels = data.labels;
        var dataset = radarChartInstance.data.datasets[0];
        if (dataset) dataset.data = data.values;
        if (data.tooltipContributors) radarChartInstance._radarTooltipContributors = data.tooltipContributors;
        radarChartInstance.update('active');
    }

    var resultDateFormat = new Intl.DateTimeFormat('en-GB', {
        day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC'
    });

    function buildClockState(
        bortz: boolean,
        athleteData: AgeVisualizationAthlete | null | undefined,
        results: readonly AgeVisualizationAthlete[],
        history: AgeVisualizationHistory | undefined
    ): ClockState {
        var readings: ClockPayload[] = [];
        var radarData = bortz ? getBortzRadarData : getPhenoRadarData;
        if (history) {
            var entries = (bortz ? history.bortz : history.pheno).slice()
                .filter(function (entry) {
                    return Number.isFinite(Date.parse(entry.date)) && Number.isFinite(entry.bio)
                        && Number.isFinite(entry.chrono) && entry.values.length >= (bortz ? 22 : 10)
                        && entry.values.every(Number.isFinite);
                }).sort(function (a, b) { return Date.parse(a.date) - Date.parse(b.date); });
            entries.forEach(function (entry) {
                // Every domain and the age summary must describe this same blood draw.
                var values = bortz ? { bestBortzValues: entry.values } : { bestBiomarkerValues: entry.values };
                var data = radarData(values, results, entry.chrono);
                if (data) readings.push({ data: data, bio: entry.bio, chrono: entry.chrono, date: entry.date });
            });
        } else {
            var bio = bortz ? athleteData?.lowestBortzAge : athleteData?.lowestPhenoAge;
            var chrono = bortz ? athleteData?.chronoAtLowestBortzAge : athleteData?.chronoAtLowestPhenoAge;
            if (typeof bio === 'number' && Number.isFinite(bio) && typeof chrono === 'number' && Number.isFinite(chrono)) {
                var data = radarData(athleteData, results, chrono);
                if (data) readings.push({ data: data, bio: bio, chrono: chrono, date: null });
            }
        }
        var selectedIndex = 0;
        readings.forEach(function (reading, index) {
            if (reading.bio < (readings[selectedIndex]?.bio ?? Infinity)) selectedIndex = index;
        });
        return { readings: readings, selectedIndex: selectedIndex };
    }

    function resultLabel(reading: ClockPayload): string {
        var date = reading.date ? resultDateFormat.format(new Date(reading.date)) + ' · ' : '';
        return date + reading.bio.toFixed(1) + ' years';
    }

    function generateAgeVisualization(
        bioAge: number,
        chronoAge: number,
        athleteData: AgeVisualizationAthlete | null | undefined,
        athleteResults: readonly AgeVisualizationAthlete[] | null | undefined,
        history?: AgeVisualizationHistory,
        loadChart?: () => Promise<unknown>
    ): void {
        var wrapper = document.getElementById('ageRadarWrapper');
        var fallback = document.getElementById('ageRadarFallback');
        var canvasElement = document.getElementById('ageRadarChart');
        var canvas = canvasElement instanceof HTMLCanvasElement ? canvasElement : null;
        var switchEl = document.getElementById('ageClockSwitch');
        var tabPheno = document.getElementById('ageTabPheno');
        var tabBortz = document.getElementById('ageTabBortz');

        if (!wrapper || !fallback) {
            destroyRadarChart();
            var legacy = document.getElementById('targetShootingVisualization') || document.querySelector('#ageVisualization #targetShootingVisualization');
            if (legacy) {
                generateAgeVisualizationInternal(legacy, bioAge, chronoAge);
            }
            return;
        }

        destroyRadarChart();
        const buildGeneration = radarBuildGeneration;
        var results: readonly AgeVisualizationAthlete[] = Array.isArray(athleteResults) ? athleteResults : [];
        var pheno = buildClockState(false, athleteData, results, history);
        var bortz = buildClockState(true, athleteData, results, history);
        var hasPheno = pheno.readings.length > 0;
        var hasBortz = bortz.readings.length > 0;
        var bothClocks = hasPheno && hasBortz;
        var useBortz = hasBortz;
        var visualization = document.getElementById('ageVisualization');
        if (visualization) visualization.hidden = !hasPheno && !hasBortz;
        if (switchEl) switchEl.classList.toggle('visible', hasPheno || hasBortz);
        if (tabPheno) tabPheno.hidden = !hasPheno;
        if (tabBortz) tabBortz.hidden = !hasBortz;

        var picker = document.getElementById('ageResultPicker');
        var selectElement = document.getElementById('ageResultSelect');
        var select = selectElement instanceof HTMLSelectElement ? selectElement : null;
        var earlierElement = document.getElementById('ageResultEarlier');
        var earlier = earlierElement instanceof HTMLButtonElement ? earlierElement : null;
        var laterElement = document.getElementById('ageResultLater');
        var later = laterElement instanceof HTMLButtonElement ? laterElement : null;
        var current = document.getElementById('ageResultCurrent');
        var resultDate = document.getElementById('ageResultDate');
        var chevron = picker?.querySelector<SVGElement>('.age-result-chevron');

        function showReading(index: number): void {
            var state = useBortz ? bortz : pheno;
            const reading = state.readings[index];
            if (!reading) return;
            state.selectedIndex = index;
            // The web takes the chosen clock's colour, so set it before the chart redraws.
            if (canvas) canvas.dataset.clock = useBortz ? 'bortz' : 'pheno';
            applyRadarData(reading.data);
            updateCenterAndFallback(reading.bio, reading.chrono, useBortz);
            if (select) select.value = String(index);
            if (earlier) earlier.disabled = index === 0;
            if (later) later.disabled = index === state.readings.length - 1;
            if (resultDate) {
                resultDate.textContent = reading.date ? resultDateFormat.format(new Date(reading.date)) : '';
                resultDate.setAttribute('datetime', reading.date?.slice(0, 10) || '');
            }
            if (canvas) {
                canvas.dataset.resultDate = reading.date || '';
                canvas.setAttribute('aria-label', (useBortz ? 'Bortz Age' : 'Pheno Age') + ': '
                    + resultLabel(reading) + '. Domain percentiles: ' + reading.data.labels.map(function (label, domainIndex) {
                        return label + ': ' + reading.data.values[domainIndex];
                    }).join('; ') + '.');
            }
        }

        function selectClock(isBortz: boolean): void {
            var state = isBortz ? bortz : pheno;
            var reading = state.readings[state.selectedIndex];
            if (!reading) return;
            useBortz = isBortz;
            var clockName = isBortz ? 'Bortz Age' : 'Pheno Age';
            if (tabPheno) {
                tabPheno.setAttribute('aria-selected', isBortz ? 'false' : 'true');
                tabPheno.tabIndex = isBortz ? -1 : 0;
            }
            if (tabBortz) {
                tabBortz.setAttribute('aria-selected', isBortz ? 'true' : 'false');
                tabBortz.tabIndex = isBortz ? 0 : -1;
            }
            var multiple = state.readings.length > 1;
            if (picker) {
                picker.hidden = !reading.date;
                picker.dataset.multiple = String(multiple);
            }
            if (select) {
                select.hidden = !multiple;
                select.setAttribute('aria-label', clockName + ' result');
                select.replaceChildren(...state.readings.map(function (entry, index) {
                    var option = document.createElement('option');
                    option.value = String(index);
                    option.textContent = resultLabel(entry);
                    return option;
                }));
            }
            if (earlier) earlier.hidden = !multiple;
            if (later) later.hidden = !multiple;
            if (chevron) chevron.style.display = multiple ? '' : 'none';
            if (current) current.setAttribute('aria-hidden', multiple ? 'true' : 'false');
            showReading(state.selectedIndex);
        }

        if (select) select.onchange = function () { showReading(Number(select?.value)); };
        if (earlier) earlier.onclick = function () { showReading((useBortz ? bortz : pheno).selectedIndex - 1); };
        if (later) later.onclick = function () { showReading((useBortz ? bortz : pheno).selectedIndex + 1); };
        if (tabPheno) tabPheno.onclick = function () { selectClock(false); };
        if (tabBortz) tabBortz.onclick = function () { selectClock(true); };
        function handleClockKey(event: KeyboardEvent): void {
            if (!bothClocks) return;
            if (event.key === 'ArrowLeft' || event.key === 'ArrowRight' || event.key === 'Home' || event.key === 'End') {
                event.preventDefault();
                var nextBortz = event.key === 'Home' || (event.key !== 'End' && !useBortz);
                selectClock(nextBortz);
                (nextBortz ? tabBortz : tabPheno)?.focus();
            }
        }
        if (tabPheno) tabPheno.onkeydown = handleClockKey;
        if (tabBortz) tabBortz.onkeydown = handleClockKey;
        selectClock(useBortz);

        function renderRadar(): void {
            if (buildGeneration !== radarBuildGeneration || !wrapper || !fallback) return;
            var data = (useBortz ? bortz : pheno).readings[(useBortz ? bortz : pheno).selectedIndex]?.data;

            var centerEl = document.getElementById('ageRadarCenter');
            var ChartConstructor = window.Chart;
            if (!data || !ChartConstructor || !canvas) {
                destroyRadarChart();
                wrapper.style.display = 'none';
                if (centerEl) centerEl.style.display = 'none';
                fallback.style.display = data ? 'flex' : 'none';
                fallback.style.flexDirection = 'column';
                fallback.style.alignItems = 'center';
                fallback.style.justifyContent = 'center';
                return;
            }

            wrapper.style.display = 'block';
            if (centerEl) centerEl.style.display = 'flex';
            fallback.style.display = 'none';

            var ctx = canvas.getContext('2d');
            if (!ctx) {
                wrapper.style.display = 'none';
                if (centerEl) centerEl.style.display = 'none';
                fallback.style.display = 'flex';
                fallback.style.flexDirection = 'column';
                fallback.style.alignItems = 'center';
                fallback.style.justifyContent = 'center';
                return;
            }
            var fontFamily = window.getComputedStyle(document.body).fontFamily || 'system-ui, sans-serif';
            var modal = document.getElementById('detailsModal');
            var modalStyles = modal ? window.getComputedStyle(modal) : null;
            var radarLabelColor = modalStyles ? (modalStyles.getPropertyValue('--athlete-modal-muted').trim() || 'rgba(0,0,0,0.7)') : 'rgba(0,0,0,0.7)';
            var radarGridColor = modalStyles ? (modalStyles.getPropertyValue('--athlete-modal-chart-grid').trim() || 'rgba(0,0,0,0.08)') : 'rgba(0,0,0,0.08)';
            var radarFill = modalStyles ? (modalStyles.getPropertyValue('--athlete-modal-cream-soft').trim() || 'rgba(246,244,237,.18)') : 'rgba(246,244,237,.18)';
            var radarStroke = modalStyles ? (modalStyles.getPropertyValue('--athlete-modal-cream').trim() || 'rgba(246,244,237,.92)') : 'rgba(246,244,237,.92)';
            // The web wears the selected clock's colour from Results over time.
            function clockColor(alpha?: number): string {
                var token = canvas?.dataset.clock === 'pheno' ? '--athlete-modal-chart-pheno' : '--athlete-modal-chart-bortz';
                var color = modalStyles ? modalStyles.getPropertyValue(token).trim() : '';
                if (!color) return alpha == null ? radarStroke : radarFill;
                if (alpha == null) return color;
                var channels = color.match(/rgba?\(([^)]+)\)/)?.[1]?.split(',').slice(0, 3).join(',');
                return channels ? 'rgba(' + channels + ',' + alpha + ')' : color;
            }
            var radarTooltip = modalStyles ? (modalStyles.getPropertyValue('--athlete-modal-chart-tooltip').trim() || 'rgba(0,0,0,0.78)') : 'rgba(0,0,0,0.78)';
            var radarInk = modalStyles ? (modalStyles.getPropertyValue('--athlete-modal-text').trim() || '#fff') : '#fff';
            const radarTextContext = ctx;
            function wrapRadarText(text: string, width: number, size: number, weight = '400'): string[] {
                radarTextContext.save();
                radarTextContext.font = weight + ' ' + size + 'px ' + fontFamily;
                var lines: string[] = [];
                var line = '';
                text.split(/\s+/).forEach(function (word) {
                    var candidate = line ? line + ' ' + word : word;
                    if (line && radarTextContext.measureText(candidate).width <= width) {
                        line = candidate;
                        return;
                    }
                    if (line && radarTextContext.measureText(candidate).width > width) {
                        lines.push(line);
                        line = '';
                    }
                    for (const glyph of word) {
                        if (line && radarTextContext.measureText(line + glyph).width > width) {
                            lines.push(line);
                            line = '';
                        }
                        line += glyph;
                    }
                });
                if (line) lines.push(line);
                radarTextContext.restore();
                return lines;
            }
            function tooltipFont(context: { readonly chart: { readonly width: number } }): { family: string; size: number; lineHeight: number } {
                return { family: fontFamily, size: context.chart.width < 260 ? 11 : 12, lineHeight: context.chart.width < 260 ? 1.1 : 1.2 };
            }
            var radarPointHitRadius = window.matchMedia && window.matchMedia('(pointer: coarse)').matches ? 18 : 8;
            var contributors = data.tooltipContributors || [];
            var radarWebSpokesPlugin = {
                id: 'radarWebSpokes',
                beforeDatasetsDraw: function (chart: RadarPluginChart): void {
                    var scale = chart.scales?.r;
                    var labelCount = chart.data.labels?.length ?? 0;
                    if (!scale || labelCount === 0) return;

                    var chartContext = chart.ctx;
                    chartContext.save();
                    chartContext.beginPath();
                    for (var index = 0; index < labelCount; index++) {
                        var boundary = scale.getPointPositionForValue(index, RADAR_WEB_BOUNDARY_PERCENTILE);
                        chartContext.moveTo(scale.xCenter, scale.yCenter);
                        chartContext.lineTo(boundary.x, boundary.y);
                    }
                    chartContext.strokeStyle = radarGridColor;
                    chartContext.lineWidth = 1;
                    chartContext.stroke();
                    chartContext.restore();
                }
            };

            radarChartInstance = new ChartConstructor(ctx, {
                type: 'radar',
                plugins: [radarWebSpokesPlugin],
                data: {
                    labels: data.labels,
                    datasets: [{
                        label: 'Percentile (higher = better)',
                        data: data.values,
                        backgroundColor: function () { return clockColor(0.16); },
                        borderColor: function () { return clockColor(); },
                        // Results switch in Chart.js's 'active' mode, which would otherwise paint a saturated hover colour.
                        hoverBackgroundColor: function () { return clockColor(0.16); },
                        hoverBorderColor: function () { return clockColor(); },
                        borderWidth: 2.5,
                        pointBackgroundColor: function () { return clockColor(); },
                        pointBorderColor: 'rgba(18,20,20,.95)',
                        pointBorderWidth: 2,
                        pointHoverBackgroundColor: function () { return clockColor(); },
                        borderJoinStyle: 'round',
                        pointHoverBorderColor: 'rgba(18,20,20,.95)',
                        pointHoverBorderWidth: 2,
                        pointRadius: 5,
                        pointHoverRadius: 7,
                        pointHitRadius: radarPointHitRadius,
                        clip: 8
                    }]
                },
                options: {
                    responsive: true,
                    // The wrapper sets a landscape shape, so side labels no longer shrink the web inside a square.
                    maintainAspectRatio: false,
                    layout: { padding: function (context: { readonly chart: { readonly width: number } }) { return context.chart.width < 160 ? 12 : 4; } },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: radarTooltip,
                            titleColor: radarInk,
                            bodyColor: radarInk,
                            borderColor: function () { return clockColor(); },
                            borderWidth: 1,
                            displayColors: false,
                            padding: function (context: { readonly chart: { readonly width: number; readonly height: number } }) { return context.chart.width < 260 || context.chart.height < 180 ? 6 : 10; },
                            cornerRadius: 6,
                            // Moving or resizing between contributors can carry stale bounds outside a narrow canvas.
                            animations: {
                                numbers: { duration: 0 },
                                opacity: { duration: function () { return getRadarAnimationDuration() ? 200 : 0; } }
                            },
                            titleFont: function (context: { readonly chart: { readonly width: number } }) { return { ...tooltipFont(context), weight: '600' }; },
                            bodyFont: tooltipFont,
                            bodySpacing: function (context: { readonly chart: { readonly width: number } }) { return context.chart.width < 260 ? 1 : 2; },
                            titleMarginBottom: function (context: { readonly chart: { readonly width: number } }) { return context.chart.width < 260 ? 4 : 6; },
                            callbacks: {
                                title: function (tooltipItems: readonly RadarTooltipItem[]) {
                                    var firstItem = tooltipItems[0];
                                    if (!firstItem) return '';
                                    return String(firstItem.raw) + 'th percentile';
                                },
                                label: function () { return null; },
                                afterBody: function (tooltipItems: readonly RadarTooltipItem[]) {
                                    var firstItem = tooltipItems[0];
                                    if (!firstItem) return [];
                                    const chart = radarChartInstance;
                                    if (!chart) return [];
                                    var contrib = chart._radarTooltipContributors;
                                    var idx = firstItem.dataIndex;
                                    var contributorLines = contrib?.[idx];
                                    if (!contributorLines?.length) return [];
                                    var compact = chart.width < 260 || chart.height < 180;
                                    var wrapped = contributorLines.flatMap(function (line) {
                                        return wrapRadarText(line, Math.max(32, chart.width - (compact ? 12 : 20)), chart.width < 260 ? 11 : 12);
                                    });
                                    return compact ? wrapped : [''].concat(wrapped);
                                }
                            }
                        }
                    },
                    scales: {
                        r: {
                            min: 0,
                            max: RADAR_SCALE_MAX_PERCENTILE,
                            beginAtZero: true,
                            ticks: {
                                stepSize: 20,
                                backdropColor: 'transparent',
                                display: false
                            },
                            pointLabels: {
                                font: function (context: { readonly chart: { readonly width: number } }) {
                                    return { family: fontFamily, size: context.chart.width >= 420 ? 12 : 11, weight: '500' };
                                },
                                color: radarLabelColor,
                                // Each domain carries its percentile under its name, like a labelled axis.
                                callback: function (this: { readonly chart?: { readonly width: number; readonly data: { readonly datasets: ReadonlyArray<{ readonly data: readonly unknown[] }> } } }, label: string, index: number): string | string[] {
                                    var value = Number(this?.chart?.data.datasets[0]?.data[index]);
                                    var width = this?.chart?.width ?? 420;
                                    var names = width < 260 ? wrapRadarText(label, Math.max(32, width * .28), 11, '500') : [label];
                                    return Number.isFinite(value) ? names.concat(String(Math.round(value))) : names;
                                },
                                padding: 6,
                                z: 1
                            },
                            grid: {
                                color: function (context: { readonly tick?: { readonly value?: number } }): string {
                                    return (context.tick?.value ?? 0) <= RADAR_WEB_BOUNDARY_PERCENTILE
                                        ? radarGridColor
                                        : 'transparent';
                                },
                                lineWidth: 1
                            },
                            angleLines: { display: false }
                        }
                    },
                    animation: { duration: getRadarAnimationDuration() },
                    hover: { mode: 'nearest' }
                }
            });
            radarChartInstance._radarTooltipContributors = contributors;
            if (radarResizeObserver && wrapper) radarResizeObserver.disconnect();
            if ('ResizeObserver' in window) {
                radarResizeObserver = new ResizeObserver(scheduleRadarResize);
                radarResizeObserver.observe(wrapper);
            }
        }

        const load = loadChart;
        if (load && canvas && (hasPheno || hasBortz)) {
            // The current result is readable now. Keep the canvas space stable
            // while its optional dependency and expensive drawing wait to paint.
            wrapper.style.display = 'block';
            const center = document.getElementById('ageRadarCenter');
            if (center) center.style.display = 'flex';
            fallback.style.display = 'none';
            radarBuildFrame = requestAnimationFrame(() => {
                radarBuildFrame = 0;
                if (buildGeneration !== radarBuildGeneration) return;
                radarBuildTimer = window.setTimeout(() => {
                    radarBuildTimer = 0;
                    if (buildGeneration !== radarBuildGeneration) return;
                    try { void load().then(renderRadar, renderRadar); }
                    catch { renderRadar(); }
                }, 0);
            });
        } else renderRadar();
    }

    function generateAgeVisualizationInternal(
        visualizationContainer: HTMLElement,
        bioAge: number,
        chronoAge: number
    ): void {
        var maxAge = 100;
        var bioPct = Math.min((bioAge / maxAge) * 100, 100);
        var chronoPct = Math.min((chronoAge / maxAge) * 100, 100);
        var gradient;
        if (bioAge === chronoAge) {
            gradient = 'radial-gradient(circle, rgba(246,244,237,.72) 100%, rgba(246,244,237,.72) 100%)';
        } else if (bioAge < chronoAge) {
            gradient = 'radial-gradient(circle, rgba(246,244,237,.78) ' + bioPct + '%, rgba(181,226,226,.45) ' + chronoPct + '%, rgba(255,255,255,.18) 100%)';
        } else {
            gradient = 'radial-gradient(circle, rgba(246,244,237,.78) ' + chronoPct + '%, rgba(213,224,221,.42) ' + bioPct + '%, rgba(255,255,255,.18) 100%)';
        }
        visualizationContainer.style.background = gradient;
        visualizationContainer.style.borderRadius = '50%';
        visualizationContainer.style.position = 'relative';
        visualizationContainer.style.border = '2px solid rgba(246,244,237,.5)';
        visualizationContainer.innerHTML = '';
        var difference = bioAge - chronoAge;
        var diff = difference.toFixed(1);
        var text = (difference > 0 ? '+' : difference < 0 ? '−' : '') + Math.abs(difference).toFixed(1) + ' yrs';
        visualizationContainer.innerHTML = '<span style="position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);color:var(--light-text-color);font-weight:bold;">' + text + '</span>';
    }

    /** Badge label -> Bortz domain key (same as athlete profile radar). Used so Best domain badge tooltips show the same biomarkers as the profile. */
    var BEST_DOMAIN_LABEL_TO_KEY: Readonly<Record<string, BortzDomain>> = {
        'Best domain – liver': 'Liver',
        'Best domain – kidney': 'Kidney',
        'Best domain – metabolic': 'Metabolism',
        'Best domain – immune': 'Immune',
        'Best domain – inflammation': 'Inflammation',
        'Best domain – vitamin D': 'Vitamin D'
    };

    /** Returns the biomarker part of a Best domain tooltip, e.g. "(albumin 45 g/L, ALP 82 U/L, GGT 3.2 U/L)". Uses same indices and exclusions as athlete profile radar. */
    function getBestDomainBiomarkerTooltip(
        badgeLabel: string,
        bestBortzValues: readonly number[] | null | undefined
    ): string | null {
        if (!bestBortzValues || bestBortzValues.length < 22) return null;
        var key = BEST_DOMAIN_LABEL_TO_KEY[badgeLabel];
        if (!key || !BORTZ_DOMAIN_INDICES[key]) return null;
        var indices = BORTZ_DOMAIN_INDICES[key];
        var features = window.BortzAge?.features;
        var parts: string[] = [];
        for (var j = 0; j < indices.length; j++) {
            var idx = indices[j];
            if (idx === undefined) continue;
            if (BORTZ_CONTRIBUTION_EXCLUDED[idx]) continue;
            var f = features && features[idx];
            var v = bestBortzValues[idx];
            var shortName = f && f.id ? (f.id === 'alp' ? 'ALP' : f.id === 'crp' ? 'CRP' : f.id === 'hba1c' ? 'HbA1c' : f.id === 'ggt' ? 'GGT' : f.id === 'rbc' ? 'RBC' : f.id === 'mcv' ? 'MCV' : f.id === 'mch' ? 'MCH' : f.id === 'rdw' ? 'RDW' : f.id === 'alt' ? 'ALT' : f.id === 'apoa1' ? 'ApoA1' : f.id === 'monocyte_percentage' ? 'Monocytes' : f.id === 'neutrophil_percentage' ? 'Neutrophils' : f.id === 'lymphocyte_percentage' ? 'Lymphocytes' : f.id === 'cystatin_c' ? 'Cystatin C' : f.id === 'vitamin_d' ? 'Vitamin D' : f.id === 'shbg' ? 'SHBG' : formatBortzLabel(f.id)) : '—';
            var unit = BORTZ_BIOMARKER_UNITS[idx] || '';
            parts.push(shortName + ' ' + formatTooltipValue(v) + (unit ? ' ' + unit : ''));
        }
        return parts.length ? '(' + parts.join(', ') + ')' : null;
    }

    // Invalidate pending work at close intent; the drawn chart can leave with
    // its whole card before the existing completion path destroys it.
    document.addEventListener('athlete-dialog-close-requested', cancelRadarBuild, true);
    document.addEventListener('athlete-dialog-closing', cancelRadarBuild, true);
    window.generateAgeVisualization = generateAgeVisualization;
    window.generateAgeVisualizationInternal = generateAgeVisualizationInternal;
    window.destroyAgeRadarChart = destroyRadarChart;
    window.getBestDomainBiomarkerTooltip = getBestDomainBiomarkerTooltip;
})();

export {};
