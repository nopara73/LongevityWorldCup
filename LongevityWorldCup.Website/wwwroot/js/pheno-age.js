// Create a namespace
window.PhenoAge = window.PhenoAge || {};

// Attach biomarkers to the namespace
// https://github.com/nopara73/LongevityWorldCup/issues/136
window.PhenoAge.biomarkers = [
    { id: 'age', name: 'Age', coeff: 0.0804 }, // Age has no known lower cap
    { id: 'albumin', name: 'Albumin', coeff: -0.0336 }, // No upper cap
    { id: 'creatinine', name: 'Creatinine', coeff: 0.0095, cap: 44 },
    { id: 'glucose', name: 'Glucose', coeff: 0.1953, cap: 4.44 },
    { id: 'crp', name: 'C-reactive protein', coeff: 0.0954 }, // CRP has no known lower cap
    { id: 'wbc', name: 'White blood cell count', coeff: 0.0554, cap: 3.5 },
    { id: 'lymphocyte', name: 'Lymphocytes', coeff: -0.012, cap: 60 },
    { id: 'mcv', name: 'Mean corpuscular volume', coeff: 0.0268 }, // No lower cap
    { id: 'rcdw', name: 'Red cell distribution width', coeff: 0.3306, cap: 11.4 },
    { id: 'ap', name: 'Alkaline phosphatase', coeff: 0.0019 } // No lower cap
];

// Helper function to parse input values
window.PhenoAge.parseInput = function (value) {
    return value === '' ? NaN : Number(value);
};

// Helper function to calculate age from date of birth remains unchanged
window.PhenoAge.calculateAgeFromDOB = function (birthDate, bloodDrawDate) {
    if (!(birthDate instanceof Date)) throw new Error("Invalid input: birthDate must be a Date object");
    if (!(bloodDrawDate instanceof Date)) throw new Error("Invalid input: bloodDrawDate must be a Date object");
    if (isNaN(birthDate)) throw new Error("Invalid date of birth.");
    if (isNaN(bloodDrawDate)) throw new Error("Invalid blood draw date.");

    if (birthDate > bloodDrawDate) throw new Error("Date of birth cannot be in the future.");

    // Calculate total days lived
    const msPerDay = 1000 * 60 * 60 * 24;
    const utc1 = Date.UTC(birthDate.getFullYear(), birthDate.getMonth(), birthDate.getDate());
    const utc2 = Date.UTC(bloodDrawDate.getFullYear(), bloodDrawDate.getMonth(), bloodDrawDate.getDate());
    const totalDays = (utc2 - utc1) / msPerDay;

    // Convert days to years with improved precision
    return Math.round((totalDays / 365.2425) * 100) / 100;
};

// Liver: Albumin (index 1) and Alkaline phosphatase (index 9)
window.PhenoAge.calculateLiverScore = function (markerValues) {
    const albumin = markerValues[1];
    const ap = markerValues[9];
    const coeffAlbumin = window.PhenoAge.biomarkers[1].coeff;
    const coeffAP = window.PhenoAge.biomarkers[9].coeff;
    return albumin * coeffAlbumin + ap * coeffAP;
};

// Kidney: Creatinine (index 2) with a positive coefficient so use Math.max to ensure at least the cap value.
window.PhenoAge.calculateKidneyScore = function (markerValues) {
    const creatinine = markerValues[2];
    const cap = window.PhenoAge.biomarkers[2].cap;
    const coeff = window.PhenoAge.biomarkers[2].coeff;
    const cappedCreatinine = Math.max(creatinine, cap);
    return cappedCreatinine * coeff;
};

// Metabolic: Glucose (index 3) with a positive coefficient so use Math.max for capping.
window.PhenoAge.calculateMetabolicScore = function (markerValues) {
    const glucose = markerValues[3];
    const cap = window.PhenoAge.biomarkers[3].cap;
    const coeff = window.PhenoAge.biomarkers[3].coeff;
    const cappedGlucose = Math.max(glucose, cap);
    return cappedGlucose * coeff;
};

// Inflammation: C-reactive protein (index 4) has no capping.
window.PhenoAge.calculateInflammationScore = function (markerValues) {
    const crp = markerValues[4];
    const coeff = window.PhenoAge.biomarkers[4].coeff;
    return crp * coeff;
};

