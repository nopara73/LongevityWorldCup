// Bortz Age / Biological Age Acceleration (BAA) from Bortz et al. 2023
// https://www.nature.com/articles/s42003-023-05456-z
// Coefficients and Scottish test-set means from https://github.com/bortzjd/bloodmarker_BA_estimation
// BAA = 10 * sum((x_i - mean_i) * baaCoeff_i). Biological age = chronological age + BAA.
// Excludes MSCV, PDW, PCT, and reticulocytes.

window.BortzAge = window.BortzAge || {};

// cap: value; capMode: 'floor' | 'ceiling' (PhenoAge-style; no creatinine cap in Bortz)
window.BortzAge.features = [
    { id: 'age', name: 'Age', mean: 56.0487752, baaCoeff: 0.074763266 - 0.100432393, isLog: false },
    { id: 'albumin', name: 'Albumin', mean: 45.1238763, baaCoeff: -0.011331946, isLog: false },
    { id: 'alp', name: 'Alkaline phosphatase', mean: 82.6847975, baaCoeff: 0.00164946, isLog: false },
    { id: 'urea', name: 'Urea', mean: 5.3547152, baaCoeff: -0.029554872, isLog: false },
    { id: 'cholesterol', name: 'Total Cholesterol', mean: 5.6177437, baaCoeff: -0.0805656, isLog: false },
    { id: 'creatinine', name: 'Creatinine', mean: 71.565605, baaCoeff: -0.01095746, isLog: false },
    { id: 'cystatin_c', name: 'Cystatin C', mean: 0.900946, baaCoeff: 1.859556436, isLog: false },
    { id: 'hba1c', name: 'Hemoglobin A1c (HbA1c)', mean: 35.4785711, baaCoeff: 0.018116675, isLog: false },
    { id: 'crp', name: 'C-Reactive Protein (CRP)', mean: 0.3003624, baaCoeff: 0.079109916, isLog: true },
    { id: 'ggt', name: 'Gamma-Glutamyl Transferase (GGT)', mean: 3.3795613, baaCoeff: 0.265550311, isLog: true },
    { id: 'rbc', name: 'Red blood cell count', mean: 4.4994648, baaCoeff: -0.204442153, isLog: false },
    { id: 'mcv', name: 'Mean corpuscular volume', mean: 91.9251099, baaCoeff: 0.017165356, isLog: false },
    { id: 'rdw', name: 'Red cell distribution width', mean: 13.4342296, baaCoeff: 0.202009895, isLog: false, cap: 11.4, capMode: 'floor' },
    { id: 'monocyte_count', name: 'Monocytes', mean: 0.4746987, baaCoeff: 0.36937314, isLog: false },
    { id: 'neutrophil_count', name: 'Neutrophils', mean: 4.1849454, baaCoeff: 0.06679092, isLog: false },
    { id: 'lymphocyte_percentage', name: 'Lymphocytes (%)', mean: 28.5817604, baaCoeff: -0.0108158, isLog: false, cap: 60, capMode: 'ceiling' },
    { id: 'alt', name: 'Alanine aminotransferase', mean: 3.077868, baaCoeff: -0.312442261, isLog: true },
    { id: 'shbg', name: 'Sex Hormone-Binding Globulin (SHBG)', mean: 3.8202787, baaCoeff: 0.292323186, isLog: true },
    { id: 'vitamin_d', name: 'Vitamin D (25-OH)', mean: 3.6052878, baaCoeff: -0.265467867, isLog: true },
    { id: 'glucose', name: 'Glucose', mean: 4.9563054, baaCoeff: 0.032171478, isLog: false, cap: 4.44, capMode: 'floor' },
    { id: 'mch', name: 'Mean corpuscular hemoglobin', mean: 31.8396206, baaCoeff: 0.02746487, isLog: false },
    { id: 'apoa1', name: 'Apolipoprotein A1 (ApoA1)', mean: 1.5238771, baaCoeff: -0.185139395, isLog: false }
];

// Backward compatibility: alias for form code that expects biomarkers
window.BortzAge.biomarkers = window.BortzAge.features;

window.BortzAge.parseInput = function (value) {
    return value === '' ? NaN : Number(value);
};

/**
 * Compute Biological Age Acceleration (BAA).
 * @param {number[]} values - Raw values in feature order (same order as window.BortzAge.features).
 *   For log features (CRP, GGT, ALT, SHBG, Vitamin D), pass raw value; log is applied inside.
 * @returns {number} BAA in years
 */
function applyCap(value, f) {
    if (!f.capMode) return value;
    if (f.capMode === 'floor') return Math.max(value, f.cap);
    if (f.capMode === 'ceiling') return Math.min(value, f.cap);
    return value;
}

window.BortzAge.calculateBAA = function (values) {
    if (!values || values.length !== window.BortzAge.features.length) return NaN;
    let sum = 0;
    for (let i = 0; i < window.BortzAge.features.length; i++) {
        const f = window.BortzAge.features[i];
        let x = values[i];
        if (f.isLog) {
            if (x <= 0) return NaN;
            x = Math.log(x);
        }
        x = applyCap(x, f);
        const centered = x - f.mean;
        sum += centered * f.baaCoeff;
    }
    return sum * 10;
};

/**
 * Biological age = chronological age + BAA.
 */
window.BortzAge.calculateBortzAgeFromBAA = function (chronologicalAgeYears, baa) {
    if (!Number.isFinite(chronologicalAgeYears) || !Number.isFinite(baa)) return NaN;
    return Math.max(0, chronologicalAgeYears + baa);
};

/**
 * Compute Bortz Age from raw values in feature order.
 * @param {number} chronologicalAgeYears - Optional; if omitted, first element of values is used as age.
 * @param {number[]} rawValuesInFeatureOrder - Same order as window.BortzAge.features (age first, then 21 biomarkers)
 * @returns {number} Biological age in years
 */
window.BortzAge.calculateBortzAge = function (chronologicalAgeYears, rawValuesInFeatureOrder) {
    let values = rawValuesInFeatureOrder;
    let chronoAge = chronologicalAgeYears;
    if (arguments.length === 1 && Array.isArray(chronologicalAgeYears)) {
        values = chronologicalAgeYears;
        chronoAge = values[0];
    }
    const baa = window.BortzAge.calculateBAA(values);
    if (!Number.isFinite(baa)) return NaN;
    return window.BortzAge.calculateBortzAgeFromBAA(chronoAge, baa);
};
