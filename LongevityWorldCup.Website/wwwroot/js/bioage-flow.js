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
        const example = examplesByUnit?.[unitText];

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
        clearStoredBiomarkerHandoff,
        getBackDestination,
        getBrowserStorageItem,
        getLocalItem,
        getSessionItem,
        hideUpdateModeStepNavigation,
        isUpdateMode,
        isValidSelectedAthlete,
        navigateBack,
        readSelectedAthlete,
        redirectMissingSelectedAthlete,
        removeBrowserStorageItem,
        removeLocalItem,
        removeSessionItem,
        setBrowserStorageItem,
        setLocalItem,
        setSessionItem,
        syncBiomarkerExamplePlaceholders,
        updateBiomarkerExamplePlaceholder,
        updateCalculateButton
    };
})();
