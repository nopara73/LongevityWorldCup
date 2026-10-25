type BioageBrowserStorageName = 'localStorage' | 'sessionStorage';
type BioageStorageGetter = (key: string) => string | null;
type BioageStorageRemover = (key: string) => void;
type BioageClock = 'pheno' | 'bortz';

interface BioageSelectedAthleteDateOfBirth {
    Year: unknown;
    Month: unknown;
    Day: unknown;
}

interface BioageSelectedAthlete {
    Name: string;
    DateOfBirth: BioageSelectedAthleteDateOfBirth;
    Biomarkers?: unknown[];
}

interface BioageLatestBiomarkerValue {
    entry: object;
    value: number;
}

interface BioageBiomarkerComparisonState {
    currentDisplayValue?: unknown;
    previousDisplayValue?: unknown;
    currentScore?: unknown;
    previousScore?: unknown;
    neutral?: boolean;
}

type BioageBiomarkerComparisonGetter = (input: HTMLInputElement) => BioageBiomarkerComparisonState | null | undefined;
type BioageUnitDisplayValue = (
    canonicalValue: number,
    option: HTMLOptionElement,
    unitText: string
) => unknown;

interface BioageResultRevealOptions {
    instant?: boolean;
    userInitiated?: boolean;
}

interface BioageResultAnimationOptions {
    instant?: boolean;
}

interface BioageResultAnimationState {
    detailTimers: number[];
    frame: number;
    generation: number;
    rankRevealCleanup: (() => void) | null;
    revealScrollTop: number | null;
    settleTimer: number;
    startTimer: number;
}

interface BioageBiomarkerEntryOptions {
    clock: BioageClock;
    form: HTMLFormElement;
    isUpdate?: boolean;
    restoreDraft?: boolean;
}

interface BioageBiomarkerEntryResult {
    restoredDraft: boolean;
    step: 1 | 2;
}

interface BioageDraftField {
    checked?: boolean;
    customDate?: boolean;
    selectedIndex?: number;
    value: string;
}

interface BioageDraft {
    athleteName?: string;
    clock: BioageClock;
    fields: Record<string, BioageDraftField>;
    step: 1 | 2;
    version: 1;
}

interface BioageBiomarkerEntryController {
    athleteName: string | null;
    clock: BioageClock;
    draftFields: Record<string, BioageDraftField>;
    draftKey: string | null;
    draftPersistenceSuppressed: boolean;
    form: HTMLFormElement;
    hasPersistedDraft: boolean;
    inputs: HTMLInputElement[];
    invalidBatchActive: boolean;
    isUpdate: boolean;
    progress: HTMLParagraphElement;
    restoring: boolean;
    saveTimer: number;
    step: 1 | 2;
}

interface BioageLiveInstrumentOptions {
    clock: BioageClock;
    calculate: () => void;
    hide: () => void;
    getStep: () => number;
}

interface LwcBioageFlowApi {
    beginCalculation: () => void;
    flushLiveInstrument: () => boolean;
    canAutoCorrectUnit: (select: HTMLSelectElement) => boolean;
    bindLiveInstrument: (options: BioageLiveInstrumentOptions) => void;
    isLiveInstrumentBound: () => boolean;
    announceBioageResult: (
        resultElement: HTMLElement | null,
        announcement: string
    ) => void;
    animateBioageResult: (
        resultElement: HTMLElement | null,
        finalAge: number | string,
        animationOptions?: BioageResultAnimationOptions
    ) => void;
    bindBiomarkerComparison: (inputId: string, getState: BioageBiomarkerComparisonGetter) => void;
    clearBioageDraft: (clock?: BioageClock) => void;
    clearStoredBiomarkerHandoff: (removeItem?: BioageStorageRemover) => void;
    buildUnitSpecificBiomarkerPlaceholders: (
        inputId: string,
        canonicalValue: unknown,
        displayValueForUnit?: BioageUnitDisplayValue
    ) => object | null;
    getLatestBiomarkerEntry: (athlete: unknown, fieldNames?: string | readonly string[]) => object | null;
    getLatestBiomarkerValue: (athlete: unknown, fieldNames: string | readonly string[]) => BioageLatestBiomarkerValue | null;
    getBackDestination: (isUpdate: boolean) => '/dashboard' | '/join';
    getBrowserStorageItem: (storageName: BioageBrowserStorageName, key: string) => string | null;
    getLocalItem: BioageStorageGetter;
    getSessionItem: BioageStorageGetter;
    getDraftStep: (clock: BioageClock) => 1 | 2;
    getActiveBioageDraftKey: (clock: BioageClock) => string | null;
    shouldReloadBioageUpdate: (clock: BioageClock) => boolean;
    hasFiniteBiomarkerValue: (value: unknown) => boolean;
    hideUpdateModeStepNavigation: () => void;
    initializeBiomarkerEntry: (options: BioageBiomarkerEntryOptions) => BioageBiomarkerEntryResult;
    isUpdateMode: (search?: string) => boolean;
    isValidSelectedAthlete: (value: unknown) => value is BioageSelectedAthlete;
    navigateBack: (isUpdate: boolean) => void;
    prepareInitialView: (clock: BioageClock) => void;
    completeInitialView: () => void;
    readBiomarkerValue: (entry: unknown, fieldNames: string | readonly string[]) => number | null;
    readSelectedAthlete: (getItem?: BioageStorageGetter) => unknown;
    redirectMissingSelectedAthlete: (removeItem?: BioageStorageRemover) => void;
    removeBrowserStorageItem: (storageName: BioageBrowserStorageName, key: string) => void;
    removeLocalItem: BioageStorageRemover;
    removeSessionItem: BioageStorageRemover;
    expandBiomarkerCard: (field: string | Element | null | undefined) => void;
    resetUpdateModeScroll: () => void;
    revealBioageResult: (resultElement: HTMLElement | null, revealOptions?: BioageResultRevealOptions) => void;
    setBrowserStorageItem: (storageName: BioageBrowserStorageName, key: string, value: string) => boolean;
    setLocalItem: (key: string, value: string) => boolean;
    setSubmittedBiomarkerPlaceholders: (placeholdersByInputId: unknown) => void;
    setSessionItem: (key: string, value: string) => boolean;
    setDraftStep: (clock: BioageClock, step: number) => void;
    syncBioageResultActions: () => void;
    syncBioageResultVisibility: () => void;
    syncBiomarkerExamplePlaceholders: (root?: ParentNode | null) => void;
    toFiniteBiomarkerNumber: (value: unknown) => number | null;
    updateBiomarkerComparison: (inputId: string) => void;
    updateBiomarkerExamplePlaceholder: (selectOrInput: Element | null | undefined) => void;
    updateCalculateButton: () => void;
}

interface Window {
    LwcBioageFlow: LwcBioageFlowApi;
}

