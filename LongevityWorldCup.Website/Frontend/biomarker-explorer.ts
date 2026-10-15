(function () {
    'use strict';

    type View = 'time' | 'context' | 'relationships';
    type Interaction = 'inspect' | 'select' | 'pan';
    type Point = { x: number; y: number | null; recordId?: string | null };
    type Bounds = { x0: number; x1: number; y0: number; y1: number };
    interface Scale {
        readonly min: number;
        readonly max: number;
        getPixelForValue(value: number): number;
        getValueForPixel(pixel: number): number;
    }
    interface PlotChart {
        readonly ctx: CanvasRenderingContext2D;
        readonly chartArea: { left: number; right: number; top: number; bottom: number };
        readonly scales: Readonly<Record<string, Scale | undefined>>;
        readonly options: { scales: Record<string, { min?: number; max?: number } | undefined> };
        getDatasetMeta(index: number): { data: readonly { x: number; y: number }[] };
        destroy(): void;
        resize(): void;
        draw(): void;
        update(mode?: string): void;
    }
    interface PlotConstructor { new (context: CanvasRenderingContext2D, configuration: unknown): PlotChart; }
    interface Plot {
        readonly key: string;
        readonly canvas: HTMLCanvasElement;
        readonly figure: HTMLElement;
        readonly points: readonly Point[];
        readonly datasets: readonly Record<string, unknown>[];
        readonly xLog: boolean;
        readonly yLog: boolean;
        chart: PlotChart | null;
        probeX: number | null;
        brush: Bounds | null;
        readonly abort: AbortController;
    }

    const dayMilliseconds = 86400000;
    const shortDate = new Intl.DateTimeFormat('en', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
    const number = new Intl.NumberFormat('en', { maximumFractionDigits: 2 });
    const icons: Readonly<Record<string, string>> = {
        reset: '<path d="M3 10a9 9 0 1 1 2 9M3 4v6h6"/>',
        plus: '<path d="M12 5v14M5 12h14"/>',
        minus: '<path d="M5 12h14"/>',
        fit: '<path d="M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5"/>',
        pan: '<path d="M12 3v18M3 12h18M8 7l4-4 4 4M8 17l4 4 4-4M7 8l-4 4 4 4m10-8 4 4-4 4"/>',
        select: '<path d="M8 4H4v4m12-4h4v4M4 16v4h4m12-4v4h-4M12 4h1M4 12v1m16-1v1m-8 7h1"/>',
        left: '<path d="m14 6-6 6 6 6"/>',
        right: '<path d="m10 6 6 6-6 6"/>',
        download: '<path d="M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5"/>'
    };

    function icon(name: string): string {
        return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${icons[name] ?? ''}</svg>`;
    }

    function escape(value: unknown): string {
        return String(value ?? '').replace(/[&<>"']/g, match => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[match] ?? match);
    }

    function date(day: number): string { return new Date(day * dayMilliseconds).toISOString().slice(0, 10); }
    function dateLabel(day: number): string { return shortDate.format(new Date(day * dayMilliseconds)); }
    function formatted(value: number | null | undefined, digits?: number): string { return value === null || value === undefined || !Number.isFinite(value) ? '—' : digits === undefined ? number.format(value) : value.toFixed(digits); }
    function signed(value: number): string { return (value > 0 ? '+' : '') + formatted(value); }
    function finite(value: number | null | undefined): value is number { return typeof value === 'number' && Number.isFinite(value); }

    function extent(values: readonly number[], log = false): [number, number] {
        const valid = values.filter(value => Number.isFinite(value) && (!log || value > 0));
        if (!valid.length) return log ? [0.1, 10] : [0, 1];
        const min = Math.min(...valid); const max = Math.max(...valid);
        if (log) { const padding = Math.max((Math.log10(max) - Math.log10(min)) * 0.08, 0.12); return [10 ** (Math.log10(min) - padding), 10 ** (Math.log10(max) + padding)]; }
        const padding = Math.max((max - min) * 0.1, Math.abs(max) * 0.015, 0.05);
        const span = max - min + 2 * padding;
        const power = 10 ** Math.floor(Math.log10(span / 5));
        const fraction = span / 5 / power;
        const step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10) * power;
        return [Math.floor((min - padding) / step) * step, Math.ceil((max + padding) / step) * step];
    }

    class Explorer {
        private readonly root: HTMLElement;
        private readonly data: BiomarkerExplorerDataApi;
        private readonly athlete: BiomarkerExplorerAthlete;
        private readonly cohort: BiomarkerExplorerCohort;
        private readonly abort = new AbortController();
        private readonly allRecords = new Map<string, BiomarkerExplorerRecord>();
        private readonly plots: Plot[] = [];
        private readonly colorCache = new Map<string, string>();
        private readonly snapshotDate = new Date().toISOString().slice(0, 10);
        private view: View = 'time';
        private interaction: Interaction = 'inspect';
        private marker = 'AlbGL';
        private secondary = 'RdwPc';
        private extraMarkers: string[] = [];
        private selectedId = '';
        private comparisonId = '';
        private previewId = '';
        private range: [number, number] = [0, 1];
        private fullRange: [number, number] = [0, 1];
        private defaultRange: [number, number] = [0, 1];
        private fieldMode = false;
        private ageMin: number | null = null;
        private ageMax: number | null = null;
        private division = '';
        private logX = false;
        private logY = false;
        private standardized = false;
        private colorAge = false;
        private statistics = false;
        private phenoVisible = true;
        private bortzVisible = true;
        private brushIsPersonal = false;
        private timeProbe: number | null = null;
        private selectedReference: Set<string> | null = null;
        private brushedIds = new Set<string>();
        private contextBounds: Bounds | null = null;
        private relationshipBounds: Bounds | null = null;
        private tablePage = 0;
        private resizeObserver: ResizeObserver | null = null;
        private themeObserver: MutationObserver | null = null;
        private resizeFrame = 0;
        private renderFrame = 0;
        private disposed = false;
        private compact = false;
        private timeMetric: 'pheno' | 'bortz' | 'marker' = 'pheno';
        private cumulative = false;

        constructor(root: HTMLElement, raw: unknown, field: readonly unknown[], data: BiomarkerExplorerDataApi) {
            this.root = root;
            this.data = data;
            this.athlete = data.normalize(raw);
            this.cohort = data.latestCohort(field.map(value => data.normalize(value)), this.athlete.id);
            for (const record of [...this.athlete.records, ...this.cohort.records]) this.allRecords.set(record.id, record);
            const latest = this.athlete.records.at(-1);
            const first = this.athlete.records[0];
            if (latest && first) {
                this.selectedId = latest.id;
                this.comparisonId = this.athlete.records.at(-2)?.id ?? '';
                this.timeMetric = latest.pheno !== null ? 'pheno' : latest.bortz !== null ? 'bortz' : 'marker';
                this.marker = finite(latest.values.AlbGL) ? 'AlbGL' : data.markers.find(marker => finite(latest.values[marker.key]))?.key ?? 'AlbGL';
                this.fullRange = first.day === latest.day ? [first.day - 7, latest.day + 7] : [first.day - 3, latest.day + 3];
                const twoYearsEarlier = new Date(latest.day * dayMilliseconds);
                twoYearsEarlier.setUTCFullYear(twoYearsEarlier.getUTCFullYear() - 2);
                this.defaultRange = latest.day - first.day > 731 ? [twoYearsEarlier.getTime() / dayMilliseconds, latest.day + 3] : [...this.fullRange];
                this.range = [...this.defaultRange];
            }
            this.root.classList.add('biomarker-explorer');
            this.root.innerHTML = this.template();
            this.populateControls();
            this.bind();
            this.render();
            if (this.athlete.records.length <= 1) this.element<HTMLDetailsElement>('.be-measured').open = true;
            this.root.dataset.explorerReady = 'true';
            this.resizeObserver = new ResizeObserver(() => this.resize());
            this.resizeObserver.observe(this.root);
            this.themeObserver = new MutationObserver(() => this.scheduleRender());
            this.themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'data-theme'] });
        }

        private element<T extends HTMLElement = HTMLElement>(selector: string): T {
            const found = this.root.querySelector<T>(selector);
            if (!found) throw new Error(`Missing biomarker explorer control: ${selector}`);
            return found;
        }

        private template(): string {
            return `<div class="be-heading"><h3>Biomarkers</h3><button type="button" class="be-icon" data-action="reset" title="Reset exploration" aria-label="Reset exploration">${icon('reset')}</button></div>
                <div class="be-record-controls"><label class="be-control"><span>Blood test</span><select id="be-date" aria-label="Selected recorded test"></select></label><button type="button" data-action="compare-tests" class="be-compare-toggle" aria-label="Compare tests" aria-pressed="false">Compare</button></div>
                <label class="be-control be-compare-control" hidden><span>Compared with</span><select id="be-compare" aria-label="Baseline recorded test"></select></label>
                <div class="be-clock-summary" role="group" aria-label="Computed clock results"></div>
                <div class="be-tabs" role="tablist" aria-label="Biomarker exploration">
                    <button type="button" role="tab" id="be-tab-time" data-view="time" aria-controls="be-panel">History</button>
                    <button type="button" role="tab" id="be-tab-context" data-view="context" aria-controls="be-panel">Field position</button>
                    <button type="button" role="tab" id="be-tab-relationships" data-view="relationships" aria-controls="be-panel">Compare markers</button>
                </div>
                <div id="be-panel" role="tabpanel" aria-labelledby="be-tab-time">
                    <p class="be-question"></p>
                    <div class="be-history-focus" role="group" aria-label="History chart"><button type="button" data-action="clock-history">Clock results</button><button type="button" data-action="marker-history">Measured biomarkers</button></div>
                    <div class="be-picker-row"><label class="be-control"><span>Biomarker</span><select id="be-marker" aria-label="Biomarker"></select></label>
                        <label class="be-control be-secondary" hidden><span>Compare with</span><select id="be-secondary" aria-label="Relationship y-axis biomarker"></select></label>
                        <button type="button" class="be-icon be-add" data-action="add-marker" aria-label="Add a biomarker lane" title="Add a biomarker lane">${icon('plus')}</button>
                    </div>
                    <div class="be-mode" role="group" aria-label="Relationship observations" hidden><button type="button" data-action="field-mode">Across athletes</button><button type="button" data-action="own-mode">This athlete</button></div>
                    <div class="be-readout" aria-live="off"></div><div class="be-preview" hidden></div>
                    <div class="be-plot-area"><div class="be-plots"></div><p class="be-source"></p></div>
                    <div class="be-chart-recovery" hidden><p>Graphs are unavailable. All recorded values are available below.</p><button type="button" data-action="retry-charts">Retry graphs</button></div>
                    <div class="be-change-drivers"></div>
                    <details class="be-details be-measured"><summary>Measured results <span class="be-measured-count"></span></summary><div class="be-results-grid"></div></details>
                    <details class="be-details be-plot-options"><summary>Plot options</summary>
                    <div class="be-tools" role="group" aria-label="Plot controls">
                        <button type="button" class="be-icon" data-action="previous" aria-label="Previous recorded test">${icon('left')}</button><button type="button" class="be-icon" data-action="next" aria-label="Next recorded test">${icon('right')}</button>
                        <button type="button" data-action="inspect" aria-pressed="true">Inspect</button><button type="button" data-action="select" aria-pressed="false">${icon('select')} Select</button><button type="button" data-action="pan" aria-pressed="false">${icon('pan')} Pan</button>
                        <span class="be-tool-spacer"></span><button type="button" class="be-icon" data-action="zoom-in" aria-label="Zoom plot in" title="Zoom in">${icon('plus')}</button><button type="button" class="be-icon" data-action="zoom-out" aria-label="Zoom plot out" title="Zoom out">${icon('minus')}</button><button type="button" class="be-icon" data-action="fit" aria-label="Fit all observations" title="Fit all observations">${icon('fit')}</button>
                    </div>
                    <div class="be-time-controls"><div class="be-navigator" aria-label="Recorded test dates"><div class="be-rug"></div><div class="be-window"><button type="button" class="be-range-start" role="slider" aria-label="Start of visible date range"></button><button type="button" class="be-range-pan" aria-label="Move the visible date range"></button><button type="button" class="be-range-end" role="slider" aria-label="End of visible date range"></button></div></div>
                        <div class="be-date-range"><label>From<input type="date" id="be-from" aria-label="Visible date range start"></label><label>To<input type="date" id="be-to" aria-label="Visible date range end"></label><button type="button" data-action="all-dates">All dates</button></div>
                    </div>
                    <p class="be-gesture" id="be-gesture"></p>
                    </details>
                    <div class="be-selection" hidden><span class="be-selection-count"></span><button type="button" data-action="use-selection">Use as reference</button><button type="button" data-action="clear-selection">Clear</button></div>
                    <details class="be-details be-bounds"><summary>Select with values</summary><div class="be-bounds-grid"><label>X from<input type="number" step="any" id="be-x-from"></label><label>X to<input type="number" step="any" id="be-x-to"></label><label class="be-y-bound">Y from<input type="number" step="any" id="be-y-from"></label><label class="be-y-bound">Y to<input type="number" step="any" id="be-y-to"></label><button type="button" data-action="apply-bounds">Apply selection</button></div></details>
                    <div class="be-reference-selection" hidden><span>Selected reference panels</span><button type="button" data-action="whole-field">Whole field</button></div>
                    <details class="be-clock-details"><summary>Explain the clock change</summary><div class="be-clock-explanation"></div></details>
                    <details class="be-details be-advanced"><summary>Explore further</summary><div class="be-advanced-grid">
                        <fieldset class="be-cohort-controls"><legend>Reference panels</legend><div class="be-age-range"><label>Age at test, from<input type="number" id="be-age-min" min="0" max="120" step="1" placeholder="Any"></label><label>to<input type="number" id="be-age-max" min="0" max="120" step="1" placeholder="Any"></label></div><label class="be-control"><span>Competition division</span><select id="be-division" aria-label="Reference competition division"></select></label><p>One latest submitted panel per other athlete. Division is a competition category.</p></fieldset>
                        <fieldset><legend>Display</legend><label class="be-check"><input type="checkbox" id="be-log-x"> Log value scale</label><label class="be-check be-log-y-control" hidden><input type="checkbox" id="be-log-y"> Log y-axis scale</label><label class="be-check be-standardized-control"><input type="checkbox" id="be-standardized"> Compare personal patterns (SD)</label><p class="be-scale-explanation"></p><label class="be-check be-age-color-control" hidden><input type="checkbox" id="be-color-age"> Color reference points by age at test</label><label class="be-check be-cumulative-control" hidden><input type="checkbox" id="be-cumulative"> Show cumulative distribution</label></fieldset>
                        <fieldset class="be-statistics-control" hidden><legend>Describe this field</legend><label class="be-check"><input type="checkbox" id="be-statistics"> Summarize rank association</label><p class="be-association"></p></fieldset>
                    </div></details>
                    <details class="be-details be-data"><summary>Data &amp; export <span class="be-data-count"></span></summary><div class="be-data-actions"><button type="button" data-action="export">${icon('download')} Download CSV</button><span class="be-table-caption"></span></div><div class="be-table-scroll" tabindex="0" role="region" aria-label="Observation data table"><table class="be-table"><caption class="visually-hidden">Recorded values and computed clock results</caption><thead></thead><tbody></tbody></table></div><div class="be-pagination"><button type="button" data-action="table-previous">Previous page</button><span></span><button type="button" data-action="table-next">Next page</button></div></details>
                    <details class="be-details be-reading"><summary>How to read this</summary><div class="be-reading-content"></div></details>
                    <span class="visually-hidden be-announcement" role="status" aria-live="polite" aria-atomic="true"></span>
                </div>`;
        }

        private populateControls(): void {
            const options = this.data.markers.filter(marker => [...this.allRecords.values()].some(record => finite(record.values[marker.key])));
            const groups = [...new Set(options.map(marker => marker.domain))];
            const html = groups.map(group => `<optgroup label="${escape(group)}">${options.filter(marker => marker.domain === group).map(marker => `<option value="${marker.key}">${escape(marker.label)} (${escape(marker.unit)})</option>`).join('')}</optgroup>`).join('');
            this.element<HTMLSelectElement>('#be-marker').innerHTML = html;
            this.element<HTMLSelectElement>('#be-secondary').innerHTML = html;
            if (this.marker === this.secondary) this.secondary = this.marker === 'AlbGL' ? 'RdwPc' : 'AlbGL';
            const records = this.athlete.records;
            const recordOptions = records.map(record => {
                const sameDay = records.filter(item => item.day === record.day);
                const label = `${dateLabel(record.day)}${sameDay.length > 1 ? ` · panel ${sameDay.indexOf(record) + 1} of ${sameDay.length}` : ''}`;
                return `<option value="${escape(record.id)}">${escape(label)}</option>`;
            }).join('');
            this.element<HTMLSelectElement>('#be-date').innerHTML = recordOptions;
            this.element<HTMLSelectElement>('#be-compare').innerHTML = recordOptions;
            this.element<HTMLSelectElement>('#be-division').innerHTML = '<option value="">All divisions</option>' + [...new Set(this.cohort.records.map(record => record.division).filter(Boolean))].sort().map(division => `<option>${escape(division)}</option>`).join('');
        }

        private bind(): void {
            const signal = this.abort.signal;
            this.root.addEventListener('click', event => {
                const target = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-action], [data-view], [data-record], [data-marker-remove], [data-marker]') : null;
                if (!target || !this.root.contains(target)) return;
                if (target.dataset.view) { this.view = target.dataset.view as View; this.interaction = 'inspect'; this.previewId = ''; this.tablePage = 0; this.render(); return; }
                if (target.dataset.record) { this.pickRecord(target.dataset.record); return; }
                if (target.dataset.marker) {
                    if (!this.data.markers.some(marker => marker.key === target.dataset.marker)) return;
                    this.marker = target.dataset.marker; this.view = 'time'; this.timeMetric = 'marker'; this.logX = false;
                    this.extraMarkers = this.extraMarkers.filter(key => key !== this.marker);
                    this.contextBounds = null; this.relationshipBounds = null; this.brushedIds.clear();
                    if (this.secondary === this.marker) this.secondary = this.data.markers.find(marker => marker.key !== this.marker)?.key ?? '';
                    this.render(); this.element('.be-history-focus').scrollIntoView({ block: 'nearest', behavior: 'auto' });
                    this.element<HTMLSelectElement>('#be-marker').focus({ preventScroll: true }); return;
                }
                if (target.dataset.markerRemove) { this.extraMarkers = this.extraMarkers.filter(key => key !== target.dataset.markerRemove); this.render(); this.element<HTMLButtonElement>('[data-action="add-marker"]').focus({ preventScroll: true }); return; }
                this.action(target.dataset.action ?? '');
            }, { signal });
            this.root.addEventListener('change', event => {
                const target = event.target;
                if (!(target instanceof HTMLInputElement || target instanceof HTMLSelectElement)) return;
                if (target.dataset.extraIndex !== undefined) {
                    const index = Number(target.dataset.extraIndex);
                    if (Number.isInteger(index) && index >= 0 && index < this.extraMarkers.length && this.data.markers.some(marker => marker.key === target.value) && target.value !== this.marker && !this.extraMarkers.some((key, item) => item !== index && key === target.value)) this.extraMarkers[index] = target.value;
                    this.render(); return;
                }
                switch (target.id) {
                    case 'be-marker': case 'be-secondary':
                        if (!this.data.markers.some(marker => marker.key === target.value) || target.id === 'be-secondary' && target.value === this.marker) {
                            target.value = target.id === 'be-marker' ? this.marker : this.secondary; return;
                        }
                        if (target.id === 'be-secondary') { this.secondary = target.value; this.logY = false; this.relationshipBounds = null; this.brushedIds.clear(); break; }
                        this.marker = target.value; this.logX = false; this.contextBounds = null; this.relationshipBounds = null; this.brushedIds.clear(); this.extraMarkers = this.extraMarkers.filter(key => key !== this.marker); if (this.secondary === this.marker) this.secondary = this.data.markers.find(marker => marker.key !== this.marker)?.key ?? ''; break;
                    case 'be-date': this.pickRecord(target.value); return;
                    case 'be-compare': this.comparisonId = target.value; break;
                    case 'be-from': case 'be-to': this.setDateRange(); return;
                    case 'be-age-min': case 'be-age-max': this.setAgeRange(); return;
                    case 'be-division': this.division = target.value; this.contextBounds = null; this.relationshipBounds = null; this.brushedIds.clear(); break;
                    case 'be-log-x': this.logX = (target as HTMLInputElement).checked; this.contextBounds = null; this.relationshipBounds = null; break;
                    case 'be-log-y': this.logY = (target as HTMLInputElement).checked; this.relationshipBounds = null; break;
                    case 'be-standardized': this.standardized = (target as HTMLInputElement).checked; break;
                    case 'be-color-age': this.colorAge = (target as HTMLInputElement).checked; break;
                    case 'be-statistics': this.statistics = (target as HTMLInputElement).checked; break;
                    case 'be-cumulative': this.cumulative = (target as HTMLInputElement).checked; this.contextBounds = null; break;
                    default: return;
                }
                this.tablePage = 0; this.render();
            }, { signal });
            this.root.addEventListener('keydown', event => {
                const target = event.target;
                if (target instanceof HTMLElement && target.dataset.view && ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
                    event.preventDefault();
                    const views: readonly View[] = ['time', 'context', 'relationships'];
                    const index = event.key === 'Home' ? 0 : event.key === 'End' ? 2 : (views.indexOf(this.view) + (event.key === 'ArrowRight' ? 1 : 2)) % 3;
                    this.view = views[index] ?? 'time'; this.interaction = 'inspect'; this.render(); this.element<HTMLButtonElement>(`[data-view="${this.view}"]`).focus();
                }
            }, { signal });
            this.element<HTMLDetailsElement>('.be-data').addEventListener('toggle', () => { if (this.element<HTMLDetailsElement>('.be-data').open) this.renderTable(); }, { signal });
            this.bindNavigator();
        }

        private selected(): BiomarkerExplorerRecord | undefined { return this.allRecords.get(this.selectedId); }
        private markerInfo(key = this.marker): BiomarkerExplorerMarker { return this.data.markers.find(marker => marker.key === key) ?? this.data.markers[0]!; }
        private ownVisible(): readonly BiomarkerExplorerRecord[] { return this.athlete.records.filter(record => record.day >= this.range[0] && record.day <= this.range[1]); }
        private reference(): readonly BiomarkerExplorerRecord[] {
            return this.cohort.records.filter(record => (!this.selectedReference || this.selectedReference.has(record.id)) && (!this.division || record.division === this.division) && (this.ageMin === null || record.age !== null && record.age >= this.ageMin) && (this.ageMax === null || record.age !== null && record.age <= this.ageMax));
        }

        private action(action: string): void {
            switch (action) {
                case 'reset': this.reset(); return;
                case 'inspect': case 'select': case 'pan': this.interaction = action; break;
                case 'zoom-in': this.zoom(0.65); return;
                case 'zoom-out': this.zoom(1.55); return;
                case 'fit': this.contextBounds = null; this.relationshipBounds = null; if (this.view === 'time') this.range = [...this.fullRange]; break;
                case 'all-dates': this.range = [...this.fullRange]; break;
                case 'field-mode': this.fieldMode = true; this.relationshipBounds = null; this.previewId = ''; break;
                case 'own-mode': this.fieldMode = false; this.relationshipBounds = null; this.previewId = ''; this.brushedIds.clear(); this.statistics = false; break;
                case 'previous': this.moveRecord(-1); return;
                case 'next': this.moveRecord(1); return;
                case 'compare-tests': {
                    const index = this.athlete.records.findIndex(record => record.id === this.selectedId);
                    this.comparisonId = this.comparisonId ? '' : this.athlete.records[index > 0 ? index - 1 : 1]?.id ?? ''; break;
                }
                case 'focus-pheno': case 'focus-bortz': this.timeMetric = action === 'focus-pheno' ? 'pheno' : 'bortz'; this.view = 'time'; this.interaction = 'inspect'; break;
                case 'clock-history': this.timeMetric = this.athlete.records.some(record => record.pheno !== null) ? 'pheno' : 'bortz'; break;
                case 'marker-history': this.timeMetric = 'marker'; break;
                case 'use-selection': if (!this.brushIsPersonal && this.brushedIds.size) { this.selectedReference = new Set(this.brushedIds); this.brushedIds.clear(); this.contextBounds = null; this.relationshipBounds = null; } break;
                case 'clear-selection': this.brushedIds.clear(); break;
                case 'apply-bounds': this.applyBounds(); return;
                case 'whole-field': this.selectedReference = null; this.brushedIds.clear(); this.contextBounds = null; this.relationshipBounds = null; break;
                case 'add-marker': { const marker = this.data.markers.find(marker => marker.key !== this.marker && !this.extraMarkers.includes(marker.key) && this.athlete.records.some(record => finite(record.values[marker.key]))); if (marker && this.extraMarkers.length < 2) this.extraMarkers.push(marker.key); break; }
                case 'close-preview': this.previewId = ''; break;
                case 'toggle-pheno': this.phenoVisible = !this.phenoVisible; break;
                case 'toggle-bortz': this.bortzVisible = !this.bortzVisible; break;
                case 'export': this.exportCsv(); return;
                case 'show-data': this.element<HTMLDetailsElement>('.be-data').open = true; this.renderTable(); this.element('.be-data').scrollIntoView({ block: 'nearest', behavior: 'auto' }); return;
                case 'table-previous': this.tablePage = Math.max(0, this.tablePage - 1); this.renderTable(); return;
                case 'table-next': this.tablePage++; this.renderTable(); return;
                case 'retry-charts': {
                    const loader = Reflect.get(window, 'ensureChartJs') as (() => Promise<boolean>) | undefined;
                    if (loader) void loader().then(() => { if (!this.disposed) this.render(); });
                    return;
                }
                default: return;
            }
            this.render();
        }

        private reset(): void {
            const latest = this.athlete.records.at(-1);
            this.marker = latest && finite(latest.values.AlbGL) ? 'AlbGL' : this.data.markers.find(marker => latest && finite(latest.values[marker.key]))?.key ?? 'AlbGL';
            this.secondary = 'RdwPc'; if (this.marker === this.secondary) this.secondary = 'AlbGL';
            this.selectedId = latest?.id ?? ''; this.comparisonId = this.athlete.records.at(-2)?.id ?? ''; this.previewId = ''; this.extraMarkers = [];
            this.timeMetric = latest && latest.pheno !== null ? 'pheno' : latest && latest.bortz !== null ? 'bortz' : 'marker'; this.cumulative = false;
            this.range = [...this.defaultRange]; this.ageMin = null; this.ageMax = null; this.division = ''; this.fieldMode = false;
            this.logX = false; this.logY = false; this.standardized = false; this.colorAge = false; this.statistics = false;
            this.phenoVisible = true; this.bortzVisible = true; this.brushIsPersonal = false;
            this.selectedReference = null; this.brushedIds.clear(); this.contextBounds = null; this.relationshipBounds = null;
            this.interaction = 'inspect'; this.tablePage = 0;
            this.element<HTMLInputElement>('#be-age-min').value = ''; this.element<HTMLInputElement>('#be-age-max').value = '';
            this.render(); this.announce('Exploration reset. Latest recorded test selected.');
        }

        private pickRecord(id: string): void {
            const record = this.allRecords.get(id);
            if (!record) return;
            if (record.athleteId !== this.athlete.id) { this.previewId = id; this.renderReadout(); this.announce(`Previewing ${record.athleteName}, ${dateLabel(record.day)}.`); return; }
            this.previewId = ''; this.selectedId = id;
            if (record.day < this.range[0] || record.day > this.range[1]) this.range = this.clampRange([record.day - (this.range[1] - this.range[0]) / 2, record.day + (this.range[1] - this.range[0]) / 2]);
            if (this.comparisonId === id) this.comparisonId = '';
            this.render(); this.announce(`${dateLabel(record.day)} selected. ${this.markerInfo().label} ${formatted(record.values[this.marker])} ${this.markerInfo().unit}.`);
        }

        private moveRecord(delta: number): void {
            const index = this.athlete.records.findIndex(record => record.id === this.selectedId);
            const record = this.athlete.records[Math.max(0, Math.min(this.athlete.records.length - 1, index + delta))];
            if (record) this.pickRecord(record.id);
        }

        private setDateRange(): void {
            const from = this.element<HTMLInputElement>('#be-from'); const to = this.element<HTMLInputElement>('#be-to');
            const a = this.data.dateDay(from.value); const b = this.data.dateDay(to.value);
            const invalid = a === null || b === null || a > b;
            from.setCustomValidity(invalid ? 'Choose a valid date range, with From before To.' : '');
            if (invalid) { from.reportValidity(); return; }
            this.range = this.clampRange(a === b ? [a - 0.5, b + 0.5] : [a, b]); this.render();
        }

        private setAgeRange(): void {
            const min = this.element<HTMLInputElement>('#be-age-min'); const max = this.element<HTMLInputElement>('#be-age-max');
            min.setCustomValidity('');
            const a = min.value === '' ? null : min.valueAsNumber; const b = max.value === '' ? null : max.valueAsNumber;
            const invalid = !min.validity.valid || !max.validity.valid || a !== null && b !== null && a > b;
            min.setCustomValidity(invalid ? 'Choose an age range from younger to older.' : '');
            if (invalid) { min.reportValidity(); return; }
            this.ageMin = a; this.ageMax = b; this.brushedIds.clear(); this.contextBounds = null; this.relationshipBounds = null; this.render();
        }

        private clampRange(range: readonly [number, number]): [number, number] {
            const width = Math.min(this.fullRange[1] - this.fullRange[0], Math.max(1, range[1] - range[0]));
            const left = Math.max(this.fullRange[0], Math.min(this.fullRange[1] - width, range[0]));
            return [left, left + width];
        }

        private announce(text: string): void { this.element('.be-announcement').textContent = text; }
        private scheduleRender(): void { if (!this.renderFrame && !this.disposed) this.renderFrame = requestAnimationFrame(() => { this.renderFrame = 0; if (!this.disposed) this.render(); }); }

        private render(): void {
            if (this.disposed) return;
            if (this.view === 'relationships' && this.brushedIds.size && !this.brushIsPersonal) this.fieldMode = true;
            const focusedPlot = this.plots.find(plot => plot.canvas === document.activeElement)?.key;
            const focusedAction = document.activeElement instanceof HTMLElement && this.root.contains(document.activeElement) ? document.activeElement.dataset.action : undefined;
            const focusedExtra = document.activeElement instanceof HTMLElement && this.root.contains(document.activeElement) ? document.activeElement.dataset.extraIndex : undefined;
            this.colorCache.clear(); this.compact = this.root.clientWidth < 440;
            this.root.dataset.activeView = this.view;
            this.root.dataset.selectedRecord = this.selectedId;
            this.root.dataset.primaryMarker = this.marker;
            this.element('#be-panel').setAttribute('aria-labelledby', `be-tab-${this.view}`);
            this.root.querySelectorAll<HTMLButtonElement>('[data-view]').forEach(tab => { const selected = tab.dataset.view === this.view; tab.setAttribute('aria-selected', String(selected)); tab.tabIndex = selected ? 0 : -1; });
            this.element('.be-question').textContent = this.view === 'time' ? '' : this.view === 'context' ? 'Compare this measured value with other LWC athletes.' : this.fieldMode ? 'Two measured values from each other athlete’s latest panel.' : 'Two measured values from this athlete’s dated blood tests.';
            this.element('.be-question').hidden = this.view === 'time';
            this.element('.be-history-focus').hidden = this.view !== 'time';
            this.element('[data-action="clock-history"]').setAttribute('aria-pressed', String(this.timeMetric !== 'marker'));
            this.element('[data-action="marker-history"]').setAttribute('aria-pressed', String(this.timeMetric === 'marker'));
            this.element('.be-picker-row').hidden = this.view === 'time' && this.timeMetric !== 'marker';
            this.element('.be-measured').hidden = this.view !== 'time';
            this.element('.be-cumulative-control').hidden = this.view !== 'context';
            this.element<HTMLInputElement>('#be-cumulative').checked = this.cumulative;
            this.element<HTMLSelectElement>('#be-marker').value = this.marker;
            const secondary = this.element<HTMLSelectElement>('#be-secondary'); secondary.value = this.secondary;
            for (const option of secondary.options) option.disabled = option.value === this.marker;
            this.element('.be-secondary').hidden = this.view !== 'relationships';
            this.element<HTMLButtonElement>('.be-add').hidden = this.view !== 'time';
            this.element<HTMLButtonElement>('.be-add').disabled = this.extraMarkers.length >= 2 || !this.data.markers.some(marker => marker.key !== this.marker && !this.extraMarkers.includes(marker.key) && this.athlete.records.some(record => finite(record.values[marker.key])));
            this.element('.be-mode').hidden = this.view !== 'relationships';
            this.element('[data-action="field-mode"]').setAttribute('aria-pressed', String(this.fieldMode));
            this.element('[data-action="own-mode"]').setAttribute('aria-pressed', String(!this.fieldMode));
            for (const mode of ['inspect', 'select', 'pan']) this.element(`[data-action="${mode}"]`).setAttribute('aria-pressed', String(this.interaction === mode));
            this.element('[data-action="inspect"]').hidden = this.interaction === 'inspect';
            this.element('[data-action="select"]').hidden = this.view === 'time';
            this.element('[data-action="pan"]').hidden = this.view === 'time';
            this.element('.be-time-controls').hidden = this.view !== 'time' && !(this.view === 'relationships' && !this.fieldMode);
            this.element<HTMLSelectElement>('#be-date').value = this.selectedId;
            this.element<HTMLSelectElement>('#be-compare').value = this.comparisonId;
            for (const option of this.element<HTMLSelectElement>('#be-compare').options) option.disabled = option.value === this.selectedId;
            const index = this.athlete.records.findIndex(record => record.id === this.selectedId);
            this.element<HTMLButtonElement>('[data-action="previous"]').disabled = index <= 0;
            this.element<HTMLButtonElement>('[data-action="next"]').disabled = index < 0 || index >= this.athlete.records.length - 1;
            this.element<HTMLButtonElement>('[data-action="compare-tests"]').disabled = this.athlete.records.length < 2;
            this.element('[data-action="compare-tests"]').setAttribute('aria-pressed', String(!!this.comparisonId));
            this.element('.be-compare-control').hidden = !this.comparisonId || this.view !== 'time';
            this.element('[data-action="compare-tests"]').hidden = this.view !== 'time';
            this.element<HTMLSelectElement>('#be-division').value = this.division;
            const canStandardize = [this.marker, ...this.extraMarkers].every(key => this.data.standardization(this.athlete.records.map(record => record.values[key] ?? NaN)) !== null);
            if (!canStandardize || !this.extraMarkers.length) this.standardized = false;
            const sd = this.element<HTMLInputElement>('#be-standardized'); sd.checked = this.standardized; sd.disabled = !canStandardize || !this.extraMarkers.length;
            this.element('.be-standardized-control').hidden = this.view !== 'time';
            this.element<HTMLInputElement>('#be-log-x').checked = this.logX;
            this.element<HTMLInputElement>('#be-log-y').checked = this.logY;
            this.element<HTMLInputElement>('#be-log-x').disabled = this.view === 'time' && this.standardized;
            this.element('.be-log-y-control').hidden = this.view !== 'relationships';
            this.element('.be-cohort-controls').hidden = this.view === 'time' || this.view === 'relationships' && !this.fieldMode;
            this.element('.be-age-color-control').hidden = this.view !== 'relationships' || !this.fieldMode;
            this.element<HTMLInputElement>('#be-color-age').checked = this.colorAge;
            this.element('.be-statistics-control').hidden = this.view !== 'relationships' || !this.fieldMode;
            this.element<HTMLInputElement>('#be-statistics').checked = this.statistics;
            this.element('.be-scale-explanation').textContent = this.standardized && this.view === 'time' ? 'Each lane uses this athlete’s full observed history: mean 0, sample SD 1. The basis stays fixed when you move the date window. Raw values remain in the readout and data table; assay history is not recorded.' : this.logX || this.logY ? 'Log spacing represents equal fold changes. Nonpositive values stay in the data table. Scaling does not change the clock calculation.' : this.view === 'time' ? 'Personal pattern comparison needs two lanes, each with at least five values and nonzero spread. It describes observed variation, not population variation.' : 'Values retain their recorded canonical units. No population standardization is applied.';
            this.renderNavigator();
            this.renderResults();
            this.renderCharts();
            this.renderReadout();
            this.element('.be-readout').hidden = this.view === 'time' && this.timeMetric !== 'marker';
            this.renderSelection();
            this.renderAssociation();
            this.renderReading();
            this.element('.be-data-count').textContent = `(${this.tableRecords().length})`;
            if (this.element<HTMLDetailsElement>('.be-data').open) this.renderTable();
            this.element('.be-gesture').textContent = this.interaction === 'select' ? 'Drag a region to select its records. Numeric bounds below offer the same selection.' : this.interaction === 'pan' ? 'Drag to move the plot.' : this.view === 'time' ? 'Select a recorded point to see that test. Dates are spaced by the actual time between tests.' : 'Select a point to see its measured values.';
            this.element('[data-action="inspect"]').textContent = this.interaction === 'inspect' ? 'Inspect' : 'Done';
            if (focusedPlot) this.plots.find(plot => plot.key === focusedPlot)?.canvas.focus({ preventScroll: true });
            else if (focusedExtra !== undefined) this.root.querySelector<HTMLSelectElement>(`select[data-extra-index="${CSS.escape(focusedExtra)}"]`)?.focus({ preventScroll: true });
            else if (focusedAction) this.root.querySelector<HTMLElement>(`[data-action="${CSS.escape(focusedAction)}"]`)?.focus({ preventScroll: true });
        }

        private renderResults(): void {
            const record = this.selected(); const before = this.allRecords.get(this.comparisonId);
            const summary = this.element('.be-clock-summary');
            summary.hidden = this.view !== 'time' || this.timeMetric === 'marker';
            this.element<HTMLButtonElement>('[data-action="clock-history"]').disabled = !this.athlete.records.some(item => item.pheno !== null || item.bortz !== null);
            summary.innerHTML = (['pheno', 'bortz'] as const).filter(clock => this.athlete.records.some(item => item[clock] !== null)).map(clock => {
                const reduction = record && record[clock] !== null && record.age !== null ? record.age - record[clock] : null;
                const prior = before && before[clock] !== null && before.age !== null ? before.age - before[clock] : null;
                const delta = reduction !== null && prior !== null ? reduction - prior : null;
                return `<button type="button" class="be-clock-tile" data-action="focus-${clock}" aria-pressed="${this.view === 'time' && this.timeMetric === clock}"><span>${clock}</span><strong>${formatted(reduction, 1)}<small> y</small></strong><span>computed age reduction</span><small>${delta !== null ? `${signed(delta)} y vs ${escape(dateLabel(before!.day))}` : reduction === null ? 'Incomplete eligible panel' : `Biological age ${formatted(record?.[clock], 1)} y`}</small></button>`;
            }).join('');
            const measured = this.data.markers.filter(marker => record && finite(record.values[marker.key]));
            this.element('.be-measured-count').textContent = `(${measured.length})`;
            this.element('.be-results-grid').innerHTML = this.data.markers.filter(marker => this.athlete.records.some(item => finite(item.values[marker.key]))).map(marker => {
                const value = record?.values[marker.key]; const baseline = before?.values[marker.key];
                const delta = finite(value) && finite(baseline) ? value - baseline : null;
                return `<button type="button" class="be-result" data-marker="${marker.key}" aria-label="See ${escape(marker.label)} history"><span class="be-result-name">${escape(marker.label)}<small>${escape(marker.domain)}</small></span><span class="be-result-value">${formatted(value)}<small>${escape(marker.unit)}</small></span><span class="be-result-change">${delta === null ? '—' : signed(delta)}<small>${delta === null ? 'no comparison' : 'change'}</small></span></button>`;
            }).join('');
            const drivers = this.element('.be-change-drivers');
            drivers.hidden = this.view !== 'time' || this.timeMetric === 'marker' || !record || !before;
            if (drivers.hidden || !record || !before || this.timeMetric === 'marker') return;
            const clock = this.timeMetric; const terms = this.data.clockChange(before, record, clock);
            if (!terms) { drivers.innerHTML = `<p class="be-offscale">${clock} changes cannot be calculated between these tests: both need complete eligible panels.</p>`; return; }
            const ordered = terms.filter(row => Math.abs(row.change) > 1e-6).sort((a, b) => Math.abs(b.change) - Math.abs(a.change));
            const shown = ordered.slice(0, 5);
            if (ordered.length > 5) shown.push({ key: 'other', label: `${ordered.length - 5} other model terms`, change: ordered.slice(5).reduce((sum, row) => sum + row.change, 0), a: 0, b: 0, unit: '', capped: false, transformed: false });
            const largest = Math.max(.001, ...ordered.map(row => Math.abs(row.change)));
            drivers.innerHTML = `<div class="be-drivers-heading"><strong>What changed in ${clock}?</strong><span>${escape(dateLabel(before.day))} → ${escape(dateLabel(record.day))}</span></div><p class="be-driver-caption">Changes in the calculated age reduction, in years</p><div class="be-driver-list">${shown.map(row => `<${this.data.markers.some(marker => marker.key === row.key) ? 'button type="button" data-marker="' + row.key + '"' : 'div'} class="be-driver"><span>${escape(row.label)}<small>${row.key === 'other' ? 'See the complete clock breakdown' : `${formatted(row.a)} → ${formatted(row.b)} ${escape(row.unit)}${row.capped ? ' · scoring cap' : ''}`}</small></span><span class="be-driver-track"><i style="width:${Math.min(48, Math.abs(row.change) / largest * 48)}%;${row.change <= 0 ? 'left:50%' : 'right:50%'}"></i></span><strong>${signed(-row.change)} y</strong></${this.data.markers.some(marker => marker.key === row.key) ? 'button' : 'div'}>`).join('') || '<p class="be-offscale">The calculated age reduction is unchanged.</p>'}</div><p class="be-driver-note">Model arithmetic; a change in the score does not establish a health effect.</p>`;
        }

        private renderCharts(): void {
            this.destroyCharts();
            const container = this.element('.be-plots'); container.replaceChildren();
            const selected = this.selected();
            const marker = this.markerInfo();
            const reference = this.reference();
            const visible = this.ownVisible();
            this.element('.be-source').textContent = this.view === 'time' ? `${visible.length} of ${this.athlete.records.length} recorded tests${selected ? ` · selected ${dateLabel(selected.day)}${selected.day < this.range[0] || selected.day > this.range[1] ? ' (outside this period)' : ''}` : ''}` : this.view === 'relationships' && !this.fieldMode ? `${this.data.samePanelPairs(visible, this.marker, this.secondary).length} paired tests in this period · same submitted record/date · 0-day pairing` : this.referenceCaption(reference);
            if (!this.athlete.records.length && this.view === 'time') { container.innerHTML = '<p class="be-empty">No dated numeric measurements are available.</p>'; return; }
            if (this.view === 'time') {
                const clocks: Record<string, unknown>[] = [];
                const clockPoints: Point[] = [];
                for (const clock of ['pheno', 'bortz'] as const) {
                    if (this.timeMetric !== clock) continue;
                    if (!visible.some(record => record[clock] !== null)) continue;
                    const points = this.data.series(this.athlete.records, record => record[clock] !== null && record.age !== null ? record.age - record[clock] : null);
                    clockPoints.push(...points);
                    clocks.push({ ...this.dataset(clock === 'pheno' ? 'pheno age' : 'bortz age', points, clock === 'pheno' ? this.color('--athlete-modal-chart-pheno', '#87e5e5') : this.color('--athlete-modal-chart-bortz', '#9ae8b2'), 'rectRot'), hidden: clock === 'pheno' ? !this.phenoVisible : !this.bortzVisible });
                }
                const createClocks = (): void => {
                    const count = clockPoints.filter(point => point.recordId && finite(point.y) && point.x >= this.range[0] && point.x <= this.range[1]).length;
                    if (count === 1) container.insertAdjacentHTML('beforeend', '<p class="be-single-test">One complete clock result in this period. A history appears after another eligible test.</p>');
                    else if (clocks.length) this.createPlot('clocks', `${this.timeMetric} history`, 'Age reduction · years', clocks, clockPoints, false, false, true);
                    else container.insertAdjacentHTML('beforeend', '<p class="be-clock-missing">No complete clock panel in this period. Measured values remain available.</p>');
                };
                if (this.timeMetric !== 'marker') createClocks();
                for (const key of this.timeMetric === 'marker' ? [this.marker, ...this.extraMarkers] : []) {
                    const info = this.markerInfo(key);
                    const basis = this.standardized ? this.data.standardization(this.athlete.records.map(record => record.values[key] ?? NaN)) : null;
                    const points = this.data.series(this.athlete.records, record => { const value = record.values[key]; return finite(value) && (!this.logX || this.standardized || value > 0) ? basis ? (value - basis.mean) / basis.sd : value : null; });
                    const dataset = this.dataset(info.label, points, this.color('--athlete-modal-accent', '#7bdde3'));
                    if (points.filter(point => point.recordId && finite(point.y) && point.x >= this.range[0] && point.x <= this.range[1]).length < 2) { container.insertAdjacentHTML('beforeend', `<p class="be-single-test">${escape(info.label)} has fewer than two measured values in this period. The recorded result is shown above.</p>`); continue; }
                    const plot = this.createPlot(key, `${info.label} · ${basis ? 'personal SD' : info.unit}`, basis ? 'SD from this athlete’s observed mean' : info.unit, [dataset], points, false, this.logX && !basis, true);
                    if (key !== this.marker) {
                        const index = this.extraMarkers.indexOf(key);
                        const label = document.createElement('label'); label.className = 'be-lane-picker';
                        const caption = document.createElement('span'); caption.className = 'visually-hidden'; caption.textContent = 'Additional biomarker';
                        const select = document.createElement('select'); select.dataset.extraIndex = String(index); select.setAttribute('aria-label', `Additional biomarker ${index + 1}`);
                        for (const item of this.data.markers.filter(item => this.athlete.records.some(record => finite(record.values[item.key])))) {
                            const option = new Option(`${item.label} (${item.unit})`, item.key, false, item.key === key);
                            option.disabled = item.key === this.marker || this.extraMarkers.some(extra => extra !== key && extra === item.key);
                            select.append(option);
                        }
                        label.append(caption, select);
                        const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'be-icon'; remove.dataset.markerRemove = key;
                        remove.setAttribute('aria-label', `Remove ${info.label} lane`); remove.innerHTML = icon('minus');
                        plot.figure.querySelector('.be-figure-heading')?.append(label, remove);
                    }
                    const offscale = this.logX && !basis ? visible.filter(record => finite(record.values[key]) && (record.values[key] ?? 0) <= 0).length : 0;
                    if (offscale) plot.figure.insertAdjacentHTML('beforeend', `<p class="be-offscale">${offscale} nonpositive value${offscale === 1 ? '' : 's'} available in the data table.</p>`);
                    if (basis) {
                        const observed = this.athlete.records.filter(record => finite(record.values[key]));
                        plot.figure.insertAdjacentHTML('beforeend', `<p class="be-offscale">Fixed basis: ${basis.n} values, ${escape(dateLabel(observed[0]!.day))}–${escape(dateLabel(observed.at(-1)!.day))}; mean ${formatted(basis.mean)} ${escape(info.unit)}, sample SD ${formatted(basis.sd)} ${escape(info.unit)}.</p>`);
                    }
                }
            } else if (this.view === 'context') {
                const records = reference.filter(record => finite(record.values[this.marker]));
                const values = records.map(record => record.values[this.marker] ?? NaN);
                const curve = this.data.ecdf(values).filter(point => !this.logX || point.x > 0);
                const bounds = extent([...values, selected?.values[this.marker] ?? NaN], this.logX);
                const stacks = new Map<number, number>();
                const points = records.filter(record => !this.logX || (record.values[this.marker] ?? 0) > 0).map(record => {
                    const x = record.values[this.marker] ?? NaN;
                    const bin = Math.round((x - bounds[0]) / (bounds[1] - bounds[0]) * 70);
                    const stack = stacks.get(bin) ?? 0; stacks.set(bin, stack + 1);
                    return { x, y: this.cumulative ? 2 : 50 + ((stack % 7) - 3) * 9, recordId: record.id };
                });
                const first = curve[0]; const last = curve.at(-1);
                const curvePoints: Point[] = first && last ? [{ x: bounds[0], y: first.y }, ...curve, { x: bounds[1], y: 100 }] : [];
                const datasets: Record<string, unknown>[] = [{ label: 'Share at or below', data: curvePoints, hidden: !this.cumulative, showLine: true, borderColor: this.color('--athlete-modal-muted', '#b8c5cf'), borderWidth: 2, pointRadius: 0, tension: 0, fill: false }, { label: 'Submitted values', data: points, showLine: false, pointRadius: 3, pointHoverRadius: 4, pointBackgroundColor: points.map(point => this.brushedIds.has(point.recordId) ? this.color('--athlete-modal-accent', '#7bdde3') : 'rgba(158,179,194,.55)'), pointBorderWidth: 0 }];
                const plot = this.createPlot('context', 'Other LWC athletes · one dot per submitted panel', 'Share of panels at or below · %', datasets, points, this.logX, false, false);
                plot.figure.dataset.cumulative = String(this.cumulative);
                const n = records.length;
                this.element('.be-source').textContent = `${this.referenceCaption(records)} · ${reference.length - n} missing ${marker.label.toLowerCase()}`;
                if (!n) plot.figure.insertAdjacentHTML('beforeend', '<p class="be-empty">No reference panels contain this marker. Change the marker or reference filters.</p>');
                const offscale = this.logX ? values.filter(value => value <= 0).length : 0;
                if (offscale) plot.figure.insertAdjacentHTML('beforeend', `<p class="be-offscale">${offscale} nonpositive reference value${offscale === 1 ? '' : 's'} omitted from log spacing; included in counts and the data table.</p>`);
            } else {
                const source = this.fieldMode ? reference : visible;
                const pairs = this.data.samePanelPairs(source, this.marker, this.secondary);
                if (!this.fieldMode && pairs.length < 2) {
                    container.innerHTML = `<p class="be-single-test">${pairs.length === 1 ? 'One paired test is available' : 'No paired tests are available'} in this period. Both measurements must be in the same panel; a personal comparison graph needs at least two such tests.</p>`;
                    return;
                }
                const points = pairs.filter(pair => (!this.logX || pair.x > 0) && (!this.logY || pair.y > 0)).map(pair => ({ x: pair.x, y: pair.y, recordId: pair.record.id }));
                const ownX = selected?.values[this.marker]; const ownY = selected?.values[this.secondary];
                const own = selected && finite(ownX) && finite(ownY) && (!this.logX || ownX > 0) && (!this.logY || ownY > 0) ? [{ x: ownX, y: ownY, recordId: selected.id }] : [];
                const colors = points.map(point => this.brushedIds.has(point.recordId) ? this.color('--athlete-modal-accent', '#7bdde3') : !this.fieldMode ? this.ownDateColor(this.allRecords.get(point.recordId)?.day ?? 0) : this.colorAge ? this.ageColor(this.allRecords.get(point.recordId)?.age ?? null) : 'rgba(159,184,202,.65)');
                const datasets: Record<string, unknown>[] = [{ label: this.fieldMode ? 'Other athletes' : 'Recorded tests', data: points, showLine: false, pointRadius: this.fieldMode ? 4 : 6, pointBackgroundColor: colors, pointBorderColor: this.color('--athlete-modal-chart-panel', '#111c26'), pointBorderWidth: 1.5 }, { label: 'Selected test', data: own, showLine: false, pointRadius: 8, pointBackgroundColor: this.color('--athlete-modal-accent', '#7bdde3'), pointBorderColor: this.color('--athlete-modal-cream', '#f6f4ed'), pointBorderWidth: 2.5 }];
                const plot = this.createPlot('relationships', `${marker.label} × ${this.markerInfo(this.secondary).label}`, `${this.markerInfo(this.secondary).label} · ${this.markerInfo(this.secondary).unit}`, datasets, [...points, ...own], this.logX, this.logY, false);
                this.element('.be-source').textContent = this.fieldMode ? `${this.referenceCaption(pairs.map(pair => pair.record))} · same submitted record/date · ${source.length - pairs.length} missing pairs` : `${pairs.length} paired tests in this period · same submitted record/date · 0-day pairing`;
                if (!pairs.length) plot.figure.insertAdjacentHTML('beforeend', '<p class="be-empty">No paired measurements in this selection. Both markers must be in the same submitted record.</p>');
                const offscale = pairs.length - points.length;
                if (offscale) plot.figure.insertAdjacentHTML('beforeend', `<p class="be-offscale">${offscale} pair${offscale === 1 ? '' : 's'} with nonpositive values available in the data table.</p>`);
                if (!this.fieldMode && pairs.length > 1) plot.figure.insertAdjacentHTML('beforeend', '<p class="be-date-key"><span></span> Earlier <span class="be-date-key-end"></span> Later · separate recorded tests; no fitted relationship</p>');
                if (this.fieldMode && this.colorAge) plot.figure.insertAdjacentHTML('beforeend', '<p class="be-age-key"><span></span> Age 20 or younger <span class="be-age-key-end"></span> 80 or older at test · gray = unknown</p>');
            }
            const recovery = this.element('.be-chart-recovery'); recovery.hidden = typeof window.Chart === 'function';
            recovery.querySelector('p')!.textContent = this.root.dataset.chartsState === 'loading' ? 'Loading graphs… Recorded values are already available below.' : 'Graphs are unavailable. All recorded values are available below.';
            this.element<HTMLButtonElement>('[data-action="retry-charts"]').hidden = this.root.dataset.chartsState === 'loading';
        }

        private referenceCaption(records: readonly BiomarkerExplorerRecord[]): string {
            const days = records.map(record => record.day);
            const span = days.length ? ` · draws ${dateLabel(Math.min(...days))}–${dateLabel(Math.max(...days))}` : '';
            return `${this.selectedReference ? 'Selected LWC panels' : 'LWC latest submitted panels'} · ${records.length} other athletes${span}`;
        }

        private color(variable: string, fallback: string): string {
            const key = variable + ':' + fallback; const cached = this.colorCache.get(key);
            if (cached) return cached;
            const value = getComputedStyle(this.root).getPropertyValue(variable).trim() || fallback;
            this.colorCache.set(key, value); return value;
        }
        private ageColor(age: number | null): string { return age === null ? '#a8b2ba' : `hsl(${205 - Math.min(1, Math.max(0, (age - 20) / 60)) * 155} 65% 70%)`; }
        private ownDateColor(day: number): string { return `hsl(184 55% ${78 - (day - this.fullRange[0]) / (this.fullRange[1] - this.fullRange[0]) * 35}%)`; }

        private dataset(label: string, points: readonly Point[], color: string, pointStyle = 'circle'): Record<string, unknown> {
            return { label, data: points, borderColor: color, backgroundColor: color, pointBackgroundColor: pointStyle === 'rectRot' ? this.color('--athlete-modal-chart-panel', '#111c26') : color, pointBorderColor: points.map(point => point.recordId && this.brushIsPersonal && this.brushedIds.has(point.recordId) ? this.color('--athlete-modal-cream', '#f6f4ed') : pointStyle === 'rectRot' ? color : this.color('--athlete-modal-chart-panel', '#111c26')), pointBorderWidth: 1.5, pointRadius: points.map(point => point.recordId === this.selectedId || point.recordId === this.comparisonId || point.recordId && this.brushIsPersonal && this.brushedIds.has(point.recordId) ? 6 : 4), pointStyle, showLine: true, borderWidth: 1.5, tension: 0, spanGaps: false, fill: false };
        }

        private createPlot(key: string, title: string, yTitle: string, datasets: readonly Record<string, unknown>[], points: readonly Point[], xLog: boolean, yLog: boolean, time: boolean): Plot {
            const figure = document.createElement('figure'); figure.className = 'be-figure' + (key === 'clocks' ? ' be-clock-figure' : '');
            figure.dataset.plot = key;
            figure.dataset.chartStatus = typeof window.Chart === 'function' ? 'ready' : 'unavailable';
            const measured = key !== 'clocks' && key !== 'relationships' ? this.selected()?.values[key === 'context' ? this.marker : key] : undefined;
            const clockKey = key === 'clocks' ? `<span class="be-clock-key">${datasets.map(dataset => `<button type="button" data-action="toggle-${dataset.label === 'pheno age' ? 'pheno' : 'bortz'}" aria-pressed="${!dataset.hidden}" aria-label="Show ${escape(dataset.label)} observations"><span class="${dataset.label === 'pheno age' ? 'be-pheno-dot' : 'be-bortz-dot'}">◇</span>${dataset.label === 'pheno age' ? 'pheno' : 'bortz'}</button>`).join('')}</span>` : '';
            figure.innerHTML = `<figcaption class="be-figure-heading"><span>${escape(title)}</span>${key === 'clocks' ? clockKey : finite(measured) ? `<strong class="be-figure-value">${formatted(measured)} <small>${escape(this.markerInfo(key === 'context' ? this.marker : key).unit)}</small></strong>` : ''}</figcaption><div class="be-chart-wrap"><canvas tabindex="0" role="img" aria-describedby="be-gesture" aria-label="${escape(title)}. Recorded observations are also available in the data table."></canvas></div><p class="be-probe" aria-live="off">&nbsp;</p>`;
            this.element('.be-plots').appendChild(figure);
            const canvas = figure.querySelector<HTMLCanvasElement>('canvas')!;
            canvas.style.touchAction = this.interaction === 'inspect' ? 'pan-y pinch-zoom' : 'none';
            const plot: Plot = { key, figure, canvas, points, datasets, xLog, yLog, chart: null, probeX: null, brush: null, abort: new AbortController() };
            this.plots.push(plot);
            const Chart = window.Chart as unknown as PlotConstructor | undefined;
            const context = canvas.getContext('2d');
            if (Chart && context) {
                const active = time ? points.filter(point => point.x >= this.range[0] && point.x <= this.range[1]) : points;
                const xExtent = time ? this.range : extent([...points.map(point => point.x), ...(key === 'context' ? this.data.ecdf(this.reference().map(record => record.values[this.marker] ?? NaN)).map(point => point.x) : []), this.selected()?.values[this.marker] ?? NaN], xLog);
                const yExtent = key === 'context' ? [0, 100] : extent(active.flatMap(point => finite(point.y) ? [point.y] : []), yLog);
                const bounds = key === 'context' ? this.contextBounds : key === 'relationships' ? this.relationshipBounds : null;
                const muted = this.color('--athlete-modal-muted', '#b8c5cf'); const grid = this.color('--athlete-modal-chart-grid', 'rgba(255,255,255,.08)');
                const config = { type: 'scatter', data: { datasets }, plugins: [{ id: 'lwc-biomarker-selection', afterDatasetsDraw: (chart: PlotChart) => this.drawGuides(plot, chart) }], options: {
                    responsive: true, maintainAspectRatio: false, animation: false, parsing: false,
                    events: [], layout: { padding: { top: 8, right: 12, bottom: 0, left: 0 } },
                    plugins: { legend: { display: false }, tooltip: { enabled: false } },
                    scales: { x: { type: xLog ? 'logarithmic' : 'linear', min: bounds?.x0 ?? xExtent[0], max: bounds?.x1 ?? xExtent[1], afterBuildTicks: (scale: { ticks: { value: number }[] }) => {
                            if (!time) return;
                            const days = [...new Set(this.ownVisible().map(record => record.day))];
                            if (days.length > 0 && days.length <= (this.root.clientWidth < 440 ? 4 : 6)) scale.ticks = days.map(value => ({ value }));
                        }, grid: { color: grid }, border: { color: grid }, title: { display: !time, text: `${this.markerInfo().label} · ${this.markerInfo().unit}`, color: muted, font: { size: 12 } }, ticks: { color: muted, maxTicksLimit: this.root.clientWidth < 440 ? 4 : 6, maxRotation: 0, callback: (value: string | number) => time ? new Intl.DateTimeFormat('en', { month: 'short', ...(this.range[1] - this.range[0] > 365 ? { year: '2-digit' as const } : { day: 'numeric' as const }), timeZone: 'UTC' }).format(new Date(Number(value) * dayMilliseconds)) : formatted(Number(value)) } },
                        y: { display: key !== 'context' || this.cumulative, type: yLog ? 'logarithmic' : 'linear', min: bounds?.y0 ?? yExtent[0], max: bounds?.y1 ?? yExtent[1], afterFit: (scale: { width: number }) => { if (time) scale.width = this.root.clientWidth < 440 ? 55 : 68; }, grid: { color: grid }, border: { color: grid }, title: { display: true, text: yTitle, color: muted, font: { size: 11 } }, ticks: { color: muted, maxTicksLimit: key === 'context' ? 3 : 5, callback: (value: string | number) => key === 'context' ? `${formatted(Number(value))}%` : formatted(Number(value)) } }
                    }
                } };
                plot.chart = new Chart(context, config);
            }
            this.bindPlot(plot, time);
            return plot;
        }

        private drawGuides(plot: Plot, chart: PlotChart): void {
            const x = chart.scales.x; const y = chart.scales.y; if (!x || !y) return;
            const area = chart.chartArea; const context = chart.ctx;
            context.save(); context.beginPath(); context.rect(area.left, area.top, area.right - area.left, area.bottom - area.top); context.clip();
            const guide = (value: number, color: string, dashed = false): void => { const pixel = x.getPixelForValue(value); if (!Number.isFinite(pixel)) return; context.beginPath(); context.strokeStyle = color; context.lineWidth = 1; context.setLineDash(dashed ? [4, 4] : []); context.moveTo(pixel, area.top); context.lineTo(pixel, area.bottom); context.stroke(); };
            if (this.view === 'time') { const selected = this.selected(); const comparison = this.allRecords.get(this.comparisonId); if (selected) guide(selected.day, this.color('--athlete-modal-accent', '#7bdde3')); if (comparison) guide(comparison.day, this.color('--athlete-modal-cream', '#f6f4ed'), true); }
            else if (plot.key === 'context') {
                const value = this.selected()?.values[this.marker];
                const values = this.reference().map(record => record.values[this.marker]).filter(finite);
                if (finite(value) && (!plot.xLog || value > 0) && values.length && this.cumulative) {
                    guide(value, this.color('--athlete-modal-accent', '#7bdde3'));
                    const count = this.data.counts(values, value); const px = x.getPixelForValue(value); const py = y.getPixelForValue(count.atOrBelow);
                    context.beginPath(); context.setLineDash([3, 4]); context.strokeStyle = this.color('--athlete-modal-accent', '#7bdde3'); context.moveTo(area.left, py); context.lineTo(px, py); context.stroke();
                    context.setLineDash([]); context.beginPath(); context.arc(px, py, 4, 0, Math.PI * 2); context.fillStyle = this.color('--athlete-modal-chart-panel', '#111c26'); context.fill(); context.stroke();
                    const label = `${formatted(count.atOrBelow, 1)}% at or below`; context.font = '11px Roboto, sans-serif'; context.fillStyle = this.color('--athlete-modal-accent', '#7bdde3');
                    context.fillText(label, Math.max(area.left + 4, Math.min(area.right - context.measureText(label).width - 4, px + 7)), Math.max(area.top + 14, py - 10));
                } else if (finite(value) && (!plot.xLog || value > 0) && values.length) {
                    const px = x.getPixelForValue(value); const py = y.getPixelForValue(50);
                    guide(value, this.color('--athlete-modal-accent', '#7bdde3'));
                    context.beginPath(); context.arc(px, py, 7, 0, Math.PI * 2); context.fillStyle = this.color('--athlete-modal-chart-panel', '#111c26'); context.fill();
                    context.lineWidth = 2; context.strokeStyle = this.color('--athlete-modal-accent', '#7bdde3'); context.stroke();
                    const label = `Selected: ${formatted(value)} ${this.markerInfo().unit}`;
                    context.font = '12px Roboto, sans-serif'; context.fillStyle = this.color('--athlete-modal-accent', '#7bdde3');
                    context.fillText(label, Math.max(area.left + 4, Math.min(area.right - context.measureText(label).width - 4, px + 10)), area.top + 14);
                }
            }
            const probe = this.view === 'time' ? this.timeProbe : plot.probeX;
            if (probe !== null) guide(probe, this.color('--athlete-modal-muted', '#b8c5cf'), true);
            if (plot.key === 'clocks') { const zero = y.getPixelForValue(0); context.beginPath(); context.setLineDash([4, 4]); context.strokeStyle = this.color('--athlete-modal-muted', '#b8c5cf'); context.moveTo(area.left, zero); context.lineTo(area.right, zero); context.stroke(); }
            if (plot.key === 'relationships' && !this.fieldMode) {
                const points = plot.datasets[0]?.data as readonly Point[] | undefined;
                if (points && points.length <= 6) for (const point of points) {
                    const record = point.recordId ? this.allRecords.get(point.recordId) : undefined;
                    if (!record || !finite(point.y)) continue;
                    const px = x.getPixelForValue(point.x); const py = y.getPixelForValue(point.y);
                    const label = new Intl.DateTimeFormat('en', { month: 'short', day: 'numeric', ...(this.range[1] - this.range[0] > 365 ? { year: '2-digit' as const } : {}), timeZone: 'UTC' }).format(new Date(record.day * dayMilliseconds));
                    context.setLineDash([]); context.font = '11px Roboto, sans-serif'; context.fillStyle = this.color('--athlete-modal-muted', '#b8c5cf');
                    context.fillText(label, Math.max(area.left + 3, Math.min(area.right - context.measureText(label).width - 3, px + 10)), Math.max(area.top + 14, py - 12));
                }
            }
            if (plot.brush) { const left = x.getPixelForValue(plot.brush.x0); const right = x.getPixelForValue(plot.brush.x1); const top = plot.key === 'context' || this.view === 'time' ? area.top : y.getPixelForValue(plot.brush.y1); const bottom = plot.key === 'context' || this.view === 'time' ? area.bottom : y.getPixelForValue(plot.brush.y0); context.setLineDash([]); context.fillStyle = 'rgba(123,221,227,.13)'; context.strokeStyle = this.color('--athlete-modal-accent', '#7bdde3'); context.fillRect(left, top, right - left, bottom - top); context.strokeRect(left, top, right - left, bottom - top); }
            context.restore();
        }

        private renderReadout(): void {
            const record = this.selected();
            const marker = this.markerInfo();
            const before = this.allRecords.get(this.comparisonId);
            if (!record) { this.element('.be-readout').innerHTML = '<p>No recorded test is available.</p>'; return; }
            const value = record.values[this.marker];
            const sameDate = this.athlete.records.filter(item => item.day === record.day).length;
            let html = `<div class="be-readout-label">${record.id === this.athlete.records.at(-1)?.id ? 'Latest submitted test' : 'Selected test'} · ${escape(dateLabel(record.day))}${record.age !== null ? ` · age at test ${formatted(record.age, 1)}` : ''}${sameDate > 1 ? ` · ${sameDate} panels on this date` : ''}</div><div class="be-measurement"><strong>${formatted(value)}</strong><span>${escape(marker.unit)}<small>${escape(marker.label)} · measured</small></span></div>`;
            if (this.view === 'relationships') html += `<div class="be-second-value">${escape(this.markerInfo(this.secondary).label)} <strong>${formatted(record.values[this.secondary])}</strong> ${escape(this.markerInfo(this.secondary).unit)} · measured in this same panel</div>`;
            if (this.view === 'context') {
                const values = this.reference().map(item => item.values[this.marker]).filter(finite);
                if (finite(value) && values.length) { const count = this.data.counts(values, value); html += `<div class="be-counts"><span><strong>${count.lower}</strong> lower</span><span><strong>${count.equal}</strong> equal</span><span><strong>${count.higher}</strong> higher</span></div><p class="be-context-position">${formatted(count.atOrBelow, 1)}% of these ${values.length} panels are at or below this value.</p>`; }
                else html += '<p>This selected panel does not contain the marker, or the reference set is empty.</p>';
            }
            if (before && this.view === 'time') {
                const a = before.values[this.marker]; const delta = finite(a) && finite(value) ? value - a : null;
                const days = record.day - before.day;
                html += `<div class="be-difference"><span>${escape(dateLabel(before.day))} → ${escape(dateLabel(record.day))}</span><strong>${delta === null ? 'Missing measurement' : `${signed(delta)} ${escape(marker.unit)}`}</strong><span>${Math.abs(days)} days apart${days < 0 ? ' · comparing an earlier test with the baseline' : ''}</span></div>`;
            }
            this.element('.be-readout').innerHTML = html;
            this.element('.be-clock-details').hidden = !before || this.view !== 'time';
            if (before) this.renderClockExplanation(before, record);
            const preview = this.allRecords.get(this.previewId);
            const previewElement = this.element('.be-preview'); previewElement.hidden = !preview;
            if (preview) previewElement.innerHTML = `<div><strong>${escape(preview.athleteName)}</strong> · ${escape(dateLabel(preview.day))}${preview.age !== null ? ` · age at test ${formatted(preview.age, 1)}` : ''}<p>${escape(marker.label)} ${formatted(preview.values[this.marker])} ${escape(marker.unit)}${this.view === 'relationships' ? ` · ${escape(this.markerInfo(this.secondary).label)} ${formatted(preview.values[this.secondary])} ${escape(this.markerInfo(this.secondary).unit)}` : ''}</p></div><button type="button" data-action="close-preview">Close preview</button>`;
        }

        private renderClockExplanation(before: BiomarkerExplorerRecord, after: BiomarkerExplorerRecord): void {
            let html = '<p>These are terms in the LWC calculation of biological minus chronological age. They describe the model’s arithmetic, not causal effects on health. Chronological age is an input to both clocks.</p>';
            for (const clock of ['pheno', 'bortz'] as const) {
                const rows = this.data.clockChange(before, after, clock);
                if (!rows) { html += `<p>${clock}: both tests need complete eligible panels (${clock === 'pheno' ? '9' : '22'} measured inputs). Missing values are not filled.</p>`; continue; }
                html += `<div class="be-table-scroll" tabindex="0" role="region" aria-label="${clock} clock change terms"><table class="be-table"><caption>${clock} · ${signed(rows.reduce((sum, row) => sum + row.change, 0))} computed years</caption><thead><tr><th scope="col">Term</th><th scope="col">Before</th><th scope="col">After</th><th scope="col">Change in computed years</th></tr></thead><tbody>${rows.map(row => `<tr><th scope="row">${escape(row.label)}${row.capped ? '<small>LWC scoring cap applied; measured values retained</small>' : ''}${row.transformed ? '<small>Log transformation in the model</small>' : ''}</th><td>${formatted(row.a)} ${escape(row.unit)}</td><td>${formatted(row.b)} ${escape(row.unit)}</td><td>${signed(row.change)}</td></tr>`).join('')}</tbody></table></div>`;
            }
            this.element('.be-clock-explanation').innerHTML = html;
        }

        private renderSelection(): void {
            const personalMode = this.view === 'time' || this.view === 'relationships' && !this.fieldMode;
            const active = this.element('.be-selection'); active.hidden = !this.brushedIds.size || this.brushIsPersonal !== personalMode;
            this.element('.be-selection-count').textContent = `${this.brushedIds.size} selected ${this.brushIsPersonal ? 'tests' : 'reference panels'}`;
            this.element('[data-action="use-selection"]').hidden = personalMode || this.brushIsPersonal;
            this.element('.be-reference-selection').hidden = !this.selectedReference || personalMode;
            const boundsElement = this.element<HTMLDetailsElement>('.be-bounds'); boundsElement.hidden = this.view === 'time';
            this.root.querySelectorAll<HTMLElement>('.be-y-bound').forEach(element => { element.hidden = this.view !== 'relationships'; });
            const plot = this.plots.find(plot => plot.key === this.view);
            const key = `${this.view}:${this.marker}:${this.secondary}:${this.fieldMode}`;
            if (plot?.chart && boundsElement.dataset.boundsKey !== key) {
                const x = plot.chart.scales.x; const y = plot.chart.scales.y;
                if (x && y) {
                    this.element<HTMLInputElement>('#be-x-from').value = formatted(x.min).replace(/,/g, ''); this.element<HTMLInputElement>('#be-x-to').value = formatted(x.max).replace(/,/g, '');
                    this.element<HTMLInputElement>('#be-y-from').value = formatted(y.min).replace(/,/g, ''); this.element<HTMLInputElement>('#be-y-to').value = formatted(y.max).replace(/,/g, '');
                    boundsElement.dataset.boundsKey = key;
                }
            }
        }

        private applyBounds(): void {
            const fields = ['#be-x-from', '#be-x-to', ...(this.view === 'relationships' ? ['#be-y-from', '#be-y-to'] : [])].map(selector => this.element<HTMLInputElement>(selector));
            fields.forEach(field => field.setCustomValidity(''));
            const values = fields.map(field => field.valueAsNumber);
            const invalid = values.some(value => !Number.isFinite(value)) || (values[0] ?? 0) > (values[1] ?? 0) || this.view === 'relationships' && (values[2] ?? 0) > (values[3] ?? 0);
            if (invalid) { const field = fields[0]; field?.setCustomValidity('Enter numeric bounds from smaller to larger.'); field?.reportValidity(); return; }
            this.selectBounds({ x0: values[0] ?? 0, x1: values[1] ?? 0, y0: values[2] ?? -Infinity, y1: values[3] ?? Infinity });
        }

        private selectBounds(bounds: Bounds): void {
            this.brushIsPersonal = this.view === 'relationships' && !this.fieldMode;
            const source = this.view === 'relationships' && !this.fieldMode ? this.ownVisible() : this.reference();
            this.brushedIds = new Set(source.filter(record => {
                const x = record.values[this.marker]; const y = record.values[this.secondary];
                return finite(x) && x >= bounds.x0 && x <= bounds.x1 && (this.view === 'context' || finite(y) && y >= bounds.y0 && y <= bounds.y1);
            }).map(record => record.id));
            this.interaction = 'inspect'; this.render(); this.announce(`${this.brushedIds.size} records selected. Reference denominator remains unchanged.`);
        }

        private renderAssociation(): void {
            const pairs = this.data.samePanelPairs(this.reference(), this.marker, this.secondary);
            if (pairs.length < 20 || !this.fieldMode) this.statistics = false;
            const checkbox = this.element<HTMLInputElement>('#be-statistics'); checkbox.disabled = pairs.length < 20;
            checkbox.checked = this.statistics;
            const result = this.statistics && this.fieldMode && pairs.length >= 20 ? this.data.rankAssociation(pairs) : null;
            this.element('.be-association').textContent = pairs.length < 20 ? `A field summary is available with at least 20 paired panels; currently ${pairs.length}. This display threshold does not establish reliability.` : !this.statistics ? 'Ask whether higher values tend to accompany higher values. The summary describes this selected field, without a significance test or causal interpretation.' : result === null ? `Rank association is undefined: one marker has no variation (${pairs.length} paired panels).` : `Spearman’s ρ = ${formatted(result, 2)} · n = ${pairs.length} other athletes. It summarizes the ordering of paired values in this field. Age, test date, selection and laboratory differences can influence the pattern; it is not a within-person result.`;
        }

        private renderReading(): void {
            let text = this.view === 'time' ? '<p>Filled dots are measured biomarkers; outlined diamonds are computed clocks. Dates use proportional calendar spacing. Connectors only link adjacent recorded tests, never across missing measurements or intervals over 180 days. They are guides between observations, not measurements between tests.</p><p>Age reduction is chronological age at test minus computed biological age. It is the competition score, depends on age as a model input, and does not demonstrate successful aging reversal. The contribution bars explain the exact score arithmetic between two submitted tests.</p>' : this.view === 'context' ? '<p>Each dot is one other athlete’s latest submitted value. Vertical stacking keeps equal or nearby values visible; it has no biological meaning. The outlined marker is the selected athlete’s result, excluded from the reference count. The optional cumulative view counts the share of recorded panels at or below each value; no normal curve is fitted.</p><p>The comparison source is the selected LWC field, one latest submitted panel per other athlete. These participants are not a general-population norm. A competition percentile is different from a laboratory reference range, clinical target or modeled mortality optimum.</p>' : '<p>Each point contains two measured markers from one submitted dated record. Pairing tolerance is zero days; neither values on different dates nor repeated tests are pooled as independent people. Same-date records do not provide an exact specimen time.</p><p>Across-athlete patterns and this athlete’s repeated tests answer different questions. Date coloring and the linked timeline help expose shared time trends; age filters explore the field without claiming statistical adjustment. Sparse personal histories have no fitted curves or correlation scores.</p>';
            text += `<p>Source: <a href="/api/data/athletes" target="_blank" rel="noopener">public LWC athlete snapshot</a>, retrieved ${escape(this.snapshotDate)} UTC. Units are the stored canonical units. Original report units, assay identifiers, censoring qualifiers, laboratory ranges and measurement uncertainty are not recorded in this contract. Source proofs are available in this athlete’s Proofs section; there is no automatic panel-to-proof mapping.</p>`;
            if (this.cohort.ambiguous) text += `<p>${this.cohort.ambiguous} other athlete${this.cohort.ambiguous === 1 ? ' has' : 's have'} multiple panels on the latest date and ${this.cohort.ambiguous === 1 ? 'is' : 'are'} excluded from the reference until the panel is resolved.</p>`;
            if (this.athlete.invalidDates) text += `<p>${this.athlete.invalidDates} record${this.athlete.invalidDates === 1 ? ' has an invalid date and is' : 's have invalid dates and are'} excluded from dated analysis.</p>`;
            this.element('.be-reading-content').innerHTML = text;
        }

        private tableRecords(): readonly BiomarkerExplorerRecord[] {
            if (this.view === 'time') return this.ownVisible();
            const source = this.view === 'relationships' && !this.fieldMode ? this.ownVisible() : this.reference();
            return source.filter(record => finite(record.values[this.marker]) && (this.view !== 'relationships' || finite(record.values[this.secondary])));
        }

        private renderTable(): void {
            const records = this.tableRecords();
            const pages = Math.max(1, Math.ceil(records.length / 50)); this.tablePage = Math.max(0, Math.min(this.tablePage, pages - 1));
            const rows = records.slice(this.tablePage * 50, (this.tablePage + 1) * 50);
            const keys = this.view === 'time' ? [this.marker, ...this.extraMarkers] : this.view === 'relationships' ? [this.marker, this.secondary] : [this.marker];
            const current = document.activeElement instanceof HTMLElement && this.root.contains(document.activeElement) ? document.activeElement.dataset.record : undefined;
            this.element('.be-data .be-table thead').innerHTML = `<tr>${this.view === 'time' || !this.fieldMode && this.view === 'relationships' ? '' : '<th scope="col">Athlete</th>'}<th scope="col">Test date</th><th scope="col">Age at test</th>${keys.map(key => `<th scope="col">${escape(this.markerInfo(key).label)}<small>${escape(this.markerInfo(key).unit)} · measured</small></th>`).join('')}${this.view === 'time' ? '<th scope="col">pheno age<small>years · computed</small></th><th scope="col">bortz age<small>years · computed</small></th>' : ''}</tr>`;
            this.element('.be-data .be-table tbody').innerHTML = rows.map(record => `<tr class="${this.brushedIds.has(record.id) ? 'be-row-brushed' : ''}"${record.id === this.selectedId ? ' aria-current="true"' : ''}>${this.view === 'time' || !this.fieldMode && this.view === 'relationships' ? '' : `<th scope="row">${escape(record.athleteName)}</th>`}<td><button type="button" data-record="${escape(record.id)}" aria-label="${record.athleteId === this.athlete.id ? 'Select test' : 'Preview'} ${escape(record.athleteName)}, ${escape(dateLabel(record.day))}">${escape(dateLabel(record.day))}${this.athlete.records.filter(item => item.day === record.day).length > 1 && record.athleteId === this.athlete.id ? '<small>Separate same-date panel</small>' : ''}</button></td><td>${formatted(record.age, 1)}</td>${keys.map(key => `<td>${formatted(record.values[key])}</td>`).join('')}${this.view === 'time' ? `<td>${formatted(record.pheno, 1)}</td><td>${formatted(record.bortz, 1)}</td>` : ''}</tr>`).join('');
            this.element('.be-table-caption').textContent = `${records.length} ${this.view === 'time' || this.view === 'relationships' && !this.fieldMode ? 'recorded tests' : 'reference panels'} · values stay in canonical units`;
            this.element('.be-pagination').hidden = pages <= 1;
            this.element('.be-pagination span').textContent = `${this.tablePage + 1} / ${pages}`;
            this.element<HTMLButtonElement>('[data-action="table-previous"]').disabled = this.tablePage === 0;
            this.element<HTMLButtonElement>('[data-action="table-next"]').disabled = this.tablePage >= pages - 1;
            if (current) this.element('.be-data').querySelector<HTMLButtonElement>(`[data-record="${CSS.escape(current)}"]`)?.focus({ preventScroll: true });
        }

        private exportCsv(): void {
            const records = this.tableRecords();
            const headers = ['Athlete', 'Panel ID', 'Test date', 'Chronological age at test (years)', ...this.data.markers.map(marker => `${marker.label} (${marker.unit}) - measured canonical value`), 'pheno age (years) - computed', 'bortz age (years) - computed', 'pheno biological minus chronological age (years) - computed', 'bortz biological minus chronological age (years) - computed', 'pheno age reduction (years) - computed', 'bortz age reduction (years) - computed', 'Analysis view', 'History metric', 'X marker', 'Y marker', 'Pinned test date', 'Baseline test date', 'Reference source', 'Reference panel count', 'Reference age at test filter (years)', 'Reference competition division', 'Snapshot retrieval date (UTC)', 'Pairing tolerance (days)', 'Clock parameter fingerprint', 'pheno model asset version', 'bortz model asset version', 'Chronological age calculation', 'Original units / assay / uncertainty metadata'];
            const definition = JSON.stringify({ pheno: window.PhenoAge?.biomarkers, phenoScale: 0.090165, bortz: window.BortzAge?.features, clampBiologicalAgeAtZero: true });
            let hash = 2166136261; for (let index = 0; index < definition.length; index++) hash = Math.imul(hash ^ definition.charCodeAt(index), 16777619);
            const fingerprint = (hash >>> 0).toString(16).padStart(8, '0');
            const csv = (value: string | number | null | undefined): string => { let text = value === null || value === undefined ? '' : String(value); if (typeof value === 'string' && /^[=+\-@\t\r]/.test(text)) text = "'" + text; return '"' + text.replace(/"/g, '""') + '"'; };
            const rows = records.map(record => [record.athleteName, record.id, record.date, record.age, ...this.data.markers.map(marker => record.values[marker.key]), record.pheno, record.bortz, record.pheno !== null && record.age !== null ? record.pheno - record.age : null, record.bortz !== null && record.age !== null ? record.bortz - record.age : null, record.pheno !== null && record.age !== null ? record.age - record.pheno : null, record.bortz !== null && record.age !== null ? record.age - record.bortz : null, this.view === 'relationships' ? this.fieldMode ? 'relationships across athletes' : 'relationships within athlete' : this.view, this.view === 'time' ? this.timeMetric : null, this.marker, this.view === 'relationships' ? this.secondary : null, this.selected()?.date, this.allRecords.get(this.comparisonId)?.date, this.view === 'time' || !this.fieldMode && this.view === 'relationships' ? 'This athlete - recorded tests in selected date window' : this.referenceCaption(records), this.view === 'time' || !this.fieldMode && this.view === 'relationships' ? null : records.length, `${this.ageMin ?? 'Any'} to ${this.ageMax ?? 'Any'}`, this.division || 'All divisions', this.snapshotDate, 0, fingerprint, this.root.dataset.phenoModelSrc ?? 'Unavailable', this.root.dataset.bortzModelSrc ?? 'Unavailable', 'UTC calendar days since birth / 365.2425', 'Not available in public contract']);
            const blob = new Blob(['\ufeff' + [headers, ...rows].map(row => row.map(csv).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
            const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = `${this.athlete.id.replace(/[^a-z0-9_-]/gi, '_')}-${this.view}-${this.snapshotDate}.csv`; link.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000); this.announce(`Exported ${records.length} records with raw canonical values and separately labeled computed clocks.`);
        }

        private renderNavigator(): void {
            const [min, max] = this.fullRange;
            const width = max - min;
            this.element('.be-rug').innerHTML = this.athlete.records.map(record => `<span style="left:${(record.day - min) / width * 100}%" class="${record.id === this.selectedId ? 'be-rug-selected' : ''}" title="${escape(dateLabel(record.day))}"></span>`).join('');
            this.updateNavigator();
        }

        private updateNavigator(): void {
            const [min, max] = this.fullRange;
            const rangeWindow = this.element('.be-window');
            rangeWindow.style.left = `${(this.range[0] - min) / (max - min) * 100}%`;
            rangeWindow.style.width = `${(this.range[1] - this.range[0]) / (max - min) * 100}%`;
            rangeWindow.dataset.narrow = String((this.range[1] - this.range[0]) / (max - min) * this.element('.be-navigator').clientWidth < 88);
            this.element<HTMLInputElement>('#be-from').value = date(this.range[0]);
            this.element<HTMLInputElement>('#be-to').value = date(this.range[1]);
            for (const edge of ['start', 'end'] as const) {
                const control = this.element(`.be-range-${edge}`);
                const value = this.range[edge === 'start' ? 0 : 1];
                control.setAttribute('aria-valuemin', String(Math.floor(min))); control.setAttribute('aria-valuemax', String(Math.ceil(max))); control.setAttribute('aria-valuenow', String(Math.round(value))); control.setAttribute('aria-valuetext', dateLabel(value));
            }
        }

        private bindNavigator(): void {
            const windowElement = this.element('.be-window');
            let drag: { pointer: number; x: number; range: [number, number]; mode: 'start' | 'end' | 'pan' } | null = null;
            windowElement.addEventListener('pointerdown', event => {
                if (event.button !== 0) return;
                const target = event.target instanceof Element ? event.target.closest('button') : null;
                if (!target) return;
                const mode = target.classList.contains('be-range-start') ? 'start' : target.classList.contains('be-range-end') ? 'end' : 'pan';
                drag = { pointer: event.pointerId, x: event.clientX, range: [...this.range], mode };
                windowElement.setPointerCapture(event.pointerId); event.preventDefault();
            }, { signal: this.abort.signal });
            windowElement.addEventListener('pointermove', event => {
                if (!drag || drag.pointer !== event.pointerId) return;
                const width = this.element('.be-navigator').getBoundingClientRect().width;
                const delta = (event.clientX - drag.x) / width * (this.fullRange[1] - this.fullRange[0]);
                if (drag.mode === 'pan') this.range = this.clampRange([drag.range[0] + delta, drag.range[1] + delta]);
                else if (drag.mode === 'start') this.range = [Math.max(this.fullRange[0], Math.min(drag.range[1] - 1, drag.range[0] + delta)), drag.range[1]];
                else this.range = [drag.range[0], Math.min(this.fullRange[1], Math.max(drag.range[0] + 1, drag.range[1] + delta))];
                this.updateNavigator(); this.updateTimeAxes();
            }, { signal: this.abort.signal });
            const finish = (event: PointerEvent): void => {
                if (!drag || event.pointerId !== drag.pointer) return;
                drag = null; if (windowElement.hasPointerCapture(event.pointerId)) windowElement.releasePointerCapture(event.pointerId);
                this.render(); this.announce(`${dateLabel(this.range[0])} to ${dateLabel(this.range[1])} visible.`);
            };
            windowElement.addEventListener('pointerup', finish, { signal: this.abort.signal });
            windowElement.addEventListener('pointercancel', finish, { signal: this.abort.signal });
            windowElement.addEventListener('keydown', event => {
                if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
                const target = event.target as HTMLElement; event.preventDefault();
                const step = event.shiftKey ? 30 : 1;
                const delta = event.key === 'ArrowLeft' ? -step : step;
                if (target.classList.contains('be-range-start')) this.range[0] = Math.max(this.fullRange[0], Math.min(this.range[1] - 1, event.key === 'Home' ? this.fullRange[0] : event.key === 'End' ? this.range[1] - 1 : this.range[0] + delta));
                else if (target.classList.contains('be-range-end')) this.range[1] = Math.min(this.fullRange[1], Math.max(this.range[0] + 1, event.key === 'End' ? this.fullRange[1] : event.key === 'Home' ? this.range[0] + 1 : this.range[1] + delta));
                else this.range = this.clampRange([this.range[0] + delta, this.range[1] + delta]);
                this.render(); this.announce(`${dateLabel(this.range[0])} to ${dateLabel(this.range[1])} visible.`);
            }, { signal: this.abort.signal });
        }

        private updateTimeAxes(): void {
            if (this.renderFrame) return;
            this.renderFrame = requestAnimationFrame(() => {
                this.renderFrame = 0; if (this.disposed) return;
                if (this.view !== 'time') { this.render(); return; }
                for (const plot of this.plots) if (plot.chart) { const x = plot.chart.options.scales.x; if (x) { x.min = this.range[0]; x.max = this.range[1]; plot.chart.update('none'); } }
            });
        }

        private chartBounds(plot: Plot): Bounds | null {
            const x = plot.chart?.scales.x; const y = plot.chart?.scales.y;
            return x && y ? { x0: x.min, x1: x.max, y0: y.min, y1: y.max } : null;
        }

        private zoom(factor: number, center?: { x: number; y: number }): void {
            const plot = this.plots.find(plot => plot.key === this.view) ?? this.plots[0];
            if (!plot) return;
            const bounds = this.chartBounds(plot); if (!bounds) return;
            const zoomAxis = (min: number, max: number, log: boolean, anchor?: number): [number, number] => {
                const transform = (value: number): number => log ? Math.log10(value) : value;
                const inverse = (value: number): number => log ? 10 ** Math.max(-300, Math.min(300, value)) : value;
                const a = transform(min); const b = transform(max); const middle = anchor === undefined || log && anchor <= 0 ? (a + b) / 2 : transform(anchor);
                return [inverse(middle + (a - middle) * factor), inverse(middle + (b - middle) * factor)];
            };
            if (this.view === 'time') {
                const range = zoomAxis(this.range[0], this.range[1], false, center?.x ?? this.selected()?.day);
                this.range = this.clampRange(range);
            } else {
                const x = zoomAxis(bounds.x0, bounds.x1, plot.xLog, center?.x); const y = zoomAxis(bounds.y0, bounds.y1, plot.yLog, center?.y);
                const next = { x0: x[0], x1: x[1], y0: this.view === 'context' ? 0 : y[0], y1: this.view === 'context' ? 100 : y[1] };
                if (this.view === 'context') this.contextBounds = next; else this.relationshipBounds = next;
            }
            this.render();
        }

        private nearestRecords(plot: Plot, px: number, py: number): readonly { record: BiomarkerExplorerRecord; distance: number; x: number; y: number }[] {
            if (!plot.chart) return [];
            const hits: { record: BiomarkerExplorerRecord; distance: number; x: number; y: number }[] = [];
            const area = plot.chart.chartArea;
            plot.datasets.forEach((dataset, datasetIndex) => {
                if (dataset.hidden) return;
                const points = dataset.data as readonly Point[] | undefined;
                const elements = plot.chart?.getDatasetMeta(datasetIndex).data;
                points?.forEach((point, index) => {
                    if (!point.recordId || !finite(point.y)) return;
                    const element = elements?.[index]; const record = this.allRecords.get(point.recordId);
                    if (!element || !record || !Number.isFinite(element.x) || !Number.isFinite(element.y) || element.x < area.left || element.x > area.right || element.y < area.top || element.y > area.bottom) return;
                    const distance = Math.hypot(px - element.x, py - element.y);
                    if (distance <= 24) hits.push({ record, distance, x: element.x, y: element.y });
                });
            });
            hits.sort((a, b) => a.distance - b.distance);
            const nearest = hits[0]; if (!nearest) return [];
            const seen = new Set<string>();
            return hits.filter(hit => Math.hypot(hit.x - nearest.x, hit.y - nearest.y) < 6 && !seen.has(hit.record.id) && (seen.add(hit.record.id), true));
        }

        private probe(plot: Plot, xValue: number, record?: BiomarkerExplorerRecord): void {
            plot.probeX = xValue;
            if (this.view === 'time') this.timeProbe = xValue;
            const line = plot.figure.querySelector<HTMLElement>('.be-probe'); if (!line) return;
            const clockValue = record && this.timeMetric !== 'marker' ? record[this.timeMetric] : null;
            const clockReduction = record?.age !== null && record?.age !== undefined && clockValue !== null ? record.age - clockValue : null;
            if (plot.key === 'context') {
                const values = this.reference().map(item => item.values[this.marker]).filter(finite);
                const count = this.data.counts(values, xValue);
                line.textContent = `${formatted(xValue)} ${this.markerInfo().unit} · ${count.lower} lower · ${count.equal} equal · ${count.higher} higher`;
            } else if (record) line.textContent = `${record.athleteName} · ${dateLabel(record.day)}${record.age !== null ? ` · age at test ${formatted(record.age, 1)}` : ''} · ${plot.key === 'clocks' ? `${this.timeMetric} ${formatted(clockReduction, 1)} years of computed age reduction` : `${this.markerInfo(plot.key === 'relationships' ? this.marker : plot.key).label} ${formatted(record.values[plot.key === 'relationships' ? this.marker : plot.key])} ${this.markerInfo(plot.key === 'relationships' ? this.marker : plot.key).unit}${plot.key === 'relationships' ? ` · ${this.markerInfo(this.secondary).label} ${formatted(record.values[this.secondary])} ${this.markerInfo(this.secondary).unit}` : ''}`}`;
            else line.textContent = this.view === 'time' ? `${dateLabel(xValue)} · no observed point here` : 'Tap a recorded point to inspect its values.';
            if (this.view === 'time') for (const lane of this.plots) lane.chart?.draw(); else plot.chart?.draw();
        }

        private bindPlot(plot: Plot, time: boolean): void {
            let drag: { pointer: number; px: number; py: number; x: number; y: number; bounds: Bounds; mode: Interaction; moved: boolean } | null = null;
            let pointerFrame = 0;
            const coordinates = (event: PointerEvent | WheelEvent): { px: number; py: number; x: number; y: number } | null => {
                const chart = plot.chart; if (!chart) return null;
                const rect = plot.canvas.getBoundingClientRect(); const px = event.clientX - rect.left; const py = event.clientY - rect.top;
                const area = chart.chartArea; const x = chart.scales.x; const y = chart.scales.y;
                if (!x || !y || px < area.left || px > area.right || py < area.top || py > area.bottom) return null;
                return { px, py, x: x.getValueForPixel(px), y: y.getValueForPixel(py) };
            };
            plot.canvas.addEventListener('pointerdown', event => {
                if (event.button !== 0) return;
                const position = coordinates(event); const bounds = this.chartBounds(plot); if (!position || !bounds) return;
                drag = { pointer: event.pointerId, ...position, bounds, mode: this.interaction, moved: false };
                if (this.interaction !== 'inspect') { plot.canvas.setPointerCapture(event.pointerId); event.preventDefault(); }
            }, { signal: plot.abort.signal });
            plot.canvas.addEventListener('pointermove', event => {
                const position = coordinates(event); if (!position) return;
                if (!drag) { if (event.pointerType === 'mouse' && this.interaction === 'inspect') { const nearest = this.nearestRecords(plot, position.px, position.py)[0]; this.probe(plot, nearest && time ? nearest.record.day : position.x, nearest?.record); } return; }
                if (event.pointerId !== drag.pointer) return;
                if (Math.hypot(position.px - drag.px, position.py - drag.py) > 6) drag.moved = true;
                if (drag.mode === 'select') {
                    plot.brush = { x0: Math.min(drag.x, position.x), x1: Math.max(drag.x, position.x), y0: Math.min(drag.y, position.y), y1: Math.max(drag.y, position.y) }; plot.chart?.draw();
                } else if (drag.mode === 'pan' && plot.chart) {
                    const area = plot.chart.chartArea;
                    const move = (a: number, b: number, log: boolean, fraction: number): [number, number] => { const min = log ? Math.log10(a) : a; const max = log ? Math.log10(b) : b; const delta = (max - min) * fraction; return log ? [10 ** Math.max(-300, Math.min(300, min + delta)), 10 ** Math.max(-300, Math.min(300, max + delta))] : [min + delta, max + delta]; };
                    const x = move(drag.bounds.x0, drag.bounds.x1, plot.xLog, (drag.px - position.px) / (area.right - area.left));
                    const y = move(drag.bounds.y0, drag.bounds.y1, plot.yLog, (position.py - drag.py) / (area.bottom - area.top));
                    if (time) { this.range = this.clampRange(x); this.updateNavigator(); this.updateTimeAxes(); }
                    else {
                        const bounds = { x0: x[0], x1: x[1], y0: plot.key === 'context' ? 0 : y[0], y1: plot.key === 'context' ? 100 : y[1] };
                        if (plot.key === 'context') this.contextBounds = bounds; else this.relationshipBounds = bounds;
                        if (!pointerFrame) pointerFrame = requestAnimationFrame(() => { pointerFrame = 0; const next = plot.key === 'context' ? this.contextBounds : this.relationshipBounds; const chart = plot.chart; if (!chart || !next || this.disposed) return; const sx = chart.options.scales.x; const sy = chart.options.scales.y; if (sx && sy) { sx.min = next.x0; sx.max = next.x1; sy.min = next.y0; sy.max = next.y1; chart.update('none'); } });
                    }
                }
            }, { signal: plot.abort.signal });
            plot.canvas.addEventListener('pointerup', event => {
                const start = drag; drag = null; if (!start || event.pointerId !== start.pointer) return;
                const position = coordinates(event);
                if (plot.canvas.hasPointerCapture(event.pointerId)) plot.canvas.releasePointerCapture(event.pointerId);
                if (start.mode === 'select' && start.moved && plot.brush) {
                    const bounds = plot.brush;
                    if (time) { this.range = this.clampRange([bounds.x0, bounds.x1]); this.interaction = 'inspect'; this.render(); this.announce('Selected date period applied.'); }
                    else this.selectBounds(bounds);
                } else if (start.mode === 'pan' && start.moved) this.render();
                else if (position && !start.moved) {
                    const hits = this.nearestRecords(plot, position.px, position.py);
                    const first = hits[0];
                    if (hits.length > 1) {
                        plot.figure.querySelector('.be-hit-list')?.remove();
                        const list = document.createElement('div'); list.className = 'be-hit-list';
                        list.innerHTML = `<p>${hits.length} records at this point</p>${hits.slice(0, 12).map(hit => `<button type="button" data-record="${escape(hit.record.id)}">${escape(hit.record.athleteName)} · ${escape(dateLabel(hit.record.day))}</button>`).join('')}${hits.length > 12 ? '<button type="button" data-action="show-data">View all in the data table</button>' : ''}`;
                        plot.figure.appendChild(list); this.announce(`${hits.length} overlapping records. Choose a record below the graph.`);
                    } else if (first) {
                        if (event.shiftKey && first.record.athleteId === this.athlete.id && first.record.id !== this.selectedId) { this.comparisonId = first.record.id; this.render(); }
                        else this.pickRecord(first.record.id);
                    } else { this.probe(plot, position.x); const text = plot.figure.querySelector('.be-probe')?.textContent; if (text) this.announce(text); }
                }
            }, { signal: plot.abort.signal });
            plot.canvas.addEventListener('pointercancel', () => { drag = null; plot.brush = null; plot.chart?.draw(); }, { signal: plot.abort.signal });
            plot.canvas.addEventListener('pointerleave', () => { if (!drag) { plot.probeX = null; this.timeProbe = null; if (this.view === 'time') for (const lane of this.plots) lane.chart?.draw(); else plot.chart?.draw(); const line = plot.figure.querySelector('.be-probe'); if (line) line.textContent = '\u00a0'; } }, { signal: plot.abort.signal });
            plot.canvas.addEventListener('wheel', event => { if (!event.shiftKey) return; const position = coordinates(event); if (!position) return; event.preventDefault(); this.zoom(event.deltaY > 0 ? 1.25 : 0.8, position); }, { signal: plot.abort.signal, passive: false });
            plot.canvas.addEventListener('keydown', event => {
                if (!['ArrowLeft', 'ArrowRight', 'Home', 'End', '+', '-', 'Escape'].includes(event.key)) return;
                if (event.key === 'Escape' && this.interaction === 'inspect' && !this.brushedIds.size) return;
                event.preventDefault();
                if (event.key === 'Escape') { event.stopPropagation(); this.interaction = 'inspect'; this.brushedIds.clear(); this.render(); return; }
                if (event.key === '+' || event.key === '-') { this.zoom(event.key === '+' ? 0.65 : 1.55); return; }
                if (time || this.view === 'relationships' && !this.fieldMode) {
                    if (event.key === 'Home' || event.key === 'End') { const record = event.key === 'Home' ? this.athlete.records[0] : this.athlete.records.at(-1); if (record) this.pickRecord(record.id); }
                    else this.moveRecord(event.key === 'ArrowRight' ? 1 : -1);
                } else if (plot.key === 'context') {
                    const values = [...new Set(this.reference().map(record => record.values[this.marker]).filter(finite))].sort((a, b) => a - b);
                    const selectedValue = plot.probeX ?? this.selected()?.values[this.marker] ?? values[0] ?? 0;
                    let index = values.findIndex(value => value >= selectedValue); if (index < 0) index = values.length - 1;
                    index = event.key === 'Home' ? 0 : event.key === 'End' ? values.length - 1 : Math.max(0, Math.min(values.length - 1, index + (event.key === 'ArrowRight' ? 1 : -1)));
                    const value = values[index]; if (value !== undefined) { this.probe(plot, value); this.announce(plot.figure.querySelector('.be-probe')?.textContent ?? ''); }
                } else {
                    const records = this.data.samePanelPairs(this.reference(), this.marker, this.secondary).slice().sort((a, b) => a.x - b.x || a.y - b.y);
                    let index = records.findIndex(pair => pair.record.id === this.previewId);
                    index = event.key === 'Home' ? 0 : event.key === 'End' ? records.length - 1 : Math.max(0, Math.min(records.length - 1, index + (event.key === 'ArrowRight' ? 1 : -1)));
                    const pair = records[index]; if (pair) this.pickRecord(pair.record.id);
                }
            }, { signal: plot.abort.signal });
            plot.abort.signal.addEventListener('abort', () => { if (pointerFrame) cancelAnimationFrame(pointerFrame); }, { once: true });
        }

        private destroyCharts(): void {
            for (const plot of this.plots) { plot.abort.abort(); plot.chart?.destroy(); }
            this.plots.length = 0;
        }

        resize(): void {
            if (this.disposed || this.resizeFrame) return;
            this.resizeFrame = requestAnimationFrame(() => { this.resizeFrame = 0; if (this.disposed) return; if ((this.root.clientWidth < 440) !== this.compact) this.render(); else for (const plot of this.plots) plot.chart?.resize(); });
        }

        refresh(): void { if (!this.disposed) this.render(); }

        destroy(): void {
            this.disposed = true; this.abort.abort(); this.resizeObserver?.disconnect(); this.themeObserver?.disconnect();
            if (this.resizeFrame) cancelAnimationFrame(this.resizeFrame); if (this.renderFrame) cancelAnimationFrame(this.renderFrame);
            this.destroyCharts(); delete this.root.dataset.explorerReady;
        }
    }

    let current: Explorer | null = null;
    window.LwcBiomarkerExplorer = {
        mount(root, athlete, field) { current?.destroy(); const data = window.LwcBiomarkerData; if (!data) throw new Error('Biomarker observation data component did not load.'); current = new Explorer(root, athlete, field, data); },
        destroy() { current?.destroy(); current = null; },
        resize() { current?.resize(); },
        refresh() { current?.refresh(); }
    };
})();
