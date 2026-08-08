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
}

interface BioageResultAnimationOptions {
    instant?: boolean;
}

interface BioageResultAnimationState {
    detailTimers: number[];
    frame: number;
    generation: number;
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
    selectedIndex?: number;
    value: string;
}

interface BioageDraft {
    clock: BioageClock;
    fields: Record<string, BioageDraftField>;
    step: 1 | 2;
    version: 1;
}

interface BioageBiomarkerEntryController {
    clock: BioageClock;
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
    visitedInputs: Set<HTMLInputElement>;
}

interface LwcBioageFlowApi {
    announceBioageResult: (
        resultElement: HTMLElement | null,
        announcement: string
    ) => void;
    animateBioageResult: (
        resultElement: HTMLElement | null,
        finalAge: number,
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
    hasFiniteBiomarkerValue: (value: unknown) => boolean;
    hideUpdateModeStepNavigation: () => void;
    initializeBiomarkerEntry: (options: BioageBiomarkerEntryOptions) => BioageBiomarkerEntryResult;
    isUpdateMode: (search?: string) => boolean;
    isValidSelectedAthlete: (value: unknown) => value is BioageSelectedAthlete;
    navigateBack: (isUpdate: boolean) => void;
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

    function getBioageDraftKey(clock: BioageClock): string {
        return `bioageDraft:${clock}:v${BIOAGE_DRAFT_VERSION}`;
    }

    function isBioageClock(value: unknown): value is BioageClock {
        return value === 'pheno' || value === 'bortz';
    }

    function readBioageDraft(clock: BioageClock): BioageDraft | null {
        const raw = getSessionItem(getBioageDraftKey(clock));
        if (!raw) return null;

        try {
            const draft: unknown = JSON.parse(raw);
            if (!isObject(draft)
                || Reflect.get(draft, 'version') !== BIOAGE_DRAFT_VERSION
                || Reflect.get(draft, 'clock') !== clock
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
            removeSessionItem(getBioageDraftKey(clock));
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
            removeSessionItem(getBioageDraftKey(draftClock));
        });
    }

    function getDraftControls(form: HTMLFormElement): Array<HTMLInputElement | HTMLSelectElement> {
        return Array.from(form.querySelectorAll<HTMLInputElement | HTMLSelectElement>(
            '#dob-year, #dob-month, #dob-day, #blood-draw-date, .biomarker-card input[id], .biomarker-card select[id]'
        ));
    }

    function serializeDraftFields(form: HTMLFormElement): Record<string, BioageDraftField> {
        const fields: Record<string, BioageDraftField> = {};
        getDraftControls(form).forEach(control => {
            if (!control.id) return;

            const field: BioageDraftField = { value: control.value };
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
        if (controller.isUpdate || controller.restoring || controller.draftPersistenceSuppressed) return false;

        if (controller.hasPersistedDraft && getSessionItem(getBioageDraftKey(controller.clock)) === null) {
            controller.draftPersistenceSuppressed = true;
            if (controller.saveTimer) window.clearTimeout(controller.saveTimer);
            controller.saveTimer = 0;
            return false;
        }

        return true;
    }

    function saveBioageDraft(controller: BioageBiomarkerEntryController): void {
        controller.saveTimer = 0;
        if (!canPersistBioageDraft(controller)) return;
        const draft: BioageDraft = {
            version: BIOAGE_DRAFT_VERSION,
            clock: controller.clock,
            step: controller.step,
            fields: serializeDraftFields(controller.form)
        };

        setSessionItem(getBioageDraftKey(controller.clock), JSON.stringify(draft));
        controller.hasPersistedDraft = getSessionItem(getBioageDraftKey(controller.clock)) !== null;
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
        if (controller.isUpdate) return false;
        const draft = readBioageDraft(controller.clock);
        if (!draft) return false;

        controller.hasPersistedDraft = true;
        controller.restoring = true;
        try {
            applyDraftField(controller.form, draft.fields, 'dob-year');
            applyDraftField(controller.form, draft.fields, 'dob-month');
            controller.form.querySelector<HTMLSelectElement>('#dob-month')
                ?.dispatchEvent(new Event('change', { bubbles: true }));
            applyDraftField(controller.form, draft.fields, 'dob-day');

            Object.keys(draft.fields)
                .filter(id => !['dob-year', 'dob-month', 'dob-day'].includes(id))
                .forEach(id => applyDraftField(controller.form, draft.fields, id));

            const negativeCrp = controller.form.querySelector<HTMLInputElement>('#crp-negative');
            if (negativeCrp?.checked) {
                negativeCrp.dispatchEvent(new Event('change', { bubbles: true }));
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

    function setBiomarkerCardExpanded(card: HTMLElement, expanded: boolean): void {
        const header = card.querySelector<HTMLElement>('.biomarker-card-header');
        const icon = header?.querySelector<HTMLElement>('.toggle-icon');
        const content = card.querySelector<HTMLElement>('.biomarker-card-content');

        card.classList.toggle('active', expanded);
        header?.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        if (icon) icon.textContent = expanded ? '−' : '+';
        if (content) {
            content.style.maxHeight = expanded ? `${content.scrollHeight}px` : '0';
            content.style.paddingTop = expanded ? '0.75rem' : '0';
            content.style.paddingBottom = expanded ? '0.75rem' : '0';
        }
    }

    function expandBiomarkerCard(field: string | Element | null | undefined): void {
        const input = getBiomarkerInput(field);
        const card = input?.closest<HTMLElement>('.biomarker-card');
        if (!card || card.classList.contains('active')) return;
        setBiomarkerCardExpanded(card, true);
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
        error.hidden = !visible;
        input.setAttribute('aria-invalid', visible ? 'true' : 'false');
    }

    function ensureBiomarkerVisible(input: HTMLInputElement): void {
        const reveal = () => {
            const dock = getFlowActionDock();
            dock?.refreshNow?.();
            dock?.ensureClear?.(input, { margin: 16, behavior: 'auto' });

            const visualViewport = window.visualViewport;
            const top = visualViewport?.offsetTop || 0;
            const bottom = top + (visualViewport?.height || window.innerHeight);
            const rect = input.getBoundingClientRect();
            if (rect.bottom > bottom - 16 || rect.top < top + 16) {
                input.scrollIntoView({
                    block: 'center',
                    inline: 'nearest',
                    behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth'
                });
            }
        };

        window.requestAnimationFrame(() => window.requestAnimationFrame(reveal));
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

    function syncBiomarkerHeaderSemantics(controller: BioageBiomarkerEntryController): void {
        const isMobile = bioageMobileMedia.matches;
        controller.form.querySelectorAll<HTMLElement>('.biomarker-card-header').forEach(header => {
            const card = header.closest<HTMLElement>('.biomarker-card');
            const icon = header.querySelector<HTMLElement>('.toggle-icon');
            header.style.pointerEvents = '';
            header.style.cursor = '';
            header.removeAttribute('aria-disabled');
            icon?.setAttribute('aria-hidden', 'true');

            if (isMobile) {
                header.removeAttribute('role');
                header.removeAttribute('tabindex');
                header.removeAttribute('aria-expanded');
                if (icon) icon.textContent = '';
                return;
            }

            header.setAttribute('role', 'button');
            header.tabIndex = 0;
            header.setAttribute('aria-expanded', card?.classList.contains('active') ? 'true' : 'false');
            if (icon) icon.textContent = card?.classList.contains('active') ? '−' : '+';
        });
    }

    function syncBiomarkerCompletion(controller: BioageBiomarkerEntryController): void {
        let completed = 0;
        const incompleteInputs: HTMLInputElement[] = [];
        controller.inputs.forEach(input => {
            const isComplete = isCompleteBiomarkerInput(input);
            input.dataset.bioageComplete = isComplete ? 'true' : 'false';
            input.closest<HTMLElement>('.biomarker-card')
                ?.classList.toggle('biomarker-card--complete', isComplete);
            if (isComplete) {
                completed += 1;
                setBiomarkerError(input, false);
            } else if (controller.isUpdate && input.value.trim() === '') {
                setBiomarkerError(input, false);
            }
            if (!isComplete) incompleteInputs.push(input);
        });
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
            ? !hasBloodDrawDate && completed === 0
                ? 'Enter the blood draw date and at least 1 new biomarker value'
                : !hasBloodDrawDate
                    ? 'Enter the blood draw date'
                    : completed > 0
                        ? `${completed} biomarker${completed === 1 ? '' : 's'} ready to update`
                        : 'Enter at least 1 new biomarker value'
            : `${completed} of ${total} biomarkers entered`;
        controller.progress.classList.toggle('bioage-biomarker-progress--complete', ready);

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
        ensureBiomarkerVisible(input);
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
        controller.form.querySelectorAll<HTMLElement>('.biomarker-card-header').forEach(header => {
            if (header.dataset.bioageEntryBound === 'true') return;
            header.dataset.bioageEntryBound = 'true';

            const toggle = () => {
                if (bioageMobileMedia.matches) return;
                const card = header.closest<HTMLElement>('.biomarker-card');
                if (card) setBiomarkerCardExpanded(card, !card.classList.contains('active'));
            };
            header.addEventListener('click', toggle);
            header.addEventListener('keydown', event => {
                if (event.key !== 'Enter' && event.key !== ' ') return;
                event.preventDefault();
                toggle();
            });
        });

        controller.inputs.forEach(input => {
            syncFixedUnitPresentation(input);
            getOrCreateBiomarkerError(input);

            input.addEventListener('focus', () => {
                controller.visitedInputs.add(input);
                expandBiomarkerCard(input);
                ensureBiomarkerVisible(input);
            });
            input.addEventListener('input', () => {
                if (!controller.restoring) clearStoredBiomarkerHandoff();
                syncBiomarkerCompletion(controller);
                scheduleBioageDraftSave(controller);
            });
            input.addEventListener('blur', () => {
                if (controller.visitedInputs.has(input)
                    && !isCompleteBiomarkerInput(input)
                    && !(controller.isUpdate && input.value.trim() === '')) {
                    setBiomarkerError(input, true);
                }
                scheduleBioageDraftSave(controller);
            });
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
                    ensureBiomarkerVisible(input);
                    return;
                }
                moveToNextBiomarker(controller, input);
            });
        });

        controller.form.addEventListener('change', () => {
            if (!controller.restoring) clearStoredBiomarkerHandoff();
            syncBiomarkerCompletion(controller);
            scheduleBioageDraftSave(controller);
        });
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
                ensureBiomarkerVisible(input);
            });
            window.setTimeout(() => {
                controller.invalidBatchActive = false;
            }, 0);
        }, true);

        bioageMobileMedia.addEventListener?.('change', () => {
            syncBiomarkerHeaderSemantics(controller);
            getFlowActionDock()?.refresh?.();
        });
        window.addEventListener('pagehide', () => saveBioageDraft(controller));
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

        const controller: BioageBiomarkerEntryController = {
            clock: options.clock,
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
            step: 1,
            visitedInputs: new Set()
        };

        options.form.classList.add('bioage-biomarker-entry-ready');
        biomarkerEntryControllers.set(options.clock, controller);
        bindBiomarkerEntry(controller);
        syncBiomarkerHeaderSemantics(controller);

        const restoredDraft = options.restoreDraft !== false && restoreBioageDraft(controller);
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
    }

