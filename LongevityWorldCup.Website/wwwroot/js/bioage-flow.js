"use strict";
(function () {
    function isObject(value) {
        return typeof value === 'object' && value !== null && !Array.isArray(value);
    }
    function removeBrowserStorageItem(storageName, key) {
        try {
            window[storageName].removeItem(key);
        }
        catch (_) {
        }
    }
    function getBrowserStorageItem(storageName, key) {
        try {
            return window[storageName].getItem(key);
        }
        catch (_) {
            return null;
        }
    }
    function setBrowserStorageItem(storageName, key, value) {
        try {
            window[storageName].setItem(key, value);
            return true;
        }
        catch (_) {
            return false;
        }
    }
    function setSessionItem(key, value) { return setBrowserStorageItem('sessionStorage', key, value); }
    function getSessionItem(key) { return getBrowserStorageItem('sessionStorage', key); }
    function removeSessionItem(key) { removeBrowserStorageItem('sessionStorage', key); }
    function setLocalItem(key, value) { return setBrowserStorageItem('localStorage', key, value); }
    function getLocalItem(key) { return getBrowserStorageItem('localStorage', key); }
    function removeLocalItem(key) { removeBrowserStorageItem('localStorage', key); }
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
    function normalizeUnitText(value) {
        return String(value || '').replace(/\s+/g, ' ').trim();
    }
    function toFiniteBiomarkerNumber(value) {
        if (value === null || value === undefined || typeof value === 'boolean')
            return null;
        if (typeof value === 'number')
            return Number.isFinite(value) ? value : null;
        if (typeof value === 'string') {
            const trimmed = value.trim();
            if (!trimmed)
                return null;
            const number = Number(trimmed);
            return Number.isFinite(number) ? number : null;
        }
        return null;
    }
    function hasFiniteBiomarkerValue(value) {
        return toFiniteBiomarkerNumber(value) !== null;
    }
    function formatBiomarkerPlaceholderValue(value) {
        const number = toFiniteBiomarkerNumber(value);
        if (number === null)
            return null;
        return Number(number.toFixed(2)).toString();
    }
    function readBiomarkerValue(entry, fieldNames) {
        if (!entry || typeof entry !== 'object' || Array.isArray(entry))
            return null;
        const fields = Array.isArray(fieldNames) ? fieldNames : [fieldNames];
        for (const field of fields) {
            const value = toFiniteBiomarkerNumber(Reflect.get(entry, field));
            if (value !== null)
                return value;
        }
        return null;
    }
    function getLatestBiomarkerEntry(athlete, fieldNames) {
        if (!isObject(athlete))
            return null;
        const biomarkers = Reflect.get(athlete, 'Biomarkers');
        if (!Array.isArray(biomarkers))
            return null;
        let latestEntry = null;
        let latestTime = Number.NEGATIVE_INFINITY;
        let latestIndex = -1;
        biomarkers.forEach((entry, index) => {
            if (!entry || typeof entry !== 'object' || Array.isArray(entry))
                return;
            if (fieldNames !== undefined && readBiomarkerValue(entry, fieldNames) === null)
                return;
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
    function getLatestBiomarkerValue(athlete, fieldNames) {
        const entry = getLatestBiomarkerEntry(athlete, fieldNames);
        if (!entry)
            return null;
        const value = readBiomarkerValue(entry, fieldNames);
        return value === null ? null : { entry, value };
    }
    function buildUnitSpecificBiomarkerPlaceholders(inputId, canonicalValue, displayValueForUnit) {
        const canonicalNumber = toFiniteBiomarkerNumber(canonicalValue);
        const select = document.getElementById(`${inputId}Unit`);
        if (canonicalNumber === null || !(select instanceof HTMLSelectElement))
            return null;
        const placeholderEntries = [];
        Array.from(select.options).forEach((option) => {
            const unitText = normalizeUnitText(option.textContent);
            if (!unitText)
                return;
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
    function readSubmittedPlaceholder(input, unitText) {
        if (!input?.dataset?.bioageSubmittedPlaceholders)
            return null;
        try {
            const placeholders = JSON.parse(input.dataset.bioageSubmittedPlaceholders);
            const value = isObject(placeholders) ? Reflect.get(placeholders, unitText) : undefined;
            return typeof value === 'string' && value !== '' ? value : null;
        }
        catch (_) {
            delete input.dataset.bioageSubmittedPlaceholders;
            return null;
        }
    }
    function cleanSubmittedPlaceholderMap(placeholdersByUnit) {
        if (!isObject(placeholdersByUnit))
            return null;
        const cleanedEntries = [];
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
    function setSubmittedBiomarkerPlaceholders(placeholdersByInputId) {
        const assignedIds = new Set();
        const placeholderEntries = isObject(placeholdersByInputId) ? Object.entries(placeholdersByInputId) : [];
        placeholderEntries.forEach(([inputId, placeholdersByUnit]) => {
            const input = document.getElementById(inputId);
            if (!(input instanceof HTMLInputElement))
                return;
            const cleaned = cleanSubmittedPlaceholderMap(placeholdersByUnit);
            if (cleaned) {
                input.dataset.bioageSubmittedPlaceholders = JSON.stringify(cleaned);
                assignedIds.add(inputId);
            }
            else {
                delete input.dataset.bioageSubmittedPlaceholders;
            }
            updateBiomarkerExamplePlaceholder(input);
        });
        document.querySelectorAll('input[data-bioage-submitted-placeholders]').forEach(input => {
            if (assignedIds.has(input.id))
                return;
            delete input.dataset.bioageSubmittedPlaceholders;
            updateBiomarkerExamplePlaceholder(input);
        });
    }
    const biomarkerComparisonBindings = new Map();
    function formatBiomarkerComparisonDelta(value) {
        const number = toFiniteBiomarkerNumber(value);
        if (number === null)
            return null;
        const abs = Math.abs(number);
        if (abs < 0.005)
            return '0';
        return abs < 10
            ? Number(abs.toFixed(2)).toString()
            : Number(abs.toFixed(1)).toString();
    }
    function ensureBiomarkerComparisonChip(input) {
        let chip = input.parentElement?.querySelector(`.bioage-input-comparison-chip[data-bioage-comparison-for="${input.id}"]`);
        if (chip)
            return chip;
        chip = document.createElement('span');
        chip.className = 'bioage-input-comparison-chip';
        chip.dataset.bioageComparisonFor = input.id;
        chip.hidden = true;
        input.parentElement?.appendChild(chip);
        return chip;
    }
    function hideBiomarkerComparison(input) {
        input.classList.remove('bioage-input-has-comparison');
        const chip = input.parentElement?.querySelector(`.bioage-input-comparison-chip[data-bioage-comparison-for="${input.id}"]`);
        if (chip) {
            chip.hidden = true;
            chip.textContent = '';
            chip.className = 'bioage-input-comparison-chip';
            chip.removeAttribute('title');
            chip.removeAttribute('aria-label');
        }
    }
    function setBiomarkerComparisonChipContent(chip, text, direction) {
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
    function updateBiomarkerComparison(inputId) {
        const input = document.getElementById(inputId);
        const binding = biomarkerComparisonBindings.get(inputId);
        if (!(input instanceof HTMLInputElement) || !binding)
            return;
        let state = null;
        try {
            state = binding.getState(input);
        }
        catch (_) {
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
    function bindBiomarkerComparison(inputId, getState) {
        const input = document.getElementById(inputId);
        if (!(input instanceof HTMLInputElement))
            return;
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
    function getBiomarkerInputForUnitSelect(select) {
        if (!select || !select.id || !select.id.endsWith('Unit'))
            return null;
        const input = document.getElementById(select.id.slice(0, -4));
        return input instanceof HTMLInputElement && input.matches('input[type="number"]') ? input : null;
    }
    function updateBiomarkerExamplePlaceholder(selectOrInput) {
        const selectCandidate = selectOrInput?.matches('select')
            ? selectOrInput
            : document.getElementById(`${selectOrInput?.id || ''}Unit`);
        const select = selectCandidate instanceof HTMLSelectElement ? selectCandidate : null;
        const inputCandidate = selectOrInput?.matches('input[type="number"]')
            ? selectOrInput
            : getBiomarkerInputForUnitSelect(select);
        const input = inputCandidate instanceof HTMLInputElement ? inputCandidate : null;
        if (!select || !input)
            return;
        const examplesByUnit = Object.hasOwn(biomarkerExamplePlaceholders, input.id)
            ? biomarkerExamplePlaceholders[input.id]
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
        }
        else {
            input.removeAttribute('placeholder');
        }
    }
    function syncBiomarkerExamplePlaceholders(root) {
        const scope = root || document;
        scope.querySelectorAll('.biomarker-card-content .input-group select[id$="Unit"]').forEach(select => {
            updateBiomarkerExamplePlaceholder(select);
            if (select.dataset.bioageExamplePlaceholderBound === 'true')
                return;
            select.dataset.bioageExamplePlaceholderBound = 'true';
            select.addEventListener('change', () => updateBiomarkerExamplePlaceholder(select));
        });
    }
    function isUpdateMode(search) {
        return new URLSearchParams(search || window.location.search).get('update') === '1';
    }
    function getBackDestination(isUpdate) {
        return isUpdate ? '/dashboard' : '/join';
    }
    function navigateBack(isUpdate) {
        window.navigateToFlowDestination(getBackDestination(isUpdate));
    }
    function resetUpdateModeScroll() {
        const reset = () => window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
        reset();
        window.requestAnimationFrame(() => {
            reset();
            getFlowActionDock()?.refresh?.();
        });
    }
    function getFlowActionDock() {
        return Reflect.get(window, 'LwcFlowActionDock');
    }
    function isValidSelectedAthlete(value) {
        if (!isObject(value))
            return false;
        const name = Reflect.get(value, 'Name');
        const dateOfBirth = Reflect.get(value, 'DateOfBirth');
        return typeof name === 'string'
            && name.trim().length > 0
            && hasSelectedAthleteDateOfBirth(dateOfBirth);
    }
    function hasSelectedAthleteDateOfBirth(value) {
        if (!isObject(value))
            return false;
        const year = toSelectedAthleteDatePart(Reflect.get(value, 'Year'), 1, 9999);
        const month = toSelectedAthleteDatePart(Reflect.get(value, 'Month'), 1, 12);
        const day = toSelectedAthleteDatePart(Reflect.get(value, 'Day'), 1, 31);
        if (year === null || month === null || day === null)
            return false;
        const date = new Date(0);
        date.setUTCFullYear(year, month - 1, day);
        date.setUTCHours(0, 0, 0, 0);
        return date.getUTCFullYear() === year
            && date.getUTCMonth() === month - 1
            && date.getUTCDate() === day;
    }
    function toSelectedAthleteDatePart(value, min, max) {
        if (typeof value === 'boolean' || value === null || value === undefined)
            return null;
        const number = typeof value === 'string' && value.trim()
            ? Number(value)
            : value;
        return typeof number === 'number' && Number.isInteger(number) && number >= min && number <= max
            ? number
            : null;
    }
    function readSelectedAthlete(getItem) {
        const readItem = typeof getItem === 'function' ? getItem : getSessionItem;
        try {
            const selectedAthleteJson = readItem('selectedAthlete');
            return selectedAthleteJson ? JSON.parse(selectedAthleteJson) : null;
        }
        catch (_) {
            return null;
        }
    }
    function redirectMissingSelectedAthlete(removeItem) {
        const remove = typeof removeItem === 'function' ? removeItem : removeSessionItem;
        remove('selectedAthlete');
        remove('tempAthlete');
        window.location.replace('/select-athlete');
    }
    function clearStoredBiomarkerHandoff(removeItem) {
        const remove = typeof removeItem === 'function' ? removeItem : removeSessionItem;
        remove('biomarkerData');
        remove('chronoPhenoDifference');
        remove('chronoBortzDifference');
    }
    function updateCalculateButton() {
        const calculateButton = document.querySelector('.bioage-calculate-button');
        const nextButton = document.getElementById('continueButton');
        if (!calculateButton || !nextButton)
            return;
        if (nextButton.classList.contains('show')) {
            calculateButton.classList.remove('green');
            calculateButton.classList.add('grey', 'flow-action--secondary');
        }
        else {
            calculateButton.classList.remove('grey', 'flow-action--secondary');
            calculateButton.classList.add('green');
        }
        syncBioageResultActions();
    }
    function syncBioageResultActions() {
        const nextButton = document.getElementById('continueButton');
        const resultActions = nextButton?.closest('.flow-action-stack');
        if (!nextButton || !resultActions || !document.body)
            return;
        const hasResult = nextButton.classList.contains('show');
        document.body.classList.toggle('bioage-result-ready', hasResult);
        resultActions.hidden = !hasResult;
        getFlowActionDock()?.refreshNow?.();
        if (hasResult) {
            scheduleBioageResultReveal(getShownBioageResultElement());
        }
        else {
            clearScheduledBioageResultReveals();
        }
        lastBioageResultActionsVisible = hasResult;
    }
    let lastBioageResultShown = false;
    let lastBioageResultActionsVisible = false;
    let resultRevealFrame = 0;
    function getShownBioageResultElement() {
        return document.querySelector('#phenoAgeResult.show, #bortzAgeResult.show');
    }
    function isRenderedElement(element) {
        if (!element)
            return false;
        const rect = element.getBoundingClientRect();
        const style = window.getComputedStyle(element);
        return rect.width > 0
            && rect.height > 0
            && style.display !== 'none'
            && style.visibility !== 'hidden';
    }
    function getCssPixelValue(element, propertyName) {
        const value = parseFloat(window.getComputedStyle(element).getPropertyValue(propertyName));
        return Number.isFinite(value) ? value : 0;
    }
    function getVisualViewportBounds() {
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
    function getBioageResultViewportBounds() {
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
    function isBioageResultComfortablyVisible(resultElement) {
        const rect = resultElement.getBoundingClientRect();
        const viewportBounds = getBioageResultViewportBounds();
        return rect.top >= viewportBounds.top
            && rect.bottom <= viewportBounds.bottom;
    }
    function getBioageResultRevealScrollTop(resultElement) {
        const rect = resultElement.getBoundingClientRect();
        const viewportBounds = getBioageResultViewportBounds();
        const targetTop = rect.height >= viewportBounds.height
            ? viewportBounds.top
            : viewportBounds.top + ((viewportBounds.height - rect.height) / 2);
        return Math.max(0, window.scrollY + rect.top - targetTop);
    }
    function revealBioageResult(resultElement, revealOptions = {}) {
        if (!resultElement)
            return;
        getFlowActionDock()?.refreshNow?.();
        if (isBioageResultComfortablyVisible(resultElement))
            return;
        const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        window.scrollTo({
            top: getBioageResultRevealScrollTop(resultElement),
            behavior: prefersReducedMotion || revealOptions.instant ? 'auto' : 'smooth',
        });
        window.setTimeout(() => getFlowActionDock()?.refresh?.(), prefersReducedMotion ? 0 : 360);
    }
    function clearScheduledBioageResultReveals() {
        if (resultRevealFrame) {
            window.cancelAnimationFrame(resultRevealFrame);
            resultRevealFrame = 0;
        }
    }
    function scheduleBioageResultReveal(resultElement) {
        if (!resultElement)
            return;
        clearScheduledBioageResultReveals();
        const revealIfCurrent = (instant = false) => {
            if (!isRenderedElement(resultElement))
                return;
            if (getShownBioageResultElement() !== resultElement)
                return;
            revealBioageResult(resultElement, { instant });
        };
        resultRevealFrame = window.requestAnimationFrame(() => {
            resultRevealFrame = window.requestAnimationFrame(() => {
                resultRevealFrame = 0;
                revealIfCurrent(false);
            });
        });
    }
    function syncBioageResultVisibility() {
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
    function bindBioageResultActions() {
        const nextButton = document.getElementById('continueButton');
        if (!nextButton)
            return;
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
    function hideUpdateModeStepNavigation() {
        const wizardNav = document.querySelector('.lwc-wizard-nav');
        if (wizardNav)
            wizardNav.hidden = true;
    }
    window.LwcBioageFlow = {
        bindBiomarkerComparison,
        clearStoredBiomarkerHandoff,
        buildUnitSpecificBiomarkerPlaceholders,
        getLatestBiomarkerEntry,
        getLatestBiomarkerValue,
        getBackDestination,
        getBrowserStorageItem,
        getLocalItem,
        getSessionItem,
        hasFiniteBiomarkerValue,
        hideUpdateModeStepNavigation,
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
    }
    else {
        bindBioageResultActions();
    }
})();
