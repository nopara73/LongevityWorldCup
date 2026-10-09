(function () {
    'use strict';
    const modelUrl = document.currentScript.dataset.modelUrl;
    window.LwcInitialView.run(() => {
        const storageKey = 'lwc-mortality-age-draft-v1';
        const $ = id => document.getElementById(id);
        const form = $('mortalityAgeForm');
        const evaluator = window.LwcMortalityAgeModel;
        let bundle = null, step = 1, hasResult = false, restoring = false;
        const fields = [
            { id: 'sbp', label: 'Systolic blood pressure', group: 'Circulation', icon: 'heart-pulse', units: ['mmHg', 'kPa'], example: 115 },
            { id: 'dbp', label: 'Diastolic blood pressure', group: 'Circulation', units: ['mmHg', 'kPa'], example: 75, note: 'Rest seated for five minutes. Average available readings from one visit.' },
            { id: 'vo2', label: 'Estimated VO₂max', group: 'Fitness', icon: 'person-running', units: ['mL/kg/min'], example: 40, note: 'The model was trained on exercise-based treadmill estimates, ages 18–49.' },
            { id: 'grip', label: 'Grip strength', group: 'Strength', icon: 'hand-fist', units: ['kg', 'lb'], example: 35, note: 'Highest maximal-effort dynamometer reading from either hand. Do not add the hands.' },
            { id: 'whr', label: 'Waist-to-height ratio', group: 'Body composition', icon: 'ruler', units: ['ratio', 'percent'], example: .5, note: 'Waist circumference divided by standing height, using the same unit.' },
            { id: 'apob', label: 'Apolipoprotein B (ApoB)', group: 'Lipids', icon: 'droplet', units: ['mg/dL', 'g/L'], example: 90 },
            { id: 'hba1c', label: 'Hemoglobin A1c (HbA1c)', group: 'Metabolism', icon: 'fire', units: ['%', 'mmol/mol'], example: 5.3 },
            { id: 'cystatin', label: 'Cystatin C', group: 'Kidney', icon: 'filter', units: ['mg/L', 'mg/dL'], example: .8, limit: true },
            { id: 'crp', label: 'High-sensitivity C-reactive protein (hs-CRP)', group: 'Inflammation', icon: 'temperature-half', units: ['mg/L', 'mg/dL'], example: 1, limit: true }
        ];
        const panelNames = { core: 'BP · waist · HbA1c', blood: 'Core · hs-CRP · cystatin C', lipid: 'Core · ApoB · hs-CRP', strength: 'Core · ApoB · grip', fitness: 'Core · hs-CRP · cystatin C · VO₂max' };
        const controllers = new Map();
        const today = new Date();
        const localDate = date => `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;
        for (let year = today.getFullYear() - 18; year >= today.getFullYear() - 100; year--) $('dob-year').add(new Option(String(year), String(year)));
        for (let month = 1; month <= 12; month++) $('dob-month').add(new Option(new Date(2000, month-1).toLocaleString(undefined, { month: 'long' }), String(month)));
        $('dob-month').value = '12';
        function syncDays() {
            const previous = $('dob-day').value || '31';
            const count = new Date(Number($('dob-year').value) || 2000, Number($('dob-month').value), 0).getDate();
            $('dob-day').replaceChildren();
            for (let day = 1; day <= count; day++) $('dob-day').add(new Option(String(day), String(day)));
            $('dob-day').value = String(Math.min(Number(previous), count));
        }
        syncDays();
        $('measurement-date').value = localDate(today);
        $('measurement-date').max = localDate(today);
        const cards = $('measurement-cards');
        let fieldset;
        for (const spec of fields) {
            if (spec.icon) {
                fieldset = document.createElement('fieldset');
                const legend = document.createElement('legend');
                const icon = document.createElement('i');
                icon.className = `fas fa-${spec.icon} category-icon`;
                icon.setAttribute('aria-hidden', 'true');
                legend.append(icon, document.createTextNode(spec.group));
                fieldset.append(legend);
                cards.append(fieldset);
            }
            const card = document.createElement('div');
            card.className = 'biomarker-card';
            card.innerHTML = `<button type="button" class="biomarker-card-header" aria-expanded="false" aria-controls="${spec.id}-content"><span>${spec.label}</span><span class="toggle-icon" aria-hidden="true">+</span></button><div id="${spec.id}-content" class="biomarker-card-content" hidden><div class="input-group"><input type="number" id="${spec.id}" name="${spec.id}" step="any" min="0.000001" required inputmode="decimal" aria-label="${spec.label}" aria-describedby="${spec.id}-error" placeholder="${spec.example}"><select id="${spec.id}-unit" aria-label="${spec.label} unit"></select></div><p id="${spec.id}-error" class="field-error" hidden></p></div>`;
            const unitSelect = card.querySelector('select');
            for (const unit of spec.units) unitSelect.add(new Option(unit, unit));
            const content = card.querySelector('.biomarker-card-content');
            if (spec.id === 'vo2') {
                const method = document.createElement('select');
                method.id = 'vo2-method';
                method.setAttribute('aria-label', 'VO₂max estimation method');
                method.add(new Option('Exercise-based estimate', 'exercise'));
                method.add(new Option('Wearable / non-exercise estimate', 'other'));
                content.append(method);
            }
            if (spec.limit) {
                const label = document.createElement('label');
                label.className = 'limit-label';
                label.innerHTML = `<input type="checkbox" id="${spec.id}-limit"> Below reporting limit`;
                label.title = 'Enter the laboratory reporting limit. The model uses limit / √2.';
                content.append(label);
            }
            if (spec.note) {
                const note = document.createElement('p');
                note.className = 'measurement-note';
                note.textContent = spec.note;
                content.append(note);
            }
            card.querySelector('button').addEventListener('click', () => {
                const open = content.hidden;
                content.hidden = !open;
                card.classList.toggle('active', open);
                card.querySelector('button').setAttribute('aria-expanded', String(open));
                card.querySelector('.toggle-icon').textContent = open ? '−' : '+';
                if (open) $(spec.id).focus({ preventScroll: true });
            });
            fieldset.append(card);
        }
        function age() {
            const year = Number($('dob-year').value);
            const month = Number($('dob-month').value);
            const day = Number($('dob-day').value);
            const draw = $('measurement-date').value;
            if (!year || !draw || month < 1 || month > 12 || day < 1 || day > 31) return NaN;
            const birth = Date.UTC(year, month-1, day);
            if (new Date(birth).getUTCMonth() !== month-1) return NaN;
            const measured = Date.parse(draw+'T00:00:00Z');
            return (measured-birth)/86400000/365.2425;
        }
        function firstStepValid() {
            const years = age();
            $('dob-year').setCustomValidity(Number.isFinite(years) && (years < 18 || years >= 80) ? 'This research calculator supports ages 18–79 at measurement.' : '');
            return ['dob-year', 'measurement-date', 'sex'].every(id => $(id).validity.valid && $(id).value !== '') && age() >= 18 && age() < 80;
        }
        function ready() {
            $('continue-button').disabled = !firstStepValid();
            $('lwcDot2').disabled = !firstStepValid();
            $('calculate-button').disabled = !bundle || !firstStepValid() || fields.some(f => !$(f.id).value || !$(f.id).validity.valid);
        }
        function saveDraft() {
            if (restoring) return;
            try {
                const draft = { step, values: {} };
                form.querySelectorAll('input, select').forEach(input => draft.values[input.id] = input.type === 'checkbox' ? input.checked : input.value);
                localStorage.setItem(storageKey, JSON.stringify(draft));
            } catch { /* Private browsing may deny storage. Calculation still works. */ }
        }
        function showStep(value, focus = true) {
            step = value;
            form.hidden = false;
            $('mortalityAgeResult').hidden = true;
            document.body.classList.remove('bioage-result-ready');
            for (const s of [1, 2]) {
                $(`lwc-step-${s}`).classList.toggle('lwc-step--active', s === value);
                $(`lwcDot${s}`).classList.toggle('lwc-dot--on', s === value);
                $(`lwcDot${s}`).setAttribute('aria-current', s === value ? 'step' : 'false');
            }
            controllers.forEach(c => c.reset());
            if (focus) (value === 1 ? $('dob-year') : $('sbp').closest('.biomarker-card').querySelector('button')).focus({ preventScroll: true });
            saveDraft();
        }
        form.querySelectorAll('input, select').forEach(input => {
            const errorId = fields.some(f => f.id === input.id) ? `${input.id}-error` : `${input.id}-validation`;
            let error = $(errorId);
            if (!error) {
                error = document.createElement('p'); error.id = errorId; error.hidden = true; error.className = 'field-error'; input.after(error);
            }
            const controller = window.LwcFieldValidation.bind(input, {
                isRestoring: () => restoring,
                clear: () => { input.removeAttribute('aria-invalid'); error.hidden = true; },
                validate: () => {
                    const valid = input.validity.valid;
                    error.textContent = valid ? '' : input.validationMessage;
                    error.hidden = valid; input.setAttribute('aria-invalid', String(!valid));
                    return valid;
                }
            });
            controllers.set(input.id, controller);
        });
        $('dob-year').addEventListener('change', syncDays);
        $('dob-month').addEventListener('change', syncDays);
        form.addEventListener('input', () => { hasResult = false; ready(); saveDraft(); });
        form.addEventListener('change', () => { hasResult = false; ready(); saveDraft(); });
        $('continue-button').addEventListener('click', () => { if (firstStepValid()) showStep(2); });
        $('back-button').addEventListener('click', () => showStep(1));
        $('lwcDot1').addEventListener('click', () => showStep(1));
        $('lwcDot2').addEventListener('click', () => { if (firstStepValid()) showStep(2); });
        $('edit-button').addEventListener('click', () => showStep(2));
        try {
            const draft = JSON.parse(localStorage.getItem(storageKey) || 'null');
            if (draft && draft.values && typeof draft.values === 'object') {
                restoring = true;
                for (const id of ['dob-year', 'dob-month']) {
                    const value = draft.values[id];
                    if (typeof value === 'string' && Array.from($(id).options).some(o => o.value === value)) $(id).value = value;
                }
                syncDays();
                form.querySelectorAll('input, select').forEach(input => {
                    const value = draft.values[input.id];
                    if (input.type === 'checkbox' && typeof value === 'boolean') input.checked = value;
                    else if (typeof value === 'string' && (input.tagName !== 'SELECT' || Array.from(input.options).some(o => o.value === value))) input.value = value;
                });
                restoring = false;
                showStep(draft.step === 2 && firstStepValid() ? 2 : 1, false);
            }
        } catch { restoring = false; }
        async function loadModel() {
            bundle = null; ready(); $('retry-model').hidden = true; $('model-status').textContent = 'Loading the research model…';
            const abort = new AbortController();
            const timer = setTimeout(() => abort.abort(), 10000);
            try {
                const response = await fetch(modelUrl, { signal: abort.signal, cache: 'no-cache' });
                if (!response.ok) throw new Error('Model unavailable');
                const loaded = await response.json();
                if (loaded.schemaVersion !== 1 || loaded.full?.status !== 'experimental-integration' || !loaded.panels || !loaded.modelVersion) throw new Error('Unsupported model');
                bundle = loaded;
                $('model-status').textContent = 'Eight domains · sex-dependent curves · five-year mortality';
            } catch {
                $('model-status').textContent = 'The research model could not load.';
                $('retry-model').hidden = false;
            } finally { clearTimeout(timer); ready(); }
        }
        $('retry-model').addEventListener('click', loadModel);
        form.addEventListener('submit', async event => {
            event.preventDefault();
            if ($('calculate-button').disabled || hasResult) return;
            const inputs = { age: age(), male: Number($('sex').value) };
            for (const field of fields) inputs[field.id] = evaluator.convert(field.id, Number($(field.id).value), $(`${field.id}-unit`).value, $(`${field.id}-limit`)?.checked);
            if (inputs.sbp <= inputs.dbp) {
                const error = $('sbp-error'); error.textContent = 'Systolic pressure must be higher than diastolic pressure.'; error.hidden = false;
                const header = $('sbp').closest('.biomarker-card').querySelector('button');
                if (header.getAttribute('aria-expanded') !== 'true') header.click();
                $('sbp').focus(); return;
            }
            const methodSupported = $('vo2-method').value === 'exercise';
            const result = methodSupported ? evaluator.calculate(inputs, bundle.full) : { supported: false, reason: 'Wearable and non-exercise fitness estimates have not been calibrated to this model.' };
            $('validAgeInput').hidden = !result.supported;
            $('unsupported-result').hidden = result.supported;
            $('unsupported-result').textContent = result.supported ? '' : 'Full-panel age unavailable. ' + result.reason;
            $('result-risk').textContent = result.supported ? `Estimated five-year mortality: ${(result.risk5*100).toFixed(2)}%.` : '';
            if (result.samplingRange80) $('result-risk').textContent += ` Conditional sampling range (80%): ${result.samplingRange80.map(a => a.toFixed(1)).join('–')} years.`;
            if (result.dependenceRange) $('result-risk').textContent += ` Tested dependence scenarios: ${result.dependenceRange.map(a => a.toFixed(1)).join('–')} years.`;
            $('panel-results').replaceChildren();
            $('panel-results').parentElement.open = !result.supported;
            for (const [label, model] of Object.entries(bundle.panels)) {
                const panel = label === 'fitness' && !methodSupported ? { supported: false } : evaluator.calculate(inputs, model);
                const row = document.createElement('div'); row.className = 'panel-row';
                const term = document.createElement('dt'); term.textContent = panelNames[label];
                const value = document.createElement('dd'); value.textContent = panel.supported ? `${panel.age.toFixed(1)} years` : 'Unsupported';
                if (!panel.supported && panel.reason) value.title = panel.reason;
                row.append(term, value); $('panel-results').append(row);
            }
            form.hidden = true; $('mortalityAgeResult').hidden = false; $('mortalityAgeResult').classList.add('show');
            document.body.classList.add('bioage-result-ready');
            hasResult = true;
            if (result.supported) {
                $('yearsText').textContent = `${Math.abs(result.age-inputs.age).toFixed(1)} years ${result.age < inputs.age ? 'below' : 'above'} your chronological age`;
                window.LwcBioageFlow.announceBioageResult($('mortalityAgeResult'), `Experimental mortality-equivalent age: ${result.age.toFixed(1)} years. The full panel has not been jointly validated.`);
                $('animatedAge').textContent = result.age.toFixed(1);
                window.LwcBioageFlow.animateBioageResult($('mortalityAgeResult'), result.age);
            }
            $('mortalityAgeResult').scrollIntoView({ block: 'start', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' });
            $('edit-button').focus({ preventScroll: true });
        });
        ready();
        window.LwcInitialView.complete();
        loadModel();
    });
})();