(function () {
    function isObject(value: unknown): value is object {
        return typeof value === 'object' && value !== null && !Array.isArray(value);
    }

    function removeBrowserStorageItem(storageName: BioageBrowserStorageName, key: string): void {
        try {
            window[storageName].removeItem(key);
        } catch (_) {
        }
    }

    function getBrowserStorageItem(storageName: BioageBrowserStorageName, key: string): string | null {
        try {
            return window[storageName].getItem(key);
        } catch (_) {
            return null;
        }
    }

    function setBrowserStorageItem(storageName: BioageBrowserStorageName, key: string, value: string): boolean {
        try {
            window[storageName].setItem(key, value);
            return true;
        } catch (_) {
            return false;
        }
    }

    function setSessionItem(key: string, value: string): boolean { return setBrowserStorageItem('sessionStorage', key, value); }
    function getSessionItem(key: string): string | null { return getBrowserStorageItem('sessionStorage', key); }
    function removeSessionItem(key: string): void { removeBrowserStorageItem('sessionStorage', key); }
    function setLocalItem(key: string, value: string): boolean { return setBrowserStorageItem('localStorage', key, value); }
    function getLocalItem(key: string): string | null { return getBrowserStorageItem('localStorage', key); }
    function removeLocalItem(key: string): void { removeBrowserStorageItem('localStorage', key); }

    const biomarkerExamplePlaceholders = {
        albumin: { 'g/L': '44', 'g/dL': '4.4' },
        alt: { 'U/L': '22', 'µkat/L': '0.37' },
        alp: { 'U/L': '70', 'µkat/L': '1.17' },
        ap: { 'U/L': '70', 'µkat/L': '1.17' },
        apoa1: { 'g/L': '1.5', 'mg/dL': '150' },
        cholesterol: { 'mmol/L': '4.8', 'mg/dL': '185' },
        creatinine: { 'µmol/L': '80', 'mg/dL': '0.9' },
        crp: { 'mg/L': '0.8', 'mg/dL': '0.08' },
        cystatin_c: { 'mg/L': '0.9', 'mg/dL': '0.09' },
        ggt: { 'U/L': '24', 'µkat/L': '0.40' },
        glucose: { 'mmol/L': '5.2', 'mg/dL': '94' },
        hba1c: { 'mmol/mol (IFCC)': '35', '%': '5.4', 'eAG (mmol/L)': '6.0', 'eAG (mg/dL)': '108' },
        lymphocyte: { '%': '32', '10⁹/L': '1.8', '10³/µL': '1.8' },
        lymphocyte_percentage: { '%': '32', '10⁹/L': '1.8', '10³/µL': '1.8' },
        mch: { 'pg': '30' },
        mcv: { 'fL': '90' },
        monocyte_percentage: { '%': '7', '10⁹/L': '0.4', '10³/µL': '0.4' },
        neutrophil_percentage: { '%': '58', '10⁹/L': '3.2', '10³/µL': '3.2' },
        rbc: { '10¹²/L': '4.8', '10⁶/µL': '4.8' },
        rcdw: { '%': '13.0' },
        rdw: { '%': '13.0' },
        shbg: { 'nmol/L': '45', 'µg/dL': '1.6' },
        urea: { 'Urea (mmol/L)': '5.0', 'Urea (mg/dL)': '30', 'BUN (mg/dL)': '14' },
        vitamin_d: { 'nmol/L': '75', 'ng/mL': '30', 'µg/L': '30' },
        wbc: { '10⁹/L': '5.5', '10³/µL': '5.5' }
    };

    function normalizeUnitText(value: unknown): string {
        return String(value || '').replace(/\s+/g, ' ').trim();
    }

    function toFiniteBiomarkerNumber(value: unknown): number | null {
        if (value === null || value === undefined || typeof value === 'boolean') return null;
        if (typeof value === 'number') return Number.isFinite(value) ? value : null;
        if (typeof value === 'string') {
            const trimmed = value.trim();
            if (!trimmed) return null;
            const number = Number(trimmed);
            return Number.isFinite(number) ? number : null;
        }
        return null;
    }

    function hasFiniteBiomarkerValue(value: unknown): boolean {
        return toFiniteBiomarkerNumber(value) !== null;
    }

    function formatBiomarkerPlaceholderValue(value: unknown): string | null {
        const number = toFiniteBiomarkerNumber(value);
        if (number === null) return null;

        return Number(number.toFixed(2)).toString();
    }

    function readBiomarkerValue(entry: unknown, fieldNames: string | readonly string[]): number | null {
        if (!entry || typeof entry !== 'object' || Array.isArray(entry)) return null;

        const fields = Array.isArray(fieldNames) ? fieldNames : [fieldNames];
        for (const field of fields) {
            const value = toFiniteBiomarkerNumber(Reflect.get(entry, field));
            if (value !== null) return value;
        }

        return null;
    }

    function getLatestBiomarkerEntry(athlete: unknown, fieldNames?: string | readonly string[]): object | null {
        if (!isObject(athlete)) return null;
        const biomarkers = Reflect.get(athlete, 'Biomarkers');
        if (!Array.isArray(biomarkers)) return null;

        let latestEntry: object | null = null;
        let latestTime = Number.NEGATIVE_INFINITY;
        let latestIndex = -1;

        biomarkers.forEach((entry: unknown, index: number) => {
            if (!entry || typeof entry !== 'object' || Array.isArray(entry)) return;
            if (fieldNames !== undefined && readBiomarkerValue(entry, fieldNames) === null) return;

            const parsedTime = Date.parse(String(Reflect.get(entry, 'Date')));
            const entryTime = Number.isFinite(parsedTime) ? parsedTime : Number.NEGATIVE_INFINITY;
            if (!latestEntry || entryTime > latestTime || (entryTime === latestTime && index > latestIndex)) {
                latestEntry = entry;
                latestTime = entryTime;
                latestIndex = index;
            }
        });

        return latestEntry;
    }

    function getLatestBiomarkerValue(
        athlete: unknown,
        fieldNames: string | readonly string[]
    ): BioageLatestBiomarkerValue | null {
        const entry = getLatestBiomarkerEntry(athlete, fieldNames);
        if (!entry) return null;
        const value = readBiomarkerValue(entry, fieldNames);
        return value === null ? null : { entry, value };
    }

    function buildUnitSpecificBiomarkerPlaceholders(
        inputId: string,
        canonicalValue: unknown,
        displayValueForUnit?: BioageUnitDisplayValue
    ): object | null {
        const canonicalNumber = toFiniteBiomarkerNumber(canonicalValue);
        const select = document.getElementById(`${inputId}Unit`);
        if (canonicalNumber === null || !(select instanceof HTMLSelectElement)) return null;

        const placeholderEntries: Array<[string, string]> = [];
        Array.from(select.options).forEach((option: HTMLOptionElement) => {
            const unitText = normalizeUnitText(option.textContent);
            if (!unitText) return;

            const optionValue = toFiniteBiomarkerNumber(option.value);
            const displayValue = typeof displayValueForUnit === 'function'
                ? displayValueForUnit(canonicalNumber, option, unitText)
                : optionValue === null
                    ? null
                    : canonicalNumber * optionValue;
            const formatted = formatBiomarkerPlaceholderValue(displayValue);
            if (formatted !== null) {
                placeholderEntries.push([unitText, formatted]);
            }
        });

        return placeholderEntries.length ? Object.fromEntries(placeholderEntries) : null;
    }

    function readSubmittedPlaceholder(input: HTMLInputElement, unitText: string): string | null {
        if (!input?.dataset?.bioageSubmittedPlaceholders) return null;

        try {
            const placeholders: unknown = JSON.parse(input.dataset.bioageSubmittedPlaceholders);
            const value = isObject(placeholders) ? Reflect.get(placeholders, unitText) : undefined;
            return typeof value === 'string' && value !== '' ? value : null;
        } catch (_) {
            delete input.dataset.bioageSubmittedPlaceholders;
            return null;
        }
    }

    function cleanSubmittedPlaceholderMap(placeholdersByUnit: unknown): object | null {
        if (!isObject(placeholdersByUnit)) return null;

        const cleanedEntries: Array<[string, string]> = [];
        Object.entries(placeholdersByUnit).forEach(([unitText, value]) => {
            const normalizedUnitText = normalizeUnitText(unitText);
            const formatted = typeof value === 'string' && value.trim()
                ? value.trim()
                : formatBiomarkerPlaceholderValue(value);

            if (normalizedUnitText && formatted !== null) {
                cleanedEntries.push([normalizedUnitText, formatted]);
            }
        });

        return cleanedEntries.length ? Object.fromEntries(cleanedEntries) : null;
    }

    function setSubmittedBiomarkerPlaceholders(placeholdersByInputId: unknown): void {
        const assignedIds = new Set<string>();
        const placeholderEntries = isObject(placeholdersByInputId) ? Object.entries(placeholdersByInputId) : [];
        placeholderEntries.forEach(([inputId, placeholdersByUnit]) => {
            const input = document.getElementById(inputId);
            if (!(input instanceof HTMLInputElement)) return;

            const cleaned = cleanSubmittedPlaceholderMap(placeholdersByUnit);
            if (cleaned) {
                input.dataset.bioageSubmittedPlaceholders = JSON.stringify(cleaned);
                assignedIds.add(inputId);
            } else {
                delete input.dataset.bioageSubmittedPlaceholders;
            }

            updateBiomarkerExamplePlaceholder(input);
        });

        document.querySelectorAll<HTMLInputElement>('input[data-bioage-submitted-placeholders]').forEach(input => {
            if (assignedIds.has(input.id)) return;

            delete input.dataset.bioageSubmittedPlaceholders;
            updateBiomarkerExamplePlaceholder(input);
        });
    }

    const biomarkerComparisonBindings = new Map<string, { getState: BioageBiomarkerComparisonGetter }>();

    function formatBiomarkerComparisonDelta(value: unknown): string | null {
        const number = toFiniteBiomarkerNumber(value);
        if (number === null) return null;

        const abs = Math.abs(number);
        if (abs < 0.005) return '0';
        return abs < 10
            ? Number(abs.toFixed(2)).toString()
            : Number(abs.toFixed(1)).toString();
    }

    function ensureBiomarkerComparisonChip(input: HTMLInputElement): HTMLSpanElement {
        let chip = input.parentElement?.querySelector<HTMLSpanElement>(`.bioage-input-comparison-chip[data-bioage-comparison-for="${input.id}"]`);
        if (chip) return chip;

        chip = document.createElement('span');
        chip.className = 'bioage-input-comparison-chip';
        chip.dataset.bioageComparisonFor = input.id;
        chip.hidden = true;
        input.parentElement?.appendChild(chip);
        return chip;
    }

    function hideBiomarkerComparison(input: HTMLInputElement): void {
        input.classList.remove('bioage-input-has-comparison');
        const chip = input.parentElement?.querySelector<HTMLSpanElement>(`.bioage-input-comparison-chip[data-bioage-comparison-for="${input.id}"]`);
        if (chip) {
            chip.hidden = true;
            chip.textContent = '';
            chip.className = 'bioage-input-comparison-chip';
            chip.removeAttribute('title');
            chip.removeAttribute('aria-label');
        }
    }

    function setBiomarkerComparisonChipContent(
        chip: HTMLSpanElement,
        text: string,
        direction: 'down' | 'up' | null
    ): void {
        chip.replaceChildren();
        if (!direction) {
            chip.textContent = text;
            return;
        }

        const icon = document.createElement('i');
        icon.className = `fas fa-arrow-${direction}`;
        icon.setAttribute('aria-hidden', 'true');

        const label = document.createElement('span');
        label.className = 'bioage-input-comparison-chip__text';
        label.textContent = text;

        chip.append(icon, label);
    }

    function updateBiomarkerComparison(inputId: string): void {
        const input = document.getElementById(inputId);
        const binding = biomarkerComparisonBindings.get(inputId);
        if (!(input instanceof HTMLInputElement) || !binding) return;

        let state: BioageBiomarkerComparisonState | null | undefined = null;
        try {
            state = binding.getState(input);
        } catch (_) {
            state = null;
        }

        const currentDisplay = toFiniteBiomarkerNumber(state?.currentDisplayValue);
        const previousDisplay = toFiniteBiomarkerNumber(state?.previousDisplayValue);
        if (currentDisplay === null || previousDisplay === null) {
            hideBiomarkerComparison(input);
            return;
        }

        const displayDelta = currentDisplay - previousDisplay;
        const deltaMagnitude = formatBiomarkerComparisonDelta(displayDelta);
        if (deltaMagnitude === null) {
            hideBiomarkerComparison(input);
            return;
        }

        const isSameDisplay = Math.abs(displayDelta) < 0.005;
        let stateClass = 'is-neutral';
        if (!state?.neutral) {
            const currentScore = toFiniteBiomarkerNumber(state?.currentScore);
            const previousScore = toFiniteBiomarkerNumber(state?.previousScore);
            if (currentScore === null || previousScore === null) {
                hideBiomarkerComparison(input);
                return;
            }

            const scoreDelta = currentScore - previousScore;
            if (Math.abs(scoreDelta) >= 0.000001) {
                stateClass = scoreDelta < 0 ? 'is-improved' : 'is-regressed';
            }
        }

        const text = isSameDisplay
            ? 'same as last'
            : `${deltaMagnitude} ${displayDelta < 0 ? 'lower' : 'higher'}`;
        const direction = isSameDisplay ? null : (displayDelta < 0 ? 'down' : 'up');
        const chip = ensureBiomarkerComparisonChip(input);
        chip.className = `bioage-input-comparison-chip ${stateClass}`;
        setBiomarkerComparisonChipContent(chip, text, direction);
        chip.hidden = false;
        chip.title = `Last ${formatBiomarkerPlaceholderValue(previousDisplay)}`;
        chip.setAttribute('aria-label', `${text}; last ${formatBiomarkerPlaceholderValue(previousDisplay)}`);
        input.classList.add('bioage-input-has-comparison');
    }

    function bindBiomarkerComparison(inputId: string, getState: BioageBiomarkerComparisonGetter): void {
        const input = document.getElementById(inputId);
        if (!(input instanceof HTMLInputElement)) return;

        biomarkerComparisonBindings.set(inputId, { getState });
        ensureBiomarkerComparisonChip(input);

        if (input.dataset.bioageComparisonBound !== 'true') {
            input.dataset.bioageComparisonBound = 'true';
            input.addEventListener('input', () => updateBiomarkerComparison(inputId));

            const unitSelect = document.getElementById(`${inputId}Unit`);
            if (unitSelect instanceof HTMLSelectElement) {
                unitSelect.addEventListener('change', () => updateBiomarkerComparison(inputId));
            }
        }

        updateBiomarkerComparison(inputId);
    }

    function getBiomarkerInputForUnitSelect(select: Element | null | undefined): HTMLInputElement | null {
        if (!select || !select.id || !select.id.endsWith('Unit')) return null;

        const input = document.getElementById(select.id.slice(0, -4));
        return input instanceof HTMLInputElement && input.matches('input[type="number"]') ? input : null;
    }

    function updateBiomarkerExamplePlaceholder(selectOrInput: Element | null | undefined): void {
        const selectCandidate = selectOrInput?.matches('select')
            ? selectOrInput
            : document.getElementById(`${selectOrInput?.id || ''}Unit`);
        const select = selectCandidate instanceof HTMLSelectElement ? selectCandidate : null;
        const inputCandidate = selectOrInput?.matches('input[type="number"]')
            ? selectOrInput
            : getBiomarkerInputForUnitSelect(select);
        const input = inputCandidate instanceof HTMLInputElement ? inputCandidate : null;

        if (!select || !input) return;

        const examplesByUnit = Object.hasOwn(biomarkerExamplePlaceholders, input.id)
            ? biomarkerExamplePlaceholders[input.id as keyof typeof biomarkerExamplePlaceholders]
            : undefined;
        const selectedOption = select.options[select.selectedIndex];
        const unitText = normalizeUnitText(selectedOption?.textContent);
        const submittedExample = readSubmittedPlaceholder(input, unitText);
        const candidateExample = submittedExample ?? (examplesByUnit && Object.hasOwn(examplesByUnit, unitText)
            ? Reflect.get(examplesByUnit, unitText)
            : undefined);
        const example = typeof candidateExample === 'string' ? candidateExample : null;

        if (example) {
            input.placeholder = example;
        } else {
            input.removeAttribute('placeholder');
        }
    }

    function syncBiomarkerExamplePlaceholders(root?: ParentNode | null): void {
        const scope = root || document;
        scope.querySelectorAll<HTMLSelectElement>('.biomarker-card-content .input-group select[id$="Unit"]').forEach(select => {
            updateBiomarkerExamplePlaceholder(select);

            if (select.dataset.bioageExamplePlaceholderBound === 'true') return;

            select.dataset.bioageExamplePlaceholderBound = 'true';
            select.addEventListener('change', () => updateBiomarkerExamplePlaceholder(select));
        });
    }

    const BIOAGE_DRAFT_VERSION = 1;
    const bioageMobileMedia = window.matchMedia(
        '(max-width: 600px), (max-width: 1024px) and (max-height: 600px) and (orientation: landscape)'
    );
    const biomarkerEntryControllers = new Map<BioageClock, BioageBiomarkerEntryController>();

    function getBioageDraftKey(clock: BioageClock, athleteName?: string | null): string {
        const owner = athleteName ? `update:${encodeURIComponent(athleteName)}:` : '';
        return `bioageDraft:${clock}:${owner}v${BIOAGE_DRAFT_VERSION}`;
    }

    function getActiveBioageDraftKey(clock: BioageClock): string | null {
        return biomarkerEntryControllers.get(clock)?.draftKey || null;
    }

    function shouldReloadBioageUpdate(clock: BioageClock): boolean {
        // A cached calculator can contain published carryover values filled during
        // calculation. Reinitialize from the raw draft on every cached update return.
        return biomarkerEntryControllers.get(clock)?.isUpdate === true;
    }

    function isBioageClock(value: unknown): value is BioageClock {
        return value === 'pheno' || value === 'bortz';
    }

    function readBioageDraft(clock: BioageClock, athleteName?: string | null): BioageDraft | null {
        const key = getBioageDraftKey(clock, athleteName);
        const raw = getSessionItem(key);
        if (!raw) return null;

        try {
            const draft: unknown = JSON.parse(raw);
            if (!isObject(draft)
                || Reflect.get(draft, 'version') !== BIOAGE_DRAFT_VERSION
                || Reflect.get(draft, 'clock') !== clock
                || (Reflect.get(draft, 'athleteName') || null) !== (athleteName || null)
                || !isObject(Reflect.get(draft, 'fields'))) {
                throw new Error('Invalid biological age draft');
            }

            const stepValue = Reflect.get(draft, 'step');
            return {
                version: BIOAGE_DRAFT_VERSION,
                clock,
                step: stepValue === 2 ? 2 : 1,
                fields: Reflect.get(draft, 'fields') as Record<string, BioageDraftField>
            };
        } catch (_) {
            removeSessionItem(key);
            return null;
        }
    }

    function clearBioageDraft(clock?: BioageClock): void {
        const clocks: BioageClock[] = clock ? [clock] : ['pheno', 'bortz'];
        clocks.forEach(draftClock => {
            const controller = biomarkerEntryControllers.get(draftClock);
            if (controller) {
                controller.draftPersistenceSuppressed = true;
                if (controller.saveTimer) window.clearTimeout(controller.saveTimer);
            }
            biomarkerEntryControllers.delete(draftClock);
            removeSessionItem(controller?.draftKey || getBioageDraftKey(draftClock));
        });
    }

    function getDraftControls(form: HTMLFormElement): Array<HTMLInputElement | HTMLSelectElement> {
        return Array.from(form.querySelectorAll<HTMLInputElement | HTMLSelectElement>(
            '#dob-year, #dob-month, #dob-day, #blood-draw-date, .biomarker-card input[id], .biomarker-card select[id]'
        ));
    }

    function serializeDraftFields(form: HTMLFormElement, isUpdate: boolean): Record<string, BioageDraftField> {
        const fields: Record<string, BioageDraftField> = {};
        getDraftControls(form).forEach(control => {
            if (!control.id || (isUpdate && control.id.startsWith('dob-'))) return;

            const field: BioageDraftField = { value: control.value };
            if (control.id === 'blood-draw-date') {
                field.customDate = control.value === '';
            }
            if (control instanceof HTMLInputElement && control.type === 'checkbox') {
                field.checked = control.checked;
            }
            if (control instanceof HTMLSelectElement) {
                field.selectedIndex = control.selectedIndex;
            }
            fields[control.id] = field;
        });
        return fields;
    }

    function canPersistBioageDraft(controller: BioageBiomarkerEntryController): boolean {
        if (!controller.draftKey || controller.restoring || controller.draftPersistenceSuppressed) return false;

        if (controller.hasPersistedDraft && getSessionItem(controller.draftKey) === null) {
            controller.draftPersistenceSuppressed = true;
            if (controller.saveTimer) window.clearTimeout(controller.saveTimer);
            controller.saveTimer = 0;
            return false;
        }

        return true;
    }

    function captureBioageDraftEdit(controller: BioageBiomarkerEntryController, target: EventTarget | null): void {
        if (!controller.isUpdate || controller.restoring
            || !(target instanceof HTMLInputElement || target instanceof HTMLSelectElement)) return;

        const fields = serializeDraftFields(controller.form, true);
        // An entered value or CRP checkbox also owns its unit controls. Changing
        // just a unit must not turn a calculated carryover value into a new entry.
        const card = target instanceof HTMLInputElement ? target.closest('.biomarker-card') : null;
        const controls = card ? card.querySelectorAll<HTMLInputElement | HTMLSelectElement>('input[id], select[id]') : [target];
        controls.forEach(control => {
            const field = fields[control.id];
            if (field) controller.draftFields[control.id] = field;
        });
    }

    function saveBioageDraft(controller: BioageBiomarkerEntryController): void {
        controller.saveTimer = 0;
        if (!canPersistBioageDraft(controller) || !controller.draftKey) return;
        const draft: BioageDraft = {
            version: BIOAGE_DRAFT_VERSION,
            clock: controller.clock,
            step: controller.step,
            // Calculations fill blank DOM inputs from published results. Only
            // explicit edits belong in an update draft, including on pagehide.
            fields: controller.isUpdate ? controller.draftFields : serializeDraftFields(controller.form, false),
            ...(controller.athleteName ? { athleteName: controller.athleteName } : {})
        };

        setSessionItem(controller.draftKey, JSON.stringify(draft));
        controller.hasPersistedDraft = getSessionItem(controller.draftKey) !== null;
    }

    function scheduleBioageDraftSave(controller: BioageBiomarkerEntryController): void {
        if (!canPersistBioageDraft(controller)) return;
        if (controller.saveTimer) window.clearTimeout(controller.saveTimer);
        controller.saveTimer = window.setTimeout(() => saveBioageDraft(controller), 100);
    }

    function applyDraftField(
        form: HTMLFormElement,
        fields: Record<string, BioageDraftField>,
        id: string
    ): void {
        const field = fields[id];
        const control = document.getElementById(id);
        if (!field || !control || !form.contains(control)) return;

        if (control instanceof HTMLSelectElement) {
            const selectedIndex = Number(field.selectedIndex);
            if (Number.isInteger(selectedIndex) && selectedIndex >= 0 && selectedIndex < control.options.length) {
                control.selectedIndex = selectedIndex;
            } else {
                control.value = typeof field.value === 'string' ? field.value : '';
            }
            return;
        }

        if (!(control instanceof HTMLInputElement)) return;
        if (control.type === 'checkbox') {
            control.checked = field.checked === true;
        } else {
            control.value = typeof field.value === 'string' ? field.value : '';
        }
    }

    function restoreBioageDraft(controller: BioageBiomarkerEntryController): boolean {
        if (!controller.draftKey) return false;
        const draft = readBioageDraft(controller.clock, controller.athleteName);
        if (!draft) return false;

        controller.draftFields = draft.fields;
        controller.hasPersistedDraft = true;
        controller.restoring = true;
        try {
            if (!controller.isUpdate) {
                applyDraftField(controller.form, draft.fields, 'dob-year');
                applyDraftField(controller.form, draft.fields, 'dob-month');
                controller.form.querySelector<HTMLSelectElement>('#dob-month')
                    ?.dispatchEvent(new Event('change', { bubbles: true }));
                applyDraftField(controller.form, draft.fields, 'dob-day');
            }

            Object.keys(draft.fields)
                .filter(id => !['dob-year', 'dob-month', 'dob-day'].includes(id))
                .forEach(id => applyDraftField(controller.form, draft.fields, id));

            // Update calculations must distinguish the restored new values from
            // unchanged markers that will carry over from the athlete's last result.
            if (controller.isUpdate) {
                controller.inputs.forEach(input => {
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    if (input.value.trim()) expandBiomarkerCard(input);
                });
            }

            controller.step = draft.step;
            return true;
        } finally {
            controller.restoring = false;
        }
    }

    function getBiomarkerInput(field: string | Element | null | undefined): HTMLInputElement | null {
        const candidate = typeof field === 'string' ? document.getElementById(field) : field;
        if (candidate instanceof HTMLInputElement) return candidate;
        return candidate?.closest<HTMLElement>('.biomarker-card')
            ?.querySelector<HTMLInputElement>('input[type="number"]') || null;
    }

    function expandBiomarkerCard(field: string | Element | null | undefined): void {
        const input = getBiomarkerInput(field);
        // Keep the existing restoration API, but the sheet's size and visibility
        // belong to CSS. Focus and errors must never revive accordion controls.
        input?.closest<HTMLElement>('.biomarker-card')?.classList.add('active');
    }

    function getBiomarkerLabel(input: HTMLInputElement): string {
        return input.getAttribute('aria-label')
            || input.closest('.biomarker-card')
            ?.querySelector<HTMLElement>('.biomarker-card-header')
            ?.childNodes[0]
            ?.textContent
            ?.trim()
            || 'this biomarker';
    }

    function isCompleteBiomarkerInput(input: HTMLInputElement): boolean {
        const value = input.value.trim();
        return value !== ''
            && Number.isFinite(Number(value))
            && input.validity.valid;
    }

    function getOrCreateBiomarkerError(input: HTMLInputElement): HTMLParagraphElement {
        const cardContent = input.closest<HTMLElement>('.biomarker-card-content');
        const errorId = `${input.id}-entry-error`;
        let error = cardContent?.querySelector<HTMLParagraphElement>(`#${errorId}`) || null;
        if (!error) {
            error = document.createElement('p');
            error.id = errorId;
            error.className = 'bioage-biomarker-error';
            error.hidden = true;
            error.setAttribute('role', 'alert');
            error.textContent = `Enter ${getBiomarkerLabel(input)}.`;
            cardContent?.appendChild(error);
        }

        const describedBy = new Set((input.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean));
        describedBy.add(errorId);
        input.setAttribute('aria-describedby', Array.from(describedBy).join(' '));
        return error;
    }

    function setBiomarkerError(input: HTMLInputElement, visible: boolean): void {
        const error = getOrCreateBiomarkerError(input);
        if (error.hidden !== !visible) {
            error.hidden = !visible;
        }
        input.setAttribute('aria-invalid', visible ? 'true' : 'false');
        document.querySelector<HTMLElement>(`[data-bioage-pip="${input.id}"]`)
            ?.classList.toggle('bioage-domain-pip--invalid', visible);
    }

    // Each domain title carries one pip per biomarker, so a long panel shows
    // where the gaps are without reading the rows.
    function getOrCreateDomainPip(input: HTMLInputElement): HTMLElement | null {
        const existing = document.querySelector<HTMLElement>(`[data-bioage-pip="${input.id}"]`);
        if (existing) return existing;
        const legend = input.closest<HTMLFieldSetElement>('fieldset')?.querySelector<HTMLLegendElement>(':scope > legend');
        if (!legend) return null;
        let tally = legend.querySelector<HTMLElement>('.bioage-domain-tally');
        if (!tally) {
            tally = document.createElement('span');
            tally.className = 'bioage-domain-tally';
            tally.setAttribute('aria-hidden', 'true');
            legend.appendChild(tally);
        }
        const pip = document.createElement('span');
        pip.className = 'bioage-domain-pip';
        pip.dataset.bioagePip = input.id;
        tally.appendChild(pip);
        return pip;
    }

    const primedBiomarkerControllers = new WeakSet<BioageBiomarkerEntryController>();
    function syncDomainTallies(controller: BioageBiomarkerEntryController): void {
        const domains = new Map<HTMLFieldSetElement, boolean>();
        controller.inputs.forEach(input => {
            const isComplete = input.dataset.bioageComplete === 'true';
            getOrCreateDomainPip(input)?.classList.toggle('bioage-domain-pip--complete', isComplete);
            const domain = input.closest<HTMLFieldSetElement>('fieldset');
            if (domain) domains.set(domain, (domains.get(domain) ?? true) && isComplete);
        });
        domains.forEach((complete, domain) => {
            const wasComplete = domain.classList.contains('bioage-domain--complete');
            domain.classList.toggle('bioage-domain--complete', complete);
            if (complete && !wasComplete && !controller.isUpdate && !controller.restoring && primedBiomarkerControllers.has(controller)) pulseClass(domain, 'bioage-domain--sealed', 720);
        });
        syncDomainWake(controller, domains);
    }

    // Domains wake one at a time: the panel being filled and the next one are
    // fully lit, later ones wait slightly dimmed (still usable) until reached.
    function syncDomainWake(
        controller: BioageBiomarkerEntryController,
        domains: Map<HTMLFieldSetElement, boolean>
    ): void {
        const ordered = Array.from(domains.keys());
        if (controller.isUpdate || ordered.length < 3) {
            ordered.forEach(domain => delete domain.dataset.bioageWake);
            return;
        }

        const firstIncomplete = ordered.findIndex(domain => !domains.get(domain));
        const frontier = firstIncomplete < 0 ? ordered.length : firstIncomplete;
        ordered.forEach((domain, index) => {
            const hasValue = Array.from(domain.querySelectorAll<HTMLInputElement>('input[type="number"]'))
                .some(input => input.value.trim() !== '');
            const awake = index <= frontier + 1 || domains.get(domain) || hasValue;
            const next = awake ? 'awake' : 'dormant';
            if (domain.dataset.bioageWake === 'dormant' && next === 'awake') {
                domain.classList.add('bioage-domain--waking');
                window.setTimeout(() => domain.classList.remove('bioage-domain--waking'), 520);
            }
            domain.dataset.bioageWake = next;
        });
    }

    // A value far from the lab's usual range for the chosen unit is usually a
    // unit mix-up, so the field nudges its unit cap instead of scolding.
    const BIOAGE_FAR_RANGE_FACTOR: Record<string, number> = { crp: 60 };
    function isBiomarkerValueFarFromExample(input: HTMLInputElement): boolean {
        const raw = input.value.trim();
        if (raw === '') return false;
        const value = Number(raw);
        if (!Number.isFinite(value)) return false;
        if (value < 0) return true;
        const example = Number.parseFloat(input.placeholder || '');
        if (!Number.isFinite(example) || example <= 0 || value === 0) return false;
        const factor = BIOAGE_FAR_RANGE_FACTOR[input.id] ?? 10;
        const ratio = value / example;
        return ratio > factor || ratio < 1 / factor;
    }

    function pulseClass(element: Element, className: string, durationMs: number): void {
        if (prefersReducedBioageResultMotion()) return;
        element.classList.remove(className);
        // Restart the animation when the same reaction repeats.
        void (element as HTMLElement).offsetWidth;
        element.classList.add(className);
        window.setTimeout(() => element.classList.remove(className), durationMs);
    }

    function syncBiomarkerFieldFeel(card: HTMLElement, input: HTMLInputElement, justCompleted: boolean): void {
        const far = isBiomarkerValueFarFromExample(input);
        const wasFar = card.dataset.bioageRange === 'far';
        if (far) card.dataset.bioageRange = 'far';
        else delete card.dataset.bioageRange;

        if (far && !wasFar) {
            pulseClass(card, 'bioage-card--nudge', 560);
        } else if (justCompleted && !far) {
            pulseClass(card, 'bioage-card--settled', 620);
            const pip = document.querySelector<HTMLElement>(`[data-bioage-pip="${input.id}"]`);
            if (pip) pulseClass(pip, 'bioage-domain-pip--pop', 420);
        }
    }

    // Section bands stick below whichever fixed site bars are showing.
    let sectionBandOffsetFrame = 0;
    function syncSectionBandOffset(): void {
        sectionBandOffsetFrame = 0;
        let top = 0;
        ['site-sticky-header', 'site-sticky-progress'].forEach(id => {
            const bar = document.getElementById(id);
            if (!bar) return;
            const style = getComputedStyle(bar);
            if (style.position !== 'fixed' || style.visibility === 'hidden') return;
            const rect = bar.getBoundingClientRect();
            // The progress row starts at the header's bottom, not at zero.
            if (rect.height > 0 && rect.bottom > 0 && rect.top < window.innerHeight) top = Math.max(top, rect.bottom);
        });
        const offset = `${Math.ceil(top)}px`;
        if (document.documentElement.style.getPropertyValue('--bioage-band-top') !== offset) {
            document.documentElement.style.setProperty('--bioage-band-top', offset);
        }
    }

    function watchSectionBandOffset(): void {
        if (document.documentElement.dataset.bioageBandOffset === 'true') return;
        document.documentElement.dataset.bioageBandOffset = 'true';
        const schedule = () => {
            if (!sectionBandOffsetFrame) sectionBandOffsetFrame = window.requestAnimationFrame(syncSectionBandOffset);
        };
        const bars = ['site-sticky-header', 'site-sticky-progress']
            .map(id => document.getElementById(id))
            .filter((bar): bar is HTMLElement => bar !== null);
        // Font loading, progress visibility and header reflow can change these
        // bounds without a scroll or viewport resize. Observe only the bars;
        // writing the form's offset cannot feed back into this observer.
        const sizeObserver = new ResizeObserver(schedule);
        const visibilityObserver = new MutationObserver(schedule);
        let watching = false;
        const start = () => {
            if (watching) return;
            watching = true;
            bars.forEach(bar => {
                sizeObserver.observe(bar);
                visibilityObserver.observe(bar, { attributes: true, attributeFilter: ['class', 'style', 'hidden', 'aria-hidden'] });
            });
            window.addEventListener('scroll', schedule, { passive: true });
            window.addEventListener('resize', schedule, { passive: true });
            schedule();
        };
        const stop = () => {
            watching = false;
            sizeObserver.disconnect();
            visibilityObserver.disconnect();
            window.removeEventListener('scroll', schedule);
            window.removeEventListener('resize', schedule);
            window.cancelAnimationFrame(sectionBandOffsetFrame);
            sectionBandOffsetFrame = 0;
        };
        window.addEventListener('pagehide', stop);
        window.addEventListener('pageshow', start);
        start();
    }

    function ensureBioageControlVisible(input: HTMLInputElement | HTMLSelectElement, immediate = false): void {
        const reveal = () => {
            if (document.activeElement !== input || !input.isConnected) return;
            const dock = getFlowActionDock();
            dock?.refreshNow?.();
            // The instrument controller may still be queued when a date receives
            // focus. Read the fixed bars now, before relying on their observer.
            if (immediate) {
                window.cancelAnimationFrame(sectionBandOffsetFrame);
                syncSectionBandOffset();
            }
            dock?.ensureClear?.(input, { margin: 16, behavior: immediate ? 'instant' : 'auto' });

            const visualViewport = window.visualViewport;
            const top = visualViewport?.offsetTop || 0;
            const bottom = top + (visualViewport?.height || window.innerHeight);
            const band = input.closest('fieldset')?.querySelector<HTMLElement>(':scope > legend');
            const bandHeight = band && getComputedStyle(band).position === 'sticky'
                ? band.getBoundingClientRect().height : 0;
            const rootStyle = getComputedStyle(document.documentElement);
            const fixedBottom = parseFloat(rootStyle.getPropertyValue('--bioage-band-top')) || 0;
            const clearTop = Math.max(top, fixedBottom) + bandHeight + 16;
            const dockHeight = parseFloat(rootStyle.getPropertyValue('--flow-action-dock-height')) || 0;
            const clearBottom = bottom - (immediate ? dockHeight : 0) - 16;
            const rect = input.getBoundingClientRect();
            const label = immediate ? input.labels?.item(0) : null;
            const labelRect = label?.getBoundingClientRect();
            // Keep the date caption with its control when the pair fits. Very
            // tall labels stay reachable in the surrounding scroll surface.
            const includeLabel = labelRect && labelRect.bottom <= rect.top + 1
                && rect.bottom - labelRect.top <= clearBottom - clearTop;
            const upperEdge = includeLabel ? Math.min(rect.top, labelRect.top) : rect.top;
            if (upperEdge < clearTop) {
                window.scrollBy({ top: Math.floor(upperEdge - clearTop), behavior: immediate ? 'instant' : 'auto' });
            } else if (rect.bottom > clearBottom) {
                input.scrollIntoView({
                    block: 'center',
                    inline: 'nearest',
                    behavior: immediate ? 'instant'
                        : window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth'
                });
            }
        };

        if (immediate) reveal();
        const initialScroll = window.scrollY;
        window.requestAnimationFrame(() => window.requestAnimationFrame(() => {
            // A new focus or deliberate scroll supersedes a pending correction.
            if (!immediate || Math.abs(window.scrollY - initialScroll) <= 1) reveal();
        }));
    }

    function syncFixedUnitPresentation(input: HTMLInputElement): void {
        const group = input.closest<HTMLElement>('.input-group');
        const select = group?.querySelector<HTMLSelectElement>('select');
        if (!group || !select || select.options.length !== 1) return;

        select.classList.add('bioage-fixed-unit-select');
        let suffix = group.querySelector<HTMLElement>('.bioage-fixed-unit');
        if (!suffix) {
            suffix = document.createElement('span');
            suffix.className = 'bioage-fixed-unit';
            suffix.id = `${input.id}-fixed-unit`;
            group.appendChild(suffix);
        }
        suffix.textContent = select.options[0]?.textContent?.trim() || '';

        const describedBy = new Set((input.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean));
        describedBy.add(suffix.id);
        input.setAttribute('aria-describedby', Array.from(describedBy).join(' '));
    }

    // Every biomarker stays open, so headers are plain labels with a completion dot.
    function syncBiomarkerHeaderSemantics(controller: BioageBiomarkerEntryController): void {
        controller.form.querySelectorAll<HTMLElement>('.biomarker-card-header').forEach(header => {
            const icon = header.querySelector<HTMLElement>('.toggle-icon');
            header.style.pointerEvents = '';
            header.style.cursor = '';
            header.removeAttribute('aria-disabled');
            header.removeAttribute('role');
            header.removeAttribute('tabindex');
            header.removeAttribute('aria-expanded');
            icon?.setAttribute('aria-hidden', 'true');
            if (icon) icon.textContent = '';
        });
    }

    function syncBiomarkerCompletion(controller: BioageBiomarkerEntryController): void {
        let completed = 0;
        const incompleteInputs: HTMLInputElement[] = [];
        controller.inputs.forEach(input => {
            const isComplete = isCompleteBiomarkerInput(input);
            const wasComplete = input.dataset.bioageComplete === 'true';
            input.dataset.bioageComplete = isComplete ? 'true' : 'false';
            const card = input.closest<HTMLElement>('.biomarker-card');
            card?.classList.toggle('biomarker-card--complete', isComplete);
            if (card && !controller.isUpdate && !controller.restoring) {
                syncBiomarkerFieldFeel(card, input, primedBiomarkerControllers.has(controller) && isComplete && !wasComplete);
            }
            if (isComplete) {
                completed += 1;
                setBiomarkerError(input, false);
            } else if (controller.isUpdate && input.value.trim() === '') {
                setBiomarkerError(input, false);
            }
            if (!isComplete) incompleteInputs.push(input);
        });
        syncDomainTallies(controller);
        controller.inputs.forEach((input, index) => {
            const hasOtherIncompleteInput = incompleteInputs.some(candidate => candidate !== input);
            input.enterKeyHint = (controller.isUpdate
                ? index < controller.inputs.length - 1
                : hasOtherIncompleteInput || index < controller.inputs.length - 1)
                ? 'next'
                : 'done';
        });

        const total = controller.inputs.length;
        const bloodDrawDate = controller.form.querySelector<HTMLInputElement>('#blood-draw-date');
        const hasBloodDrawDate = !!bloodDrawDate?.value && bloodDrawDate.validity.valid;
        const ready = controller.isUpdate
            ? completed > 0 && hasBloodDrawDate
            : total > 0 && completed === total;
        controller.progress.textContent = controller.isUpdate
            ? `${completed} new biomarker${completed === 1 ? '' : 's'}${completed > 0 && !hasBloodDrawDate ? ' · blood draw date required' : ''}`
            : `${completed} of ${total} biomarkers entered`;
        controller.progress.classList.toggle('bioage-biomarker-progress--complete', ready);
        primedBiomarkerControllers.add(controller);
        controller.form.style.setProperty('--bioage-entry-progress', `${total > 0 ? Math.round(completed / total * 100) : 0}%`);

        // The flow dock portals the action stack out of the form on small screens.
        const calculateButton = document.querySelector<HTMLButtonElement>('.bioage-calculate-button');
        if (calculateButton) {
            calculateButton.disabled = !ready;
            calculateButton.setAttribute('aria-describedby', controller.progress.id);
            if (!document.getElementById('continueButton')?.classList.contains('show')) {
                calculateButton.classList.toggle('green', ready);
                calculateButton.classList.toggle('grey', !ready);
                calculateButton.classList.toggle('flow-action--secondary', !ready);
            }
        }
    }

    function refreshAllBiomarkerCompletion(): void {
        biomarkerEntryControllers.forEach(syncBiomarkerCompletion);
    }

    function focusBiomarkerInput(input: HTMLInputElement): void {
        expandBiomarkerCard(input);
        input.focus();
        ensureBioageControlVisible(input);
    }

    function getFollowingBiomarkerInput(
        controller: BioageBiomarkerEntryController,
        input: HTMLInputElement
    ): HTMLInputElement | null {
        const index = controller.inputs.indexOf(input);
        return controller.inputs.slice(index + 1).find(candidate => !candidate.disabled) || null;
    }

    function moveToNextBiomarker(
        controller: BioageBiomarkerEntryController,
        input: HTMLInputElement
    ): void {
        const index = controller.inputs.indexOf(input);
        const nextInput = controller.inputs.slice(index + 1)
            .find(candidate => !candidate.disabled && !isCompleteBiomarkerInput(candidate))
            || (!controller.isUpdate
                ? controller.inputs.slice(0, index)
                    .find(candidate => !candidate.disabled && !isCompleteBiomarkerInput(candidate))
                : null)
            || getFollowingBiomarkerInput(controller, input);
        if (!nextInput) {
            input.blur();
            return;
        }

        focusBiomarkerInput(nextInput);
    }

    function bindBiomarkerEntry(controller: BioageBiomarkerEntryController): void {
        const recordDraftInput = (event: Event) => {
            if (!controller.restoring) clearStoredBiomarkerHandoff();
            captureBioageDraftEdit(controller, event.target);
            syncBiomarkerCompletion(controller);
            scheduleBioageDraftSave(controller);
        };

        controller.inputs.forEach(input => {
            syncFixedUnitPresentation(input);
            getOrCreateBiomarkerError(input);

            input.addEventListener('focus', () => {
                expandBiomarkerCard(input);
                ensureBioageControlVisible(input);
            });
            const unit = controller.form.querySelector<HTMLSelectElement>(`#${input.id}Unit`);
            unit?.addEventListener('focus', () => ensureBioageControlVisible(unit));
            window.LwcFieldValidation.bind(input, {
                validate: () => {
                    const valid = isCompleteBiomarkerInput(input)
                        || (controller.isUpdate && input.value.trim() === '' && !input.validity.badInput);
                    setBiomarkerError(input, !valid);
                    return valid;
                },
                clear: () => setBiomarkerError(input, false),
                isRestoring: () => controller.restoring
            });
            input.addEventListener('input', recordDraftInput);
            input.addEventListener('blur', () => scheduleBioageDraftSave(controller));
            input.addEventListener('keydown', event => {
                if (!bioageMobileMedia.matches) return;
                if (event.key === 'Tab' && !event.shiftKey) {
                    const nextInput = getFollowingBiomarkerInput(controller, input);
                    if (!nextInput) return;
                    event.preventDefault();
                    focusBiomarkerInput(nextInput);
                    return;
                }
                if (event.key !== 'Enter') return;
                event.preventDefault();
                if (controller.isUpdate && input.value.trim() === '') {
                    moveToNextBiomarker(controller, input);
                    return;
                }
                if (!isCompleteBiomarkerInput(input)) {
                    setBiomarkerError(input, true);
                    ensureBioageControlVisible(input);
                    return;
                }
                moveToNextBiomarker(controller, input);
            });
        });

        controller.form.querySelector<HTMLInputElement>('#blood-draw-date')?.addEventListener('input', recordDraftInput);
        controller.form.addEventListener('change', recordDraftInput);
        controller.form.addEventListener('invalid', event => {
            const input = event.target;
            if (!(input instanceof HTMLInputElement) || !controller.inputs.includes(input)) return;
            if (controller.invalidBatchActive) {
                event.preventDefault();
                return;
            }

            controller.invalidBatchActive = true;
            expandBiomarkerCard(input);
            setBiomarkerError(input, true);
            window.requestAnimationFrame(() => {
                input.focus();
                ensureBioageControlVisible(input);
            });
            window.setTimeout(() => {
                controller.invalidBatchActive = false;
            }, 0);
        }, true);

        bioageMobileMedia.addEventListener?.('change', () => {
            syncBiomarkerHeaderSemantics(controller);
            getFlowActionDock()?.refresh?.();
        });
        window.addEventListener('pagehide', () => {
            saveBioageDraft(controller);
            // Update views reload on cached return. Late commits from their
            // calculated DOM must not replace the raw draft while inactive.
            if (controller.isUpdate) controller.restoring = true;
        });
    }

    function initializeBloodDrawDate(form: HTMLFormElement, defaultToToday: boolean): void {
        const input = form.querySelector<HTMLInputElement>('#blood-draw-date');
        const control = input?.closest<HTMLElement>('.blood-draw-date-control');
        const label = control?.querySelector<HTMLElement>('.blood-draw-relative-label');
        if (!input || !control || !label) return;

        const localDate = (daysAgo: number) => {
            const date = new Date();
            date.setDate(date.getDate() - daysAgo);
            return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
        };
        if (!input.value && defaultToToday) input.value = localDate(0);

        const sync = () => {
            input.max = localDate(0);
            label.textContent = input.value === localDate(0) ? 'Today'
                : input.value === localDate(1) ? 'Yesterday' : '';
            label.hidden = !label.textContent;
            control.classList.toggle('has-relative-date', !!label.textContent);
        };
        const openPicker = () => {
            if (typeof input.showPicker === 'function' && !input.disabled) {
                try { input.showPicker(); } catch (_) { }
            }
        };

        input.addEventListener('input', sync);
        input.addEventListener('change', sync);
        input.addEventListener('click', openPicker);
        window.addEventListener('pageshow', sync);
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden) sync();
        });
        sync();
    }

    function initializeBiomarkerEntry(options: BioageBiomarkerEntryOptions): BioageBiomarkerEntryResult {
        const existing = biomarkerEntryControllers.get(options.clock);
        if (existing?.form === options.form) {
            syncBiomarkerCompletion(existing);
            return { restoredDraft: false, step: existing.step };
        }

        const stepTwo = options.form.querySelector<HTMLElement>('#lwc-step-2');
        const stepHeading = stepTwo?.querySelector('h2');
        if (options.isUpdate && stepTwo) {
            const bloodDrawDateFieldset = options.form.querySelector<HTMLInputElement>('#blood-draw-date')
                ?.closest<HTMLFieldSetElement>('fieldset');
            const firstBiomarkerFieldset = stepTwo.querySelector<HTMLFieldSetElement>(':scope > fieldset');
            if (bloodDrawDateFieldset && firstBiomarkerFieldset) {
                bloodDrawDateFieldset.classList.add('bioage-update-date-fieldset');
                stepTwo.insertBefore(bloodDrawDateFieldset, firstBiomarkerFieldset);
            }
        }
        const progress = document.createElement('p');
        progress.id = `${options.clock}BiomarkerProgress`;
        progress.className = 'bioage-biomarker-progress';
        progress.setAttribute('role', 'status');
        progress.setAttribute('aria-live', 'polite');
        progress.setAttribute('aria-atomic', 'true');
        stepHeading?.insertAdjacentElement('afterend', progress);

        const selectedAthlete = options.isUpdate ? readSelectedAthlete() : null;
        const athleteName = isValidSelectedAthlete(selectedAthlete) ? selectedAthlete.Name : null;
        const controller: BioageBiomarkerEntryController = {
            athleteName,
            clock: options.clock,
            draftFields: {},
            draftKey: options.isUpdate && !athleteName ? null : getBioageDraftKey(options.clock, athleteName),
            draftPersistenceSuppressed: false,
            form: options.form,
            hasPersistedDraft: false,
            inputs: Array.from(options.form.querySelectorAll<HTMLInputElement>(
                '.biomarker-card input[type="number"][required]'
            )),
            invalidBatchActive: false,
            isUpdate: options.isUpdate === true,
            progress,
            restoring: false,
            saveTimer: 0,
            step: 1
        };

        // Update drafts own raw values and units. Native history restoration
        // must not reapply calculation-filled controls after that draft loads.
        if (controller.isUpdate) options.form.autocomplete = 'off';
        options.form.classList.add('bioage-biomarker-entry-ready');
        biomarkerEntryControllers.set(options.clock, controller);
        bindBiomarkerEntry(controller);
        syncBiomarkerHeaderSemantics(controller);
        watchSectionBandOffset();

        const restoredDraft = options.restoreDraft !== false && restoreBioageDraft(controller);
        initializeBloodDrawDate(controller.form, !restoredDraft || controller.draftFields['blood-draw-date']?.customDate !== true);
        controller.draftFields = serializeDraftFields(controller.form, controller.isUpdate);
        syncBiomarkerExamplePlaceholders(options.form);
        syncBiomarkerCompletion(controller);
        removeSessionItem('lwcStep');

        return {
            restoredDraft,
            step: controller.step
        };
    }

    function getDraftStep(clock: BioageClock): 1 | 2 {
        return biomarkerEntryControllers.get(clock)?.step
            || readBioageDraft(clock)?.step
            || 1;
    }

    function setDraftStep(clock: BioageClock, step: number): void {
        const controller = biomarkerEntryControllers.get(clock);
        if (!controller) return;
        controller.step = step === 2 ? 2 : 1;
        scheduleBioageDraftSave(controller);
        if (isLiveInstrumentBound()) {
            if (step === 1) {
                cancelLiveCalculation();
                liveInstrumentOptions?.hide();
                syncLiveReadout(0);
            } else if (liveResultValues.has(getLiveResultElement()!)) {
                scheduleLiveInstrument(true);
            }
        }
    }

    function isUpdateMode(search?: string): boolean {
        return new URLSearchParams(search || window.location.search).get('update') === '1';
    }

    function prepareInitialView(clock: BioageClock): void {
        const params = new URLSearchParams(window.location.search);
        const prefillKeys = ['Year', 'Month', 'Day', 'Date', 'AlbGL', 'AlpUL', 'CreatUmolL', 'CrpMgL',
            'GluMmolL', 'LymPc', 'McvFL', 'RdwPc', 'Wbc1000cellsuL', 'NeutrophilPc', 'MonocytePc',
            'Rbc10e12L', 'MchPg', 'AltUL', 'GgtUL', 'UreaMmolL', 'CystatinCMgL', 'Hba1cMmolMol',
            'CholesterolMmolL', 'ApoA1GL', 'ShbgNmolL', 'VitaminDNmolL'];
        if (isUpdateMode() || readBioageDraft(clock) || params.get('fake') === '1'
            || prefillKeys.some(key => params.has(key)) || getBrowserStorageItem('sessionStorage', 'biomarkerData')) {
            window.LwcInitialView.prepare();
        }
    }

    function completeInitialView(): void {
        window.LwcInitialView.complete();
    }

    function getBackDestination(isUpdate: boolean): '/dashboard' | '/join' {
        return isUpdate ? '/dashboard' : '/join';
    }

    function navigateBack(isUpdate: boolean): void {
        window.navigateToFlowDestination(getBackDestination(isUpdate));
    }

    function resetUpdateModeScroll(): void {
        const reset = () => window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
        reset();
        window.requestAnimationFrame(() => {
            reset();
            getFlowActionDock()?.refresh?.();
        });
    }

    function getFlowActionDock(): LwcFlowActionDockApi | undefined {
        return Reflect.get(window, 'LwcFlowActionDock') as LwcFlowActionDockApi | undefined;
    }

    function isValidSelectedAthlete(value: unknown): value is BioageSelectedAthlete {
        if (!isObject(value)) return false;
        const name = Reflect.get(value, 'Name');
        const dateOfBirth = Reflect.get(value, 'DateOfBirth');
        return typeof name === 'string'
            && name.trim().length > 0
            && hasSelectedAthleteDateOfBirth(dateOfBirth);
    }

    function hasSelectedAthleteDateOfBirth(value: unknown): value is BioageSelectedAthleteDateOfBirth {
        if (!isObject(value)) return false;

        const year = toSelectedAthleteDatePart(Reflect.get(value, 'Year'), 1, 9999);
        const month = toSelectedAthleteDatePart(Reflect.get(value, 'Month'), 1, 12);
        const day = toSelectedAthleteDatePart(Reflect.get(value, 'Day'), 1, 31);

        if (year === null || month === null || day === null) return false;

        const date = new Date(0);
        date.setUTCFullYear(year, month - 1, day);
        date.setUTCHours(0, 0, 0, 0);
        return date.getUTCFullYear() === year
            && date.getUTCMonth() === month - 1
            && date.getUTCDate() === day;
    }

    function toSelectedAthleteDatePart(value: unknown, min: number, max: number): number | null {
        if (typeof value === 'boolean' || value === null || value === undefined) return null;

        const number: unknown = typeof value === 'string' && value.trim()
            ? Number(value)
            : value;
        return typeof number === 'number' && Number.isInteger(number) && number >= min && number <= max
            ? number
            : null;
    }

    function readSelectedAthlete(getItem?: BioageStorageGetter): unknown {
        const readItem = typeof getItem === 'function' ? getItem : getSessionItem;
        try {
            const selectedAthleteJson = readItem('selectedAthlete');
            return selectedAthleteJson ? JSON.parse(selectedAthleteJson) : null;
        } catch (_) {
            return null;
        }
    }

    function redirectMissingSelectedAthlete(removeItem?: BioageStorageRemover): void {
        const remove = typeof removeItem === 'function' ? removeItem : removeSessionItem;
        remove('selectedAthlete');
        remove('tempAthlete');
        window.location.replace('/select-athlete');
    }

    function clearStoredBiomarkerHandoff(removeItem?: BioageStorageRemover): void {
        const remove = typeof removeItem === 'function' ? removeItem : removeSessionItem;
        remove('biomarkerData');
        remove('bioageClock');
        remove('chronoPhenoDifference');
        remove('chronoBortzDifference');
        remove('biomarkerDraftKey');
    }

    function updateCalculateButton(): void {
        const calculateButton = document.querySelector('.bioage-calculate-button');
        const nextButton = document.getElementById('continueButton');
        if (!calculateButton || !nextButton) return;

        if (nextButton.classList.contains('show')) {
            calculateButton.classList.remove('green');
            calculateButton.classList.add('grey', 'flow-action--secondary');
        } else {
            calculateButton.classList.remove('grey', 'flow-action--secondary');
            calculateButton.classList.add('green');
        }

        syncBioageResultActions();
        refreshAllBiomarkerCompletion();
    }

    function syncBioageResultActions(): void {
        const nextButton = document.getElementById('continueButton');
        const resultActions = nextButton?.closest<HTMLElement>('.flow-action-stack');
        if (!nextButton || !resultActions || !document.body) return;

        const hasResult = nextButton.classList.contains('show');
        document.body.classList.toggle('bioage-result-ready', hasResult);
        resultActions.hidden = !hasResult;
        getFlowActionDock()?.refreshNow?.();

        if (hasResult && !lastBioageResultActionsVisible) {
            scheduleBioageResultReveal(getShownBioageResultElement());
        } else if (!hasResult) {
            clearScheduledBioageResultReveals();
        }

        lastBioageResultActionsVisible = hasResult;
    }

    let lastBioageResultShown = false;
    let lastBioageResultActionsVisible = false;
    let resultRevealFrame = 0;
    let pendingResultRevealElement: HTMLElement | null = null;
    let pendingResultRevealInstant = false;
    const BIOAGE_RESULT_COUNTUP_DURATION_MS = 900;
    const BIOAGE_RESULT_MAX_VISUAL_UPDATES = 72;
    const BIOAGE_RESULT_MIN_VISUAL_UPDATES = 24;
    const BIOAGE_RESULT_SCROLL_SETTLE_MS = 360;
    const BIOAGE_RESULT_START_AFTER_SCROLL_MS = BIOAGE_RESULT_SCROLL_SETTLE_MS + 40;
    const BIOAGE_RESULT_SETTLE_CLEANUP_MS = 700;
    const BIOAGE_RESULT_DETAIL_LEAD_MS = 140;
    const BIOAGE_RESULT_DETAIL_STEP_MS = 220;
    const bioageResultAnimationStates = new WeakMap<HTMLElement, BioageResultAnimationState>();

    function getBioageResultAnimationState(resultElement: HTMLElement): BioageResultAnimationState {
        const existing = bioageResultAnimationStates.get(resultElement);
        if (existing) return existing;

        const state: BioageResultAnimationState = {
            detailTimers: [],
            frame: 0,
            generation: 0,
            rankRevealCleanup: null,
            revealScrollTop: null,
            settleTimer: 0,
            startTimer: 0
        };
        bioageResultAnimationStates.set(resultElement, state);
        return state;
    }

    function getBioageResultValueContainer(resultElement: HTMLElement): HTMLElement | null {
        return resultElement.querySelector<HTMLElement>('.bio-age-number-container');
    }

    function clearBioageResultAnimation(resultElement: HTMLElement): BioageResultAnimationState {
        const state = getBioageResultAnimationState(resultElement);
        state.generation += 1;
        state.rankRevealCleanup?.();
        state.revealScrollTop = null;
        if (state.frame) {
            window.cancelAnimationFrame(state.frame);
            state.frame = 0;
        }
        if (state.settleTimer) {
            window.clearTimeout(state.settleTimer);
            state.settleTimer = 0;
        }
        if (state.startTimer) {
            window.clearTimeout(state.startTimer);
            state.startTimer = 0;
        }
        state.detailTimers.forEach(timer => window.clearTimeout(timer));
        state.detailTimers.length = 0;

        const container = getBioageResultValueContainer(resultElement);
        container?.classList.remove(
            'bioage-result-reveal--live',
            'bioage-result-reveal--waiting',
            'bioage-result-reveal--counting',
            'bioage-result-reveal--settling');
        if (container) container.dataset.bioageRevealState = 'idle';
        delete resultElement.dataset.bioageResultStage;
        return state;
    }

    function setBioageResultStage(
        resultElement: HTMLElement,
        stage: 'age' | 'difference' | 'context' | 'rank'
    ): void {
        // The complete result remains in the accessibility tree throughout. This
        // attribute sequences only the visible presentation of its existing nodes.
        resultElement.dataset.bioageResultStage = stage;
    }

    function scheduleBioageResultDetails(
        resultElement: HTMLElement,
        state: BioageResultAnimationState,
        generation: number
    ): void {
        const scheduleStage = (
            stage: 'difference' | 'context' | 'rank',
            delay: number
        ): void => {
            const timer = window.setTimeout(() => {
                if (state.generation !== generation) return;
                setBioageResultStage(resultElement, stage);
            }, delay);
            state.detailTimers.push(timer);
        };

        scheduleStage('difference', BIOAGE_RESULT_DETAIL_LEAD_MS);
        scheduleStage('context', BIOAGE_RESULT_DETAIL_LEAD_MS + BIOAGE_RESULT_DETAIL_STEP_MS);
        scheduleStage('rank', BIOAGE_RESULT_DETAIL_LEAD_MS + (BIOAGE_RESULT_DETAIL_STEP_MS * 2));
    }

    function announceBioageResult(
        resultElement: HTMLElement | null,
        announcement: string
    ): void {
        if (!resultElement?.classList.contains('show')) return;

        // Expose the status region before changing its text so assistive technology
        // observes a live-region update rather than newly unhidden static content.
        syncBioageResultVisibility();
        const liveRegion = resultElement.querySelector<HTMLElement>('[data-bioage-result-announcement]');
        if (liveRegion) liveRegion.textContent = announcement;
    }

    function prefersReducedBioageResultMotion(): boolean {
        return typeof window.matchMedia === 'function'
            && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    function formatBioageResultVisualValue(value: number): string {
        return (Math.abs(value) < 0.05 ? 0 : value).toFixed(1);
    }

    function getBioageResultVisualUpdateBudget(finalAge: number): number {
        return Math.min(
            BIOAGE_RESULT_MAX_VISUAL_UPDATES,
            Math.max(BIOAGE_RESULT_MIN_VISUAL_UPDATES, Math.ceil(Math.abs(finalAge))));
    }

    function syncBioageResultRevealTone(resultElement: HTMLElement, container: HTMLElement): void {
        const semanticValue = resultElement.querySelector<HTMLElement>('#animatedAge');
        const tone = semanticValue?.classList.contains('age-excellent')
            ? 'excellent'
            : semanticValue?.classList.contains('age-average')
                ? 'average'
                : 'good';
        container.dataset.bioageRevealTone = tone;
    }

    function animateBioageResult(
        resultElement: HTMLElement | null,
        finalAge: number | string,
        animationOptions: BioageResultAnimationOptions = {}
    ): void {
        if (!resultElement) return;

        const state = clearBioageResultAnimation(resultElement);
        const generation = state.generation;
        const container = getBioageResultValueContainer(resultElement);
        const visualValue = container?.querySelector<HTMLElement>('[data-bioage-result-visual]') || null;
        if (!container || !visualValue) return;

        syncBioageResultRevealTone(resultElement, container);
        if (typeof finalAge === 'string') {
            visualValue.textContent = finalAge;
            container.dataset.bioageRevealState = 'complete';
            setBioageResultStage(resultElement, 'rank');
            return;
        }
        if (!Number.isFinite(finalAge)) {
            liveResultValues.delete(resultElement);
            syncLiveReadout(0);
            visualValue.textContent = '';
            container.dataset.bioageRevealState = 'idle';
            return;
        }

        const finalText = formatBioageResultVisualValue(finalAge);
        container.style.setProperty('--bioage-result-characters', String(finalText.length));
        const previousValue = liveResultValues.get(resultElement);
        liveResultValues.set(resultElement, finalAge);
        if (isLiveInstrumentBound()) {
            syncGapMeter(resultElement);
            // Place an explicitly requested live result before its number reveal
            // starts; a smooth scroll can outlast the bounded animation start and
            // race the instant alignment performed by that animation.
            if (!liveRunActive) scheduleBioageResultReveal(resultElement, { instant: true });
        }
        syncLiveReadout(previousValue === undefined || finalAge >= previousValue ? 1 : -1);
        if (liveRunActive && !animationOptions.instant && !prefersReducedBioageResultMotion()) {
            const arriving = liveFirstArrival || previousValue === undefined || !Number.isFinite(previousValue);
            const from = arriving ? Math.max(0, finalAge - Math.min(18, Math.abs(finalAge))) : previousValue;
            container.classList.add('bioage-result-reveal--live');
            container.dataset.bioageRevealState = 'complete';
            setBioageResultStage(resultElement, arriving ? 'age' : 'rank');
            if (!arriving) pulseClass(container, 'bioage-result-reveal--nudge', 520);
            visualValue.textContent = formatBioageResultVisualValue(from);
            tweenRollingValue(visualValue, from, finalAge, state, generation, () => {
                if (arriving) scheduleBioageResultDetails(resultElement, state, generation);
            });
            return;
        }
        container.classList.remove('bioage-result-reveal--live');
        setBioageResultStage(resultElement, 'age');
        if (animationOptions.instant || prefersReducedBioageResultMotion()) {
            visualValue.textContent = finalText;
            container.dataset.bioageRevealState = 'complete';
            setBioageResultStage(resultElement, 'rank');
            return;
        }

        visualValue.textContent = '0.0';
        let startedAt = 0;
        let lastRenderedBucket = 0;
        let visibilityAttempts = 0;
        const visualUpdateBudget = getBioageResultVisualUpdateBudget(finalAge);
        const finish = (settle: boolean): void => {
            if (state.generation !== generation) return;

            state.frame = 0;
            visualValue.textContent = finalText;
            container.classList.remove('bioage-result-reveal--waiting', 'bioage-result-reveal--counting');
            if (!settle) {
                container.classList.remove('bioage-result-reveal--settling');
                container.dataset.bioageRevealState = 'complete';
                setBioageResultStage(resultElement, 'rank');
                return;
            }

            container.classList.add('bioage-result-reveal--settling');
            container.dataset.bioageRevealState = 'settling';
            state.settleTimer = window.setTimeout(() => {
                if (state.generation !== generation) return;
                state.settleTimer = 0;
                container.classList.remove('bioage-result-reveal--settling');
                container.dataset.bioageRevealState = 'complete';
                scheduleBioageResultDetails(resultElement, state, generation);
            }, BIOAGE_RESULT_SETTLE_CLEANUP_MS);
        };

        const countFrame = (timestamp: number): void => {
            if (state.generation !== generation) return;
            if (animationOptions.instant || prefersReducedBioageResultMotion()) {
                finish(false);
                return;
            }

            const progress = Math.min(
                1,
                Math.max(0, (timestamp - startedAt) / BIOAGE_RESULT_COUNTUP_DURATION_MS));

            if (progress >= 1) {
                finish(true);
                return;
            }

            // Keep the old reveal's satisfying near-year cadence without coupling
            // duration to intervals or refresh rate. Typical results advance about
            // one year per visual update; all results remain capped at 72 writes.
            const renderedBucket = Math.floor(progress * (visualUpdateBudget - 1));
            if (renderedBucket > lastRenderedBucket) {
                lastRenderedBucket = renderedBucket;
                const displayedProgress = renderedBucket / (visualUpdateBudget - 1);
                visualValue.textContent = formatBioageResultVisualValue(finalAge * displayedProgress);
            }

            state.frame = window.requestAnimationFrame(countFrame);
        };

        const startCounting = (): void => {
            if (state.generation !== generation) return;
            if (animationOptions.instant || prefersReducedBioageResultMotion()) {
                finish(false);
                return;
            }

            // Layout can still move after the form collapses and the action dock
            // portals its controls. Never spend the bounded reveal off-screen:
            // re-anchor after the final layout, or publish the static result if
            // the viewport cannot settle promptly.
            if (!isBioageResultValueVisible(resultElement)) {
                revealBioageResult(resultElement, { instant: true });
                visibilityAttempts += 1;
                if (visibilityAttempts >= 12) {
                    finish(false);
                    return;
                }

                state.frame = window.requestAnimationFrame(startCounting);
                return;
            }

            state.frame = 0;
            container.classList.remove('bioage-result-reveal--waiting');
            container.classList.add('bioage-result-reveal--counting');
            container.dataset.bioageRevealState = 'counting';
            startedAt = window.performance.now();
            state.frame = window.requestAnimationFrame(countFrame);
        };

        if (isBioageResultComfortablyVisible(resultElement)) {
            startCounting();
            return;
        }

        container.classList.add('bioage-result-reveal--waiting');
        container.dataset.bioageRevealState = 'waiting';
        state.startTimer = window.setTimeout(() => {
            state.startTimer = 0;
            if (state.generation !== generation) return;
            if (!resultElement.classList.contains('show')) {
                clearBioageResultAnimation(resultElement);
                return;
            }

            if (!isBioageResultComfortablyVisible(resultElement)) {
                revealBioageResult(resultElement, { instant: true });
            }
            state.frame = window.requestAnimationFrame(startCounting);
        }, BIOAGE_RESULT_START_AFTER_SCROLL_MS);
    }

    function getShownBioageResultElement(): HTMLElement | null {
        return document.querySelector<HTMLElement>('#phenoAgeResult.show, #bortzAgeResult.show');
    }

    function isRenderedElement(element: HTMLElement | null): element is HTMLElement {
        if (!element) return false;

        const rect = element.getBoundingClientRect();
        const style = window.getComputedStyle(element);
        return rect.width > 0
            && rect.height > 0
            && style.display !== 'none'
            && style.visibility !== 'hidden';
    }

    function getCssPixelValue(element: Element, propertyName: string): number {
        const value = parseFloat(window.getComputedStyle(element).getPropertyValue(propertyName));
        return Number.isFinite(value) ? value : 0;
    }

    function getVisualViewportBounds(): { top: number; bottom: number; height: number } {
        const visualViewport = window.visualViewport;
        const top = visualViewport && Number.isFinite(visualViewport.offsetTop) ? visualViewport.offsetTop : 0;
        const height = visualViewport && Number.isFinite(visualViewport.height)
            ? visualViewport.height
            : window.innerHeight;

        return {
            top,
            bottom: top + height,
            height
        };
    }

    function getBioageResultViewportBounds(): { top: number; bottom: number; height: number } {
        const rootStyle = window.getComputedStyle(document.documentElement);
        const scrollPaddingTop = parseFloat(rootStyle.scrollPaddingTop);
        const reservedTop = Number.isFinite(scrollPaddingTop) ? scrollPaddingTop : 52;
        const dockHeight = getCssPixelValue(document.documentElement, '--flow-action-dock-height');
        const reservedBottom = dockHeight + 16;
        const viewport = getVisualViewportBounds();
        const top = viewport.top + reservedTop;
        const bottom = viewport.bottom - reservedBottom;

        return {
            top,
            bottom,
            height: Math.max(0, bottom - top)
        };
    }

    function isBioageResultComfortablyVisible(resultElement: HTMLElement): boolean {
        const rect = resultElement.getBoundingClientRect();
        const viewportBounds = getBioageResultViewportBounds();

        return rect.top >= viewportBounds.top
            && rect.bottom <= viewportBounds.bottom;
    }

    function isBioageResultValueVisible(resultElement: HTMLElement): boolean {
        const container = getBioageResultValueContainer(resultElement);
        if (!container) return false;

        const rect = container.getBoundingClientRect();
        const viewport = getVisualViewportBounds();
        return rect.top >= viewport.top && rect.bottom <= viewport.bottom;
    }

    function getBioageResultRevealScrollTop(resultElement: HTMLElement): number {
        const rect = resultElement.getBoundingClientRect();
        const viewportBounds = getBioageResultViewportBounds();
        // Align a pending rank near the top rather than centring its temporary
        // loading state. Later reader movement must retain its own position.
        const rankPending = resultElement.querySelector('[data-bioage-result-rank][aria-busy="true"]');
        const targetTop = rankPending || rect.height >= viewportBounds.height
            ? viewportBounds.top
            : viewportBounds.top + ((viewportBounds.height - rect.height) / 2);

        return Math.max(0, window.scrollY + rect.top - targetTop);
    }

    function revealBioageResult(
        resultElement: HTMLElement | null,
        revealOptions: BioageResultRevealOptions = {}
    ): void {
        if (!resultElement) return;

        // An explicit reveal must measure the settled card. Sampling its arrival
        // transform would leave the label behind the header once the card lands.
        resultElement.getAnimations().forEach(animation => {
            if (animation instanceof CSSAnimation && animation.animationName === 'bioage-card-settle') {
                animation.finish();
            }
        });

        getFlowActionDock()?.refreshNow?.();

        const state = getBioageResultAnimationState(resultElement);
        if (revealOptions.userInitiated) {
            state.rankRevealCleanup?.();
            state.revealScrollTop = null;
        }
        if (state.revealScrollTop !== null && Math.abs(window.scrollY - state.revealScrollTop) > 2) return;
        const rankPending = resultElement.querySelector('[data-bioage-result-rank][aria-busy="true"]');
        if (!rankPending && isBioageResultComfortablyVisible(resultElement)) return;

        const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        const targetScrollTop = getBioageResultRevealScrollTop(resultElement);
        const maxScrollTop = Math.max(document.documentElement.scrollHeight, document.body.scrollHeight) - window.innerHeight;
        // Large-text forms can require several screens of travel. Avoid racing
        // that long smooth scroll with the result animation's bounded start.
        const longScroll = Math.abs(targetScrollTop - window.scrollY) > window.innerHeight;
        const instant = prefersReducedMotion || revealOptions.instant || longScroll;
        window.scrollTo({
            top: targetScrollTop,
            behavior: instant ? 'instant' : 'smooth',
        });
        if (instant) state.revealScrollTop = window.scrollY;

        // The loading card can leave too little document below it to reach the
        // target yet. Finish that initial scroll once, when rank loading ends,
        // only if the reader has not moved or interacted in the meantime.
        if (rankPending && targetScrollTop > maxScrollTop + 2) {
            state.rankRevealCleanup?.();
            const generation = state.generation;
            const expectedScroll = Math.max(0, maxScrollTop);
            const inputEvents = ['pointerdown', 'touchstart', 'wheel', 'keydown'] as const;
            const cleanup = (): void => {
                observer.disconnect();
                inputEvents.forEach(event => window.removeEventListener(event, cleanup));
                state.rankRevealCleanup = null;
            };
            const observer = new MutationObserver(() => {
                if (rankPending.getAttribute('aria-busy') === 'true') return;
                cleanup();
                window.setTimeout(() => {
                    if (state.generation !== generation || getShownBioageResultElement() !== resultElement) return;
                    if (Math.abs(window.scrollY - expectedScroll) > 2) return;
                    revealBioageResult(resultElement, { instant: true });
                }, prefersReducedMotion ? 0 : BIOAGE_RESULT_SCROLL_SETTLE_MS);
            });
            state.rankRevealCleanup = cleanup;
            inputEvents.forEach(event => window.addEventListener(event, cleanup, { passive: true, once: true }));
            observer.observe(rankPending, { attributes: true, attributeFilter: ['aria-busy'] });
        }

        window.setTimeout(
            () => getFlowActionDock()?.refresh?.(),
            prefersReducedMotion ? 0 : BIOAGE_RESULT_SCROLL_SETTLE_MS);
    }

    function clearScheduledBioageResultReveals(): void {
        if (resultRevealFrame) {
            window.cancelAnimationFrame(resultRevealFrame);
            resultRevealFrame = 0;
        }

        pendingResultRevealElement = null;
        pendingResultRevealInstant = false;
    }

    function scheduleBioageResultReveal(
        resultElement: HTMLElement | null,
        revealOptions: BioageResultRevealOptions = {}
    ): void {
        if (!resultElement) return;
        if (isLiveRevealSuppressed()) return;
        pendingResultRevealElement = resultElement;
        pendingResultRevealInstant ||= !!revealOptions.instant;
        if (resultRevealFrame) return;

        resultRevealFrame = window.requestAnimationFrame(() => {
            resultRevealFrame = window.requestAnimationFrame(() => {
                resultRevealFrame = 0;
                const pendingElement = pendingResultRevealElement;
                const pendingInstant = pendingResultRevealInstant;
                pendingResultRevealElement = null;
                pendingResultRevealInstant = false;

                if (!isRenderedElement(pendingElement)) return;
                if (getShownBioageResultElement() !== pendingElement) return;

                revealBioageResult(pendingElement, { instant: pendingInstant });
            });
        });
    }

    function syncBioageResultVisibility(): void {
        document.querySelectorAll<HTMLElement>('#phenoAgeResult, #bortzAgeResult')
            .forEach(candidate => {
                const isShown = candidate.classList.contains('show');
                candidate.toggleAttribute('inert', !isShown);
                if (isShown) candidate.removeAttribute('aria-hidden');
                else {
                    candidate.setAttribute('aria-hidden', 'true');
                    const announcement = candidate.querySelector<HTMLElement>('[data-bioage-result-announcement]');
                    if (announcement) announcement.textContent = '';
                    clearBioageResultAnimation(candidate);
                }
            });

        const resultElement = getShownBioageResultElement();
        const hasShownResult = !!resultElement;

        if (hasShownResult && !lastBioageResultShown) {
            scheduleBioageResultReveal(resultElement);
        }

        if (!hasShownResult) {
            clearScheduledBioageResultReveals();
        }

        lastBioageResultShown = hasShownResult;
    }

    function bindBioageResultActions(): void {
        const nextButton = document.getElementById('continueButton');
        if (!nextButton) return;

        const label = nextButton.querySelector('.flow-action__label');
        if (label && !isUpdateMode()) label.textContent = 'Join the leaderboard';

        syncBioageResultActions();
        syncBioageResultVisibility();

        const observer = new MutationObserver(syncBioageResultActions);
        observer.observe(nextButton, {
            attributes: true,
            attributeFilter: ['class']
        });

        const resultObserver = new MutationObserver(syncBioageResultVisibility);
        document.querySelectorAll('#phenoAgeResult, #bortzAgeResult').forEach(resultElement => {
            resultObserver.observe(resultElement, {
                attributes: true,
                attributeFilter: ['class']
            });
        });
    }

    function hideUpdateModeStepNavigation(): void {
        const wizardNav = document.querySelector<HTMLElement>('.lwc-wizard-nav');
        if (wizardNav) wizardNav.hidden = true;
    }

    // ---- Live instrument -------------------------------------------------
    // Once the panel is complete the result follows every edit: the age rolls
    // to its new value, the gap meter glides, and nothing scrolls the page
    // away from the field being typed in.
    const BIOAGE_LIVE_INPUT_IDLE_MS = 420;
    const BIOAGE_LIVE_COMMIT_IDLE_MS = 90;
    const BIOAGE_LIVE_GAP_SPAN_YEARS = 12;
    let liveInstrumentOptions: BioageLiveInstrumentOptions | null = null;
    let liveRunActive = false;
    let liveFirstArrival = false;
    let liveRevealSuppressed = false;
    let liveTimer = 0;
    let liveReadout: HTMLButtonElement | null = null;
    let liveResultVisible = true;
    const liveResultValues = new WeakMap<HTMLElement, number>();
    const manuallySelectedUnits = new WeakSet<HTMLSelectElement>();

    function canAutoCorrectUnit(select: HTMLSelectElement): boolean {
        return !isLiveInstrumentBound() || !manuallySelectedUnits.has(select);
    }

    function cancelLiveCalculation(): void {
        if (liveTimer) window.clearTimeout(liveTimer);
        liveTimer = 0;
    }

    function beginCalculation(): void {
        if (!isLiveInstrumentBound()) return;
        cancelLiveCalculation();
        if (!liveRunActive) liveRevealSuppressed = false;
        getLiveResultElement()?.classList.remove('bioage-result--stale');
        const next = document.querySelector<HTMLButtonElement>('#continueButton');
        if (next) next.disabled = true;
    }

    // A handoff must use one validated reading of the current fields, even when
    // invoked before input/change debounce or outside the button's click path.
    function flushLiveInstrument(): boolean {
        if (!isLiveInstrumentBound()) return true;
        cancelLiveCalculation();
        return runLiveInstrument(true);
    }

    function isLiveInstrumentBound(): boolean {
        return !!liveInstrumentOptions && !isUpdateMode();
    }

    function isLiveRevealSuppressed(): boolean {
        return liveRunActive || liveRevealSuppressed;
    }

    function getLiveResultElement(): HTMLElement | null {
        if (!liveInstrumentOptions) return null;
        return document.getElementById(liveInstrumentOptions.clock === 'pheno' ? 'phenoAgeResult' : 'bortzAgeResult');
    }

    // Each character is its own cell; a changed digit rolls in from below when
    // the value rises and from above when it falls. The old digit is drawn by
    // ::before from data-roll-from, so textContent always reads the new value.
    function renderRollingText(element: HTMLElement, text: string, direction: number): void {
        const previous = element.textContent ?? '';
        if (prefersReducedBioageResultMotion() || previous === text) {
            if (element.textContent !== text) element.textContent = text;
            return;
        }

        const offset = text.length - previous.length;
        const fragment = document.createDocumentFragment();
        Array.from(text).forEach((character, index) => {
            const cell = document.createElement('span');
            cell.className = 'bioage-roll';
            cell.textContent = character;
            const before = previous[index - offset] ?? '';
            if (before !== character && /\d/.test(character)) {
                cell.dataset.rollFrom = /\d/.test(before) ? before : '';
                cell.classList.add(direction >= 0 ? 'bioage-roll--up' : 'bioage-roll--down');
            }
            fragment.appendChild(cell);
        });
        element.replaceChildren(fragment);
    }

    function tweenRollingValue(
        element: HTMLElement,
        from: number,
        to: number,
        state: BioageResultAnimationState,
        generation: number,
        onDone?: () => void
    ): void {
        const direction = to >= from ? 1 : -1;
        const distance = Math.abs(to - from);
        const duration = Math.min(820, 300 + distance * 45);
        const startedAt = window.performance.now();
        let lastWrite = 0;
        const step = (now: number): void => {
            if (state.generation !== generation) return;
            const progress = Math.min(1, (now - startedAt) / duration);
            const eased = 1 - Math.pow(1 - progress, 3);
            if (progress >= 1) {
                state.frame = 0;
                renderRollingText(element, formatBioageResultVisualValue(to), direction);
                onDone?.();
                return;
            }
            // Writes are spaced so each digit's roll is readable, not a blur.
            if (now - lastWrite >= 55) {
                lastWrite = now;
                renderRollingText(element, formatBioageResultVisualValue(from + (to - from) * eased), direction);
            }
            state.frame = window.requestAnimationFrame(step);
        };
        state.frame = window.requestAnimationFrame(step);
    }

    function readResultGap(resultElement: HTMLElement): number | null {
        const text = resultElement.querySelector<HTMLElement>('[data-bioage-result-difference]')?.textContent || '';
        const match = text.match(/([+-])\s*(\d+(?:\.\d+)?)/);
        if (!match) return /years/.test(text) ? 0 : null;
        const magnitude = Number(match[2]);
        return match[1] === '-' ? -magnitude : magnitude;
    }

    function ensureGapMeter(resultElement: HTMLElement): HTMLElement | null {
        const difference = resultElement.querySelector<HTMLElement>('[data-bioage-result-difference]');
        if (!difference) return null;
        let meter = resultElement.querySelector<HTMLElement>('.bioage-gap-meter');
        if (!meter) {
            meter = document.createElement('div');
            meter.className = 'bioage-gap-meter';
            meter.setAttribute('aria-hidden', 'true');
            ['track', 'fill', 'origin', 'needle'].forEach(part => {
                const piece = document.createElement('span');
                piece.className = `bioage-gap-meter__${part}`;
                meter?.appendChild(piece);
            });
            difference.insertAdjacentElement('afterend', meter);
        }
        return meter;
    }

    function syncGapMeter(resultElement: HTMLElement): void {
        const meter = ensureGapMeter(resultElement);
        const gap = readResultGap(resultElement);
        if (!meter || gap === null) return;
        const ratio = Math.max(-1, Math.min(1, gap / BIOAGE_LIVE_GAP_SPAN_YEARS));
        meter.style.setProperty('--bioage-gap-start', `${50 + Math.min(0, ratio) * 50}%`);
        meter.style.setProperty('--bioage-gap-size', `${Math.abs(ratio) * 50}%`);
        meter.style.setProperty('--bioage-gap-needle', `${50 + ratio * 50}%`);
        meter.dataset.bioageGapTone = gap < -2 ? 'excellent' : gap < 0 ? 'good' : 'average';
    }

    function ensureLiveReadout(): HTMLButtonElement {
        if (liveReadout) return liveReadout;
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'bioage-live-readout';
        button.hidden = true;
        const value = document.createElement('span');
        value.className = 'bioage-live-readout__value';
        const gap = document.createElement('span');
        gap.className = 'bioage-live-readout__gap';
        const icon = document.createElement('i');
        icon.className = 'fas fa-arrow-down';
        icon.setAttribute('aria-hidden', 'true');
        gap.append(icon, document.createElement('span'));
        button.append(value, gap);
        button.addEventListener('click', () => {
            const resultElement = getShownBioageResultElement();
            if (resultElement) revealBioageResult(resultElement, { userInitiated: true });
        });
        document.body.appendChild(button);
        liveReadout = button;
        return button;
    }

    function syncLiveReadout(direction: number): void {
        if (!isLiveInstrumentBound()) return;
        const resultElement = getShownBioageResultElement();
        const readout = ensureLiveReadout();
        const age = resultElement ? liveResultValues.get(resultElement) : undefined;
        const gap = resultElement ? readResultGap(resultElement) : null;
        const shouldShow = !!resultElement && age !== undefined && Number.isFinite(age) && !liveResultVisible
            && !resultElement.classList.contains('bioage-result--stale')
            && liveInstrumentOptions?.getStep() === 2;
        readout.classList.toggle('bioage-live-readout--shown', shouldShow);
        readout.inert = !shouldShow;
        if (shouldShow) readout.hidden = false;
        else if (!readout.hidden) {
            // Leave the exit transition time to run before removing it.
            window.setTimeout(() => {
                if (!readout.classList.contains('bioage-live-readout--shown')) readout.hidden = true;
            }, 260);
        }
        if (!resultElement || age === undefined || !Number.isFinite(age)) return;

        const valueText = formatBioageResultVisualValue(age);
        const valueElement = readout.querySelector<HTMLElement>('.bioage-live-readout__value');
        if (valueElement) renderRollingText(valueElement, valueText, direction);
        const gapText = readout.querySelector<HTMLElement>('.bioage-live-readout__gap span');
        const gapIcon = readout.querySelector<HTMLElement>('.bioage-live-readout__gap i');
        if (gapText && gap !== null) renderRollingText(gapText, Math.abs(gap).toFixed(1), direction);
        if (gapIcon) gapIcon.className = `fas ${gap !== null && gap > 0 ? 'fa-arrow-up' : 'fa-arrow-down'}`;
        readout.dataset.bioageGapTone = gap === null ? 'good' : gap < -2 ? 'excellent' : gap < 0 ? 'good' : 'average';
        readout.setAttribute('aria-label', `${valueText} (${gap !== null && gap > 0 ? '+' : '-'}${Math.abs(gap ?? 0).toFixed(1)})`);
    }

    function watchLiveResultVisibility(resultElement: HTMLElement): void {
        if (typeof IntersectionObserver !== 'function' || resultElement.dataset.bioageLiveWatched === 'true') return;
        resultElement.dataset.bioageLiveWatched = 'true';
        const target = resultElement.querySelector<HTMLElement>('.bio-age-number-container') || resultElement;
        const observer = new IntersectionObserver(entries => {
            const entry = entries[entries.length - 1];
            liveResultVisible = !!entry?.isIntersecting;
            syncLiveReadout(0);
        }, { threshold: 0.35 });
        observer.observe(target);
    }

    function morphUnitSelect(select: HTMLSelectElement, automatic: boolean): void {
        const group = select.closest<HTMLElement>('.input-group');
        const input = group?.querySelector<HTMLInputElement>('input[type="number"]');
        if (prefersReducedBioageResultMotion() || !group) return;
        const easing = 'cubic-bezier(0.2, 0, 0, 1)';
        select.animate([
            { transform: 'translateY(55%)', opacity: 0 },
            { transform: 'translateY(0)', opacity: 1 }
        ], { duration: 240, easing });
        input?.animate([
            { transform: 'scale(0.96)', opacity: 0.35, filter: 'blur(1.5px)' },
            { transform: 'scale(1)', opacity: 1, filter: 'blur(0)' }
        ], { duration: 280, easing });
        pulseClass(group, automatic ? 'bioage-unit--auto' : 'bioage-unit--morph', 700);
    }

    function readUnitValues(form: HTMLFormElement): Map<HTMLSelectElement, string> {
        const values = new Map<HTMLSelectElement, string>();
        form.querySelectorAll<HTMLSelectElement>('#lwc-step-2 select[id$="Unit"]').forEach(select => values.set(select, select.value + '|' + select.selectedIndex));
        return values;
    }

    function isLivePanelReady(controller: BioageBiomarkerEntryController): boolean {
        const form = controller.form;
        const year = form.querySelector<HTMLSelectElement>('#dob-year');
        const drawDate = form.querySelector<HTMLInputElement>('#blood-draw-date');
        const month = Number(form.querySelector<HTMLSelectElement>('#dob-month')?.value || 12);
        const day = Number(form.querySelector<HTMLSelectElement>('#dob-day')?.value || 31);
        const birth = new Date(Number(year?.value), month - 1, day);
        const draw = new Date(`${drawDate?.value}T00:00:00`);
        return !!year?.value && year.validity.valid
            && !!drawDate?.value && drawDate.validity.valid
            && Number.isFinite(birth.getTime()) && Number.isFinite(draw.getTime())
            && birth <= draw
            && (controller.clock !== 'bortz' || Number(form.querySelector<HTMLInputElement>('#wbc')?.value) > 0)
            && controller.inputs.length > 0
            && controller.inputs.every(isCompleteBiomarkerInput);
    }

    function runLiveInstrument(commit: boolean): boolean {
        liveTimer = 0;
        const options = liveInstrumentOptions;
        if (!options || isUpdateMode() || options.getStep() !== 2) return false;
        const controller = biomarkerEntryControllers.get(options.clock);
        const resultElement = getLiveResultElement();
        if (!controller || !resultElement) return false;

        const shown = resultElement.classList.contains('show');
        if (!isLivePanelReady(controller)) {
            if (!shown) return false;
            if (commit) {
                resultElement.classList.remove('bioage-result--stale');
                options.hide();
            } else {
                resultElement.classList.add('bioage-result--stale');
            }
            syncLiveReadout(0);
            return false;
        }

        resultElement.classList.remove('bioage-result--stale');
        const unitsBefore = readUnitValues(controller.form);
        liveRunActive = true;
        liveFirstArrival = !shown;
        liveRevealSuppressed = true;
        try {
            options.calculate();
        } finally {
            liveRunActive = false;
        }
        unitsBefore.forEach((before, select) => {
            if (select.value + '|' + select.selectedIndex !== before) morphUnitSelect(select, true);
        });
        if (!shown && resultElement.classList.contains('show')) {
            resultElement.classList.add('bioage-result--arrived');
            window.setTimeout(() => resultElement.classList.remove('bioage-result--arrived'), 900);
        }
        const next = document.querySelector<HTMLButtonElement>('#continueButton');
        return !!next && next.classList.contains('show') && !next.disabled
            && Number.isFinite(liveResultValues.get(resultElement));
    }

    function scheduleLiveInstrument(commit: boolean): void {
        cancelLiveCalculation();
        clearScheduledBioageResultReveals();
        const next = document.querySelector<HTMLButtonElement>('#continueButton');
        if (next) next.disabled = true;
        const result = getLiveResultElement();
        if (result?.classList.contains('show')) result.classList.add('bioage-result--stale');
        if (liveInstrumentOptions?.getStep() !== 2) {
            liveInstrumentOptions?.hide();
            syncLiveReadout(0);
            return;
        }
        syncLiveReadout(0);
        liveTimer = window.setTimeout(
            () => runLiveInstrument(commit),
            commit ? BIOAGE_LIVE_COMMIT_IDLE_MS : BIOAGE_LIVE_INPUT_IDLE_MS);
    }

    function bindLiveInstrument(options: BioageLiveInstrumentOptions): void {
        if (isUpdateMode()) return;
        liveInstrumentOptions = options;
        const controller = biomarkerEntryControllers.get(options.clock);
        const resultElement = getLiveResultElement();
        if (!controller || !resultElement) return;

        controller.form.classList.add('bioage-live-instrument');
        ensureGapMeter(resultElement);
        watchLiveResultVisibility(resultElement);
        controller.form.addEventListener('input', event => {
            if (event.target instanceof HTMLInputElement || event.target instanceof HTMLSelectElement) scheduleLiveInstrument(false);
        });
        controller.form.addEventListener('change', event => {
            if (event.target instanceof HTMLSelectElement && /Unit$/.test(event.target.id)) {
                manuallySelectedUnits.add(event.target);
                morphUnitSelect(event.target, false);
            }
            scheduleLiveInstrument(true);
        });
        controller.form.addEventListener('focusout', event => {
            if (liveTimer && event.target instanceof HTMLInputElement) scheduleLiveInstrument(true);
        });
    }

    // This module blocks parsing before the form. Capture date focus from the
    // first interactive frame, independently of the later instrument setup.
    document.addEventListener('focus', event => {
        const input = event.target;
        if (!(input instanceof HTMLInputElement || input instanceof HTMLSelectElement)
            || !input.matches('#dob-year, #dob-month, #dob-day, #blood-draw-date')
            || !input.closest('form.bioageform')) return;
        ensureBioageControlVisible(input, true);
    }, true);

    window.LwcBioageFlow = {
        beginCalculation,
        flushLiveInstrument,
        canAutoCorrectUnit,
        bindLiveInstrument,
        isLiveInstrumentBound,
        announceBioageResult,
        animateBioageResult,
        bindBiomarkerComparison,
        clearBioageDraft,
        clearStoredBiomarkerHandoff,
        buildUnitSpecificBiomarkerPlaceholders,
        expandBiomarkerCard,
        getDraftStep,
        getActiveBioageDraftKey,
        shouldReloadBioageUpdate,
        getLatestBiomarkerEntry,
        getLatestBiomarkerValue,
        getBackDestination,
        getBrowserStorageItem,
        getLocalItem,
        getSessionItem,
        hasFiniteBiomarkerValue,
        hideUpdateModeStepNavigation,
        initializeBiomarkerEntry,
        isUpdateMode,
        isValidSelectedAthlete,
        navigateBack,
        prepareInitialView,
        completeInitialView,
        readBiomarkerValue,
        readSelectedAthlete,
        redirectMissingSelectedAthlete,
        removeBrowserStorageItem,
        removeLocalItem,
        removeSessionItem,
        resetUpdateModeScroll,
        revealBioageResult,
        setBrowserStorageItem,
        setLocalItem,
        setSubmittedBiomarkerPlaceholders,
        setSessionItem,
        setDraftStep,
        syncBioageResultActions,
        syncBioageResultVisibility,
        syncBiomarkerExamplePlaceholders,
        toFiniteBiomarkerNumber,
        updateBiomarkerComparison,
        updateBiomarkerExamplePlaceholder,
        updateCalculateButton
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', bindBioageResultActions, { once: true });
    } else {
        bindBioageResultActions();
    }
})();
