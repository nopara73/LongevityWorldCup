(function () {
    function removeBrowserStorageItem(storageName, key) {
        try {
            window[storageName].removeItem(key);
        } catch (_) {
        }
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

    function hasFiniteBiomarkerValue(value) {
        return toFiniteBiomarkerNumber(value) !== null;
    }

    function formatBiomarkerPlaceholderValue(value) {
        const number = toFiniteBiomarkerNumber(value);
        if (number === null) return null;

        return Number(number.toFixed(2)).toString();
    }

    function readBiomarkerValue(entry, fieldNames) {
        if (!entry || typeof entry !== 'object' || Array.isArray(entry)) return null;

        const fields = Array.isArray(fieldNames) ? fieldNames : [fieldNames];
        for (const field of fields) {
            const value = toFiniteBiomarkerNumber(entry[field]);
            if (value !== null) return value;
        }

        return null;
    }

    function getLatestBiomarkerEntry(athlete, fieldNames) {
        if (!athlete || !Array.isArray(athlete.Biomarkers)) return null;

        let latestEntry = null;
        let latestTime = Number.NEGATIVE_INFINITY;
        let latestIndex = -1;

        athlete.Biomarkers.forEach((entry, index) => {
            if (!entry || typeof entry !== 'object' || Array.isArray(entry)) return;
            if (fieldNames !== undefined && readBiomarkerValue(entry, fieldNames) === null) return;

            const parsedTime = Date.parse(entry.Date);
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
        const value = readBiomarkerValue(entry, fieldNames);
        return value === null ? null : { entry, value };
    }

    function buildUnitSpecificBiomarkerPlaceholders(inputId, canonicalValue, displayValueForUnit) {
        const canonicalNumber = toFiniteBiomarkerNumber(canonicalValue);
        const select = document.getElementById(`${inputId}Unit`);
        if (canonicalNumber === null || !select) return null;

        const placeholders = {};
        Array.from(select.options || []).forEach(option => {
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
                placeholders[unitText] = formatted;
            }
        });

        return Object.keys(placeholders).length ? placeholders : null;
    }

    function readSubmittedPlaceholder(input, unitText) {
        if (!input?.dataset?.bioageSubmittedPlaceholders) return null;

        try {
            const placeholders = JSON.parse(input.dataset.bioageSubmittedPlaceholders);
            const value = placeholders?.[unitText];
            return typeof value === 'string' && value !== '' ? value : null;
        } catch (_) {
            delete input.dataset.bioageSubmittedPlaceholders;
            return null;
        }
    }

    function cleanSubmittedPlaceholderMap(placeholdersByUnit) {
        if (!placeholdersByUnit || typeof placeholdersByUnit !== 'object' || Array.isArray(placeholdersByUnit)) return null;

        const cleaned = {};
        Object.entries(placeholdersByUnit).forEach(([unitText, value]) => {
            const normalizedUnitText = normalizeUnitText(unitText);
            const formatted = typeof value === 'string' && value.trim()
                ? value.trim()
                : formatBiomarkerPlaceholderValue(value);

            if (normalizedUnitText && formatted !== null) {
                cleaned[normalizedUnitText] = formatted;
            }
        });

        return Object.keys(cleaned).length ? cleaned : null;
    }

    function setSubmittedBiomarkerPlaceholders(placeholdersByInputId) {
        const assignedIds = new Set();
        Object.entries(placeholdersByInputId || {}).forEach(([inputId, placeholdersByUnit]) => {
            const input = document.getElementById(inputId);
            if (!input) return;

            const cleaned = cleanSubmittedPlaceholderMap(placeholdersByUnit);
            if (cleaned) {
                input.dataset.bioageSubmittedPlaceholders = JSON.stringify(cleaned);
                assignedIds.add(inputId);
            } else {
                delete input.dataset.bioageSubmittedPlaceholders;
            }

            updateBiomarkerExamplePlaceholder(input);
        });

        document.querySelectorAll('input[data-bioage-submitted-placeholders]').forEach(input => {
            if (assignedIds.has(input.id)) return;

            delete input.dataset.bioageSubmittedPlaceholders;
            updateBiomarkerExamplePlaceholder(input);
        });
    }

    const biomarkerComparisonBindings = new Map();

    function formatBiomarkerComparisonDelta(value) {
        const number = toFiniteBiomarkerNumber(value);
        if (number === null) return null;

        const abs = Math.abs(number);
        if (abs < 0.005) return '0';
        return abs < 10
            ? Number(abs.toFixed(2)).toString()
            : Number(abs.toFixed(1)).toString();
    }

    function ensureBiomarkerComparisonChip(input) {
        let chip = input.parentElement?.querySelector(`.bioage-input-comparison-chip[data-bioage-comparison-for="${input.id}"]`);
        if (chip) return chip;

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

    function updateBiomarkerComparison(inputId) {
        const input = document.getElementById(inputId);
        const binding = biomarkerComparisonBindings.get(inputId);
        if (!input || !binding || typeof binding.getState !== 'function') return;

        let state = null;
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
            : `${displayDelta < 0 ? '↓' : '↑'} ${deltaMagnitude} ${displayDelta < 0 ? 'lower' : 'higher'}`;
        const chip = ensureBiomarkerComparisonChip(input);
        chip.className = `bioage-input-comparison-chip ${stateClass}`;
        chip.textContent = text;
        chip.hidden = false;
        chip.title = `Last ${formatBiomarkerPlaceholderValue(previousDisplay)}`;
        chip.setAttribute('aria-label', `${text}; last ${formatBiomarkerPlaceholderValue(previousDisplay)}`);
        input.classList.add('bioage-input-has-comparison');
    }

    function bindBiomarkerComparison(inputId, getState) {
        const input = document.getElementById(inputId);
        if (!input || typeof getState !== 'function') return;

        biomarkerComparisonBindings.set(inputId, { getState });
        ensureBiomarkerComparisonChip(input);

        if (input.dataset.bioageComparisonBound !== 'true') {
            input.dataset.bioageComparisonBound = 'true';
            input.addEventListener('input', () => updateBiomarkerComparison(inputId));

            const unitSelect = document.getElementById(`${inputId}Unit`);
            if (unitSelect) {
                unitSelect.addEventListener('change', () => updateBiomarkerComparison(inputId));
            }
        }

        updateBiomarkerComparison(inputId);
    }

    function getBiomarkerInputForUnitSelect(select) {
        if (!select || !select.id || !select.id.endsWith('Unit')) return null;

        const input = document.getElementById(select.id.slice(0, -4));
        return input && input.matches('input[type="number"]') ? input : null;
    }

    function updateBiomarkerExamplePlaceholder(selectOrInput) {
        const select = selectOrInput?.matches?.('select')
            ? selectOrInput
            : document.getElementById(`${selectOrInput?.id || ''}Unit`);
        const input = selectOrInput?.matches?.('input[type="number"]')
            ? selectOrInput
            : getBiomarkerInputForUnitSelect(select);

        if (!select || !input) return;

        const examplesByUnit = biomarkerExamplePlaceholders[input.id];
        const selectedOption = select.options[select.selectedIndex];
        const unitText = normalizeUnitText(selectedOption?.textContent);
        const submittedExample = readSubmittedPlaceholder(input, unitText);
        const example = submittedExample ?? examplesByUnit?.[unitText];

        if (example) {
            input.placeholder = example;
        } else {
            input.removeAttribute('placeholder');
        }
    }

    function syncBiomarkerExamplePlaceholders(root) {
        const scope = root || document;
        scope.querySelectorAll('.biomarker-card-content .input-group select[id$="Unit"]').forEach(select => {
            updateBiomarkerExamplePlaceholder(select);

            if (select.dataset.bioageExamplePlaceholderBound === 'true') return;

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

    function isValidSelectedAthlete(value) {
        return value
            && typeof value === 'object'
            && !Array.isArray(value)
            && typeof value.Name === 'string'
            && value.Name.trim()
            && hasSelectedAthleteDateOfBirth(value.DateOfBirth);
    }

    function hasSelectedAthleteDateOfBirth(value) {
        if (!value || typeof value !== 'object' || Array.isArray(value)) return false;

        const year = toSelectedAthleteDatePart(value.Year, 1, 9999);
        const month = toSelectedAthleteDatePart(value.Month, 1, 12);
        const day = toSelectedAthleteDatePart(value.Day, 1, 31);

        if (year === null || month === null || day === null) return false;

        const date = new Date(0);
        date.setUTCFullYear(year, month - 1, day);
        date.setUTCHours(0, 0, 0, 0);
        return date.getUTCFullYear() === year
            && date.getUTCMonth() === month - 1
            && date.getUTCDate() === day;
    }

    function toSelectedAthleteDatePart(value, min, max) {
        if (typeof value === 'boolean' || value === null || value === undefined) return null;

        const number = typeof value === 'string' && value.trim()
            ? Number(value)
            : value;
        return Number.isInteger(number) && number >= min && number <= max
            ? number
            : null;
    }

    function readSelectedAthlete(getItem) {
        const readItem = typeof getItem === 'function' ? getItem : getSessionItem;
        try {
            const selectedAthleteJson = readItem('selectedAthlete');
            return selectedAthleteJson ? JSON.parse(selectedAthleteJson) : null;
        } catch (_) {
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
        const calculateButton = document.querySelector('form button[type="submit"]');
        const nextButton = document.getElementById('continueButton');
        if (!calculateButton || !nextButton) return;

        if (nextButton.classList.contains('show')) {
            calculateButton.classList.remove('green');
            calculateButton.classList.add('grey', 'flow-action--secondary');
        } else {
            calculateButton.classList.remove('grey', 'flow-action--secondary');
            calculateButton.classList.add('green');
        }
    }

    function hideUpdateModeStepNavigation() {
        const wizardNav = document.querySelector('.lwc-wizard-nav');
        if (wizardNav) wizardNav.hidden = true;

        const stepBackButton = document.getElementById('lwcToStep1Btn');
        const stepBackActions = stepBackButton?.closest('.lwc-step-actions');
        if (stepBackActions) stepBackActions.hidden = true;
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
        setBrowserStorageItem,
        setLocalItem,
        setSubmittedBiomarkerPlaceholders,
        setSessionItem,
        syncBiomarkerExamplePlaceholders,
        toFiniteBiomarkerNumber,
        updateBiomarkerComparison,
        updateBiomarkerExamplePlaceholder,
        updateCalculateButton
    };
})();
