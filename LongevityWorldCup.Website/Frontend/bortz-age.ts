// Bortz Age / Biological Age Acceleration (BAA) from Bortz et al. 2023.
// https://www.nature.com/articles/s42003-023-05456-z
// Coefficients and Scottish test-set means:
// https://github.com/bortzjd/bloodmarker_BA_estimation
// BAA = 10 * sum((x_i - mean_i) * baaCoeff_i). Biological age = chronological age + BAA.
// MSCV, PDW, PCT, and reticulocytes are excluded.

const features = [
    { id: "age", name: "Age", mean: 56.0487752, baaCoeff: 0.074763266 - 0.100432393, isLog: false },
    { id: "albumin", name: "Albumin", mean: 45.1238763, baaCoeff: -0.011331946, isLog: false, cap: 54, capMode: "ceiling" },
    { id: "alp", name: "Alkaline phosphatase", mean: 82.6847975, baaCoeff: 0.00164946, isLog: false },
    { id: "urea", name: "Urea", mean: 5.3547152, baaCoeff: -0.029554872, isLog: false, cap: 9.3, capMode: "ceiling" },
    { id: "cholesterol", name: "Total cholesterol", mean: 5.6177437, baaCoeff: -0.0805656, isLog: false, cap: 7.58, capMode: "ceiling" },
    { id: "creatinine", name: "Creatinine", mean: 71.565605, baaCoeff: -0.01095746, isLog: false },
    { id: "cystatin_c", name: "Cystatin C", mean: 0.900946, baaCoeff: 1.859556436, isLog: false, cap: 0.38, capMode: "floor" },
    { id: "hba1c", name: "Hemoglobin A1c (HbA1c)", mean: 35.4785711, baaCoeff: 0.018116675, isLog: false, cap: 26, capMode: "floor" },
    { id: "crp", name: "C-reactive protein (CRP)", mean: 0.3003624, baaCoeff: 0.079109916, isLog: true },
    { id: "ggt", name: "Gamma-glutamyl transferase (GGT)", mean: 3.3795613, baaCoeff: 0.265550311, isLog: true },
    { id: "rbc", name: "Red blood cell count", mean: 4.4994648, baaCoeff: -0.204442153, isLog: false, cap: 5.77, capMode: "ceiling" },
    { id: "mcv", name: "Mean corpuscular volume", mean: 91.9251099, baaCoeff: 0.017165356, isLog: false },
    { id: "rdw", name: "Red cell distribution width", mean: 13.4342296, baaCoeff: 0.202009895, isLog: false, cap: 11.4, capMode: "floor" },
    // The form stores monocytes and neutrophils as percentages; it derives the counts expected by the model.
    { id: "monocyte_percentage", name: "Monocytes", mean: 0.4746987, baaCoeff: 0.36937314, isLog: false, cap: 0.3, capMode: "floor" },
    { id: "neutrophil_percentage", name: "Neutrophils", mean: 4.1849454, baaCoeff: 0.06679092, isLog: false, cap: 2, capMode: "floor" },
    { id: "lymphocyte_percentage", name: "Lymphocytes (%)", mean: 28.5817604, baaCoeff: -0.0108158, isLog: false, cap: 60, capMode: "ceiling" },
    { id: "alt", name: "Alanine aminotransferase", mean: 3.077868, baaCoeff: -0.312442261, isLog: true, cap: 29, capMode: "ceiling" },
    { id: "shbg", name: "Sex hormone-binding globulin (SHBG)", mean: 3.8202787, baaCoeff: 0.292323186, isLog: true },
    { id: "vitamin_d", name: "Vitamin D (25-OH)", mean: 3.6052878, baaCoeff: -0.265467867, isLog: true, cap: 112.6, capMode: "ceiling" },
    { id: "glucose", name: "Glucose", mean: 4.9563054, baaCoeff: 0.032171478, isLog: false, cap: 4.44, capMode: "floor" },
    { id: "mch", name: "Mean corpuscular hemoglobin", mean: 31.8396206, baaCoeff: 0.02746487, isLog: false, cap: 25.7, capMode: "floor" },
    { id: "apoa1", name: "Apolipoprotein A1 (ApoA1)", mean: 1.5238771, baaCoeff: -0.185139395, isLog: false, cap: 1.82, capMode: "ceiling" }
] as const satisfies readonly BortzFeature[];

function parseInput(value: unknown): number {
    return value === "" ? Number.NaN : Number(value);
}

function applyCap(value: number, feature: BortzFeature): number {
    if (feature.cap === undefined || feature.capMode === undefined) return value;
    if (feature.capMode === "floor") return Math.max(value, feature.cap);
    return Math.min(value, feature.cap);
}

function currentBortzAgeApi(): BortzAgeApi {
    return window.BortzAge ?? api;
}

function calculateBAA(values: readonly number[] | null | undefined): number {
    const activeFeatures = currentBortzAgeApi().features;
    if (!values || values.length !== activeFeatures.length) return Number.NaN;

    let sum = 0;
    for (let index = 0; index < activeFeatures.length; index++) {
        const feature = activeFeatures[index];
        let value = values[index];
        if (feature === undefined || value === undefined) return Number.NaN;

        if (feature.isLog) {
            if (value <= 0) return Number.NaN;
            value = Math.log(value);
        }

        value = applyCap(value, feature);
        sum += (value - feature.mean) * feature.baaCoeff;
    }

    return sum * 10;
}

function calculateBortzAgeFromBAA(chronologicalAgeYears: unknown, baa: unknown): number {
    if (typeof chronologicalAgeYears !== "number"
        || typeof baa !== "number"
        || !Number.isFinite(chronologicalAgeYears)
        || !Number.isFinite(baa)) {
        return Number.NaN;
    }
    return Math.max(0, chronologicalAgeYears + baa);
}

function calculateBortzAge(rawValuesInFeatureOrder: readonly number[] | null | undefined): number;
function calculateBortzAge(
    chronologicalAgeYears: number,
    rawValuesInFeatureOrder: readonly number[]
): number;
function calculateBortzAge(
    chronologicalAgeOrValues: number | readonly number[] | null | undefined,
    rawValuesInFeatureOrder?: readonly number[]
): number {
    let values = rawValuesInFeatureOrder;
    let chronologicalAge: unknown = chronologicalAgeOrValues;
    if (arguments.length === 1 && Array.isArray(chronologicalAgeOrValues)) {
        values = chronologicalAgeOrValues;
        chronologicalAge = values[0];
    }

    const activeApi = currentBortzAgeApi();
    const baa = activeApi.calculateBAA(values);
    if (!Number.isFinite(baa)) return Number.NaN;
    return activeApi.calculateBortzAgeFromBAA(
        typeof chronologicalAge === "number" ? chronologicalAge : Number.NaN,
        baa
    );
}

const api: BortzAgeApi = {
    features,
    // Backward compatibility for form code that expects `biomarkers`.
    biomarkers: features,
    parseInput,
    calculateBAA,
    calculateBortzAgeFromBAA,
    calculateBortzAge
};

// Preserve the original namespace object when a page has initialized it early.
window.BortzAge = Object.assign(window.BortzAge || {}, api);

export {};