// Immune: includes White blood cell count (index 5), Lymphocyte percent (index 6),
// Mean corpuscular volume (index 7) and Red cell distribution width (index 8)
// Note: For WBC and RDW (positive coefficients) we use Math.max; for lymphocytes (negative coefficient) we use Math.min;
// MCV is used directly.
window.PhenoAge.calculateImmuneScore = function (markerValues) {
    // White blood cell count (index 5)
    const wbc = markerValues[5];
    const wbcCap = window.PhenoAge.biomarkers[5].cap;
    const wbcCoeff = window.PhenoAge.biomarkers[5].coeff;
    const immuneWBC = Math.max(wbc, wbcCap) * wbcCoeff;

    // Lymphocyte percent (index 6)
    const lymphocyte = markerValues[6];
    const lymphCap = window.PhenoAge.biomarkers[6].cap;
    const lymphCoeff = window.PhenoAge.biomarkers[6].coeff;
    const immuneLymphocyte = Math.min(lymphocyte, lymphCap) * lymphCoeff;

    // Mean corpuscular volume (index 7) – no capping
    const mcv = markerValues[7];
    const mcvCoeff = window.PhenoAge.biomarkers[7].coeff;
    const immuneMCV = mcv * mcvCoeff;

    // Red cell distribution width (index 8)
    const rcdw = markerValues[8];
    const rcdwCap = window.PhenoAge.biomarkers[8].cap;
    const rcdwCoeff = window.PhenoAge.biomarkers[8].coeff;
    const immuneRCDW = Math.max(rcdw, rcdwCap) * rcdwCoeff;

    return immuneWBC + immuneLymphocyte + immuneMCV + immuneRCDW;
};

// ----- Main PhenoAge Calculation ----- //

// Note: The 'coefficients' parameter has been removed because each biomarker now
// uses its coefficient directly from the window.PhenoAge.biomarkers array.
window.PhenoAge.calculatePhenoAge = function (markerValues) {
    // The first element is Age (index 0)
    const ageScore = markerValues[0] * window.PhenoAge.biomarkers[0].coeff;

    // Combine contributions from the five parts
    const totalScore = ageScore +
        window.PhenoAge.calculateLiverScore(markerValues) +
        window.PhenoAge.calculateKidneyScore(markerValues) +
        window.PhenoAge.calculateMetabolicScore(markerValues) +
        window.PhenoAge.calculateInflammationScore(markerValues) +
        window.PhenoAge.calculateImmuneScore(markerValues);

    // Include the constant term
    const b0 = -19.9067;
    const gamma = 0.0076927;
    const rollingTotal = totalScore + b0;

    // Use 120 months (10 years) as defined originally
    const tmonths = 120;
    const mortalityScore = 1 - Math.exp(-Math.exp(rollingTotal) * (Math.exp(gamma * tmonths) - 1) / gamma);

    return 141.50225 + Math.log(-0.00553 * Math.log(1 - mortalityScore)) / 0.090165;
};

// ----- Domain Contribution Functions ----- //

// Since the final transformation is linear in the rolling total,
// the multiplier (scaling factor) is 1/0.090165 (approximately 11.088).
const scalingFactor = 1 / 0.090165;

// Contribution from Liver biomarkers to PhenoAge (in years)
window.PhenoAge.calculateLiverPhenoAgeContributor = function (markerValues) {
    return window.PhenoAge.calculateLiverScore(markerValues) * scalingFactor;
};

// Contribution from Kidney biomarkers to PhenoAge (in years)
window.PhenoAge.calculateKidneyPhenoAgeContributor = function (markerValues) {
    return window.PhenoAge.calculateKidneyScore(markerValues) * scalingFactor;
};

// Contribution from Metabolic biomarkers to PhenoAge (in years)
window.PhenoAge.calculateMetabolicPhenoAgeContributor = function (markerValues) {
    return window.PhenoAge.calculateMetabolicScore(markerValues) * scalingFactor;
};

// Contribution from Inflammation biomarkers to PhenoAge (in years)
window.PhenoAge.calculateInflammationPhenoAgeContributor = function (markerValues) {
    return window.PhenoAge.calculateInflammationScore(markerValues) * scalingFactor;
};

// Contribution from Immune biomarkers to PhenoAge (in years)
window.PhenoAge.calculateImmunePhenoAgeContributor = function (markerValues) {
    return window.PhenoAge.calculateImmuneScore(markerValues) * scalingFactor;
};