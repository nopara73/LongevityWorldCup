/* Frozen research model evaluator. No biomarker values leave the browser. */
(function () {
    'use strict';
    const featureNames = { sbp: 'Systolic pressure', dbp: 'Diastolic pressure', whr: 'Waist-to-height ratio', hba1c: 'HbA1c', apob: 'ApoB', crp: 'hs-CRP', cystatin: 'Cystatin C', grip: 'Grip strength', vo2: 'VO₂max' };
    const cube = x => Math.max(x, 0) ** 3;
    function restrictedCubic(x, knots) {
        const [a, b, c] = knots;
        return (cube(x - a) - cube(x - b) * (c - a) / (c - b) + cube(x - c) * (b - a) / (c - b)) / (c - a) ** 2;
    }
    function integral(gamma, years) {
        return Math.abs(gamma) < 1e-5
            ? years + gamma * years ** 2 / 2 + gamma ** 2 * years ** 3 / 6
            : Math.expm1(gamma * years) / gamma;
    }
    function contribution(value, male, feature, model, coefficients = model.coefficients) {
        const curve = model.curves[feature];
        const x = curve.log ? Math.log(value) : value;
        const z = (x - curve.center) / curve.scale;
        let result = coefficients[`${feature}_linear`] * z + coefficients[`${feature}_sex`] * z * (male - 0.5);
        if (model.smooth) result += coefficients[`${feature}_nonlinear`] * (restrictedCubic(x, curve.knots) - curve.nonlinearCenter) / curve.nonlinearScale;
        return result;
    }
    function cumulativeHazard(inputs, model, coefficients = model.coefficients, gamma = model.gamma) {
        const s = inputs.male;
        let eta = coefficients.intercept + coefficients.male * s;
        for (const f of model.features) eta += contribution(inputs[f], s, f, model, coefficients);
        return Math.exp(eta) * integral(gamma[s], 5);
    }
    function invertReferenceHazard(hazard, male, reference) {
        const b = reference.coefficients;
        return 45 + 10 * (Math.log(hazard) - Math.log(integral(reference.gamma[male], 5)) - b.intercept - b.male * male) / b[male === 1 ? 'age_male' : 'age_female'];
    }
    function ageFromHazard(hazard, male, reference) {
        const age = invertReferenceHazard(hazard, male, reference);
        return Number.isFinite(age) && age >= 18 && age <= 79 ? age : null;
    }
    function quantile(values, probability) {
        const sorted = [...values].sort((a, b) => a - b);
        const position = probability * (sorted.length - 1);
        const i = Math.floor(position);
        return sorted[i] + (sorted[Math.min(i + 1, sorted.length - 1)] - sorted[i]) * (position - i);
    }
    function calculate(inputs, model) {
        if (model.requiresChronologicalAge !== false || 'age_female' in model.coefficients || 'age_male' in model.coefficients) {
            return { supported: false, reason: 'The age-free research model is unavailable. Reload and try again.' };
        }
        if (model.releaseStatus === 'withheld') return { supported: false, reason: model.releaseReason };
        if (![0, 1].includes(inputs.male)) return { supported: false, reason: 'Select female or male sex.' };
        const problems = [];
        for (const feature of model.features) {
            const value = inputs[feature];
            const [lo, hi] = model.curves[feature].support[String(inputs.male)];
            if (!Number.isFinite(value) || value <= 0) problems.push(`${featureNames[feature]} is missing or invalid`);
            else if (value < lo || value > hi) problems.push(`${featureNames[feature]} is outside the study's supported range`);
        }
        if (problems.length) return { supported: false, reason: problems.join('; ') + '.' };
        const hazard = cumulativeHazard(inputs, model);
        const risk = -Math.expm1(-hazard);
        const referenceAge = invertReferenceHazard(hazard, inputs.male, model.reference);
        if (!Number.isFinite(referenceAge) || !Number.isFinite(risk)) return { supported: false, reason: 'The mortality-equivalent age could not be calculated.' };
        const age = ageFromHazard(hazard, inputs.male, model.reference);
        const result = { supported: true, age, risk5: risk, status: model.status, features: [...model.features] };
        // Outside the reference, return only its boundary, never an extrapolated exact age.
        if (age === null) result.ageBoundary = referenceAge < 18 ? '<18' : '>79';
        if (model.bootstrap?.length) {
            const ages = model.bootstrap.map(b => ageFromHazard(cumulativeHazard(inputs, model, b.coefficients, b.gamma), inputs.male, model.reference));
            if (ages.every(Number.isFinite)) result.samplingRange80 = [quantile(ages, 0.1), quantile(ages, 0.9)];
            else result.samplingRangeUnsupported = true;
        }
        if (model.dependence?.length) {
            const ages = [age, ...model.dependence.map(b => ageFromHazard(cumulativeHazard(inputs, model, b.coefficients, b.gamma), inputs.male, model.reference))];
            if (ages.every(Number.isFinite)) result.dependenceRange = [Math.min(...ages), Math.max(...ages)];
            else result.dependenceRangeUnsupported = true;
        }
        return result;
    }
    function convert(feature, value, unit, belowLimit = false) {
        if (!Number.isFinite(value) || value <= 0) return NaN;
        const factors = {
            sbp: { mmHg: 1, kPa: 7.500616827 }, dbp: { mmHg: 1, kPa: 7.500616827 },
            whr: { ratio: 1, percent: .01 }, apob: { 'mg/dL': 1, 'g/L': 100 },
            crp: { 'mg/L': 1, 'mg/dL': 10 }, cystatin: { 'mg/L': 1, 'mg/dL': 10 },
            grip: { kg: 1, lb: .45359237 }, vo2: { 'mL/kg/min': 1 }
        };
        let converted;
        if (feature === 'hba1c') converted = unit === '%' ? value : unit === 'mmol/mol' ? .09148 * value + 2.152 : NaN;
        else converted = value * (factors[feature]?.[unit] ?? NaN);
        return belowLimit && ['crp', 'cystatin'].includes(feature) ? converted / Math.sqrt(2) : converted;
    }
    window.LwcMortalityAgeModel = Object.freeze({ calculate, contribution, convert, cumulativeHazard, ageFromHazard });
})();