    function isUpdateMode(search?: string): boolean {
        return new URLSearchParams(search || window.location.search).get('update') === '1';
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
        finalAge: number,
        animationOptions: BioageResultAnimationOptions = {}
    ): void {
        if (!resultElement) return;

        const state = clearBioageResultAnimation(resultElement);
        const generation = state.generation;
        const container = getBioageResultValueContainer(resultElement);
        const visualValue = container?.querySelector<HTMLElement>('[data-bioage-result-visual]') || null;
        if (!container || !visualValue) return;

        syncBioageResultRevealTone(resultElement, container);
        if (!Number.isFinite(finalAge)) {
            visualValue.textContent = '';
            container.dataset.bioageRevealState = 'idle';
            return;
        }

        const finalText = formatBioageResultVisualValue(finalAge);
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

    function getBioageResultRevealScrollTop(resultElement: HTMLElement): number {
        const rect = resultElement.getBoundingClientRect();
        const viewportBounds = getBioageResultViewportBounds();
        const targetTop = rect.height >= viewportBounds.height
            ? viewportBounds.top
            : viewportBounds.top + ((viewportBounds.height - rect.height) / 2);

        return Math.max(0, window.scrollY + rect.top - targetTop);
    }

    function revealBioageResult(
        resultElement: HTMLElement | null,
        revealOptions: BioageResultRevealOptions = {}
    ): void {
        if (!resultElement) return;

        getFlowActionDock()?.refreshNow?.();

        if (isBioageResultComfortablyVisible(resultElement)) return;

        const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        window.scrollTo({
            top: getBioageResultRevealScrollTop(resultElement),
            behavior: prefersReducedMotion || revealOptions.instant ? 'auto' : 'smooth',
        });

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

    window.LwcBioageFlow = {
        announceBioageResult,
        animateBioageResult,
        bindBiomarkerComparison,
        clearBioageDraft,
        clearStoredBiomarkerHandoff,
        buildUnitSpecificBiomarkerPlaceholders,
        expandBiomarkerCard,
        getDraftStep,
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
