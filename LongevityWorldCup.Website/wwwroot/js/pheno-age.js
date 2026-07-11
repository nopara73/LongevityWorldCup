// Pheno Age calculator and domain contributions.
// https://github.com/nopara73/LongevityWorldCup/issues/136
const biomarkers = [
    { id: "age", name: "Age", coeff: 0.0804 }, // Age has no known lower cap
    { id: "albumin", name: "Albumin", coeff: -0.0336, cap: 54 }, // Ceiling cap
    { id: "creatinine", name: "Creatinine", coeff: 0.0095, cap: 44 },
    { id: "glucose", name: "Glucose", coeff: 0.1953, cap: 4.44 },
    { id: "crp", name: "C-reactive protein", coeff: 0.0954 }, // CRP has no known lower cap
    { id: "wbc", name: "White blood cell count", coeff: 0.0554, cap: 3.5 },
    { id: "lymphocyte", name: "Lymphocytes", coeff: -0.012, cap: 60 },
    { id: "mcv", name: "Mean corpuscular volume", coeff: 0.0268 }, // No lower cap
    { id: "rcdw", name: "Red cell distribution width", coeff: 0.3306, cap: 11.4 },
    { id: "ap", name: "Alkaline phosphatase", coeff: 0.0019 } // No lower cap
];
function parseInput(value) {
    return value === "" ? Number.NaN : Number(value);
}
function markerValue(markerValues, index) {
    return markerValues[index] ?? Number.NaN;
}
function currentPhenoAgeApi() {
    return window.PhenoAge ?? api;
}
function biomarkerAt(index) {
    const biomarker = currentPhenoAgeApi().biomarkers[index];
    if (!biomarker)
        throw new RangeError(`Missing pheno age biomarker at index ${index}.`);
    return biomarker;
}
// Liver: Albumin (index 1) and alkaline phosphatase (index 9).
function calculateLiverScore(markerValues) {
    const albumin = markerValue(markerValues, 1);
    const ap = markerValue(markerValues, 9);
    const albuminDefinition = biomarkerAt(1);
    const alkalinePhosphataseDefinition = biomarkerAt(9);
    const cappedAlbumin = Math.min(albumin, albuminDefinition.cap ?? Number.NaN);
    return cappedAlbumin * albuminDefinition.coeff + ap * alkalinePhosphataseDefinition.coeff;
}
// Kidney: the positive creatinine coefficient makes its cap a floor.
function calculateKidneyScore(markerValues) {
    const definition = biomarkerAt(2);
    const cappedCreatinine = Math.max(markerValue(markerValues, 2), definition.cap ?? Number.NaN);
    return cappedCreatinine * definition.coeff;
}
// Metabolic: the positive glucose coefficient makes its cap a floor.
function calculateMetabolicScore(markerValues) {
    const definition = biomarkerAt(3);
    const cappedGlucose = Math.max(markerValue(markerValues, 3), definition.cap ?? Number.NaN);
    return cappedGlucose * definition.coeff;
}
// Inflammation: C-reactive protein (index 4) has no cap.
function calculateInflammationScore(markerValues) {
    return markerValue(markerValues, 4) * biomarkerAt(4).coeff;
}
// Immune: WBC, lymphocytes, MCV, and RDW.
function calculateImmuneScore(markerValues) {
    const wbc = biomarkerAt(5);
    const lymphocyte = biomarkerAt(6);
    const mcv = biomarkerAt(7);
    const rcdw = biomarkerAt(8);
    const immuneWbc = Math.max(markerValue(markerValues, 5), wbc.cap ?? Number.NaN) * wbc.coeff;
    const immuneLymphocyte = Math.min(markerValue(markerValues, 6), lymphocyte.cap ?? Number.NaN)
        * lymphocyte.coeff;
    const immuneMcv = markerValue(markerValues, 7) * mcv.coeff;
    const immuneRcdw = Math.max(markerValue(markerValues, 8), rcdw.cap ?? Number.NaN) * rcdw.coeff;
    return immuneWbc + immuneLymphocyte + immuneMcv + immuneRcdw;
}
function calculatePhenoAge(markerValues) {
    const activeApi = currentPhenoAgeApi();
    const ageScore = markerValue(markerValues, 0) * biomarkerAt(0).coeff;
    const totalScore = ageScore
        + activeApi.calculateLiverScore(markerValues)
        + activeApi.calculateKidneyScore(markerValues)
        + activeApi.calculateMetabolicScore(markerValues)
        + activeApi.calculateInflammationScore(markerValues)
        + activeApi.calculateImmuneScore(markerValues);
    const b0 = -19.9067;
    const gamma = 0.0076927;
    const rollingTotal = totalScore + b0;
    const tmonths = 120;
    const mortalityScore = 1
        - Math.exp(-Math.exp(rollingTotal) * (Math.exp(gamma * tmonths) - 1) / gamma);
    const phenoAge = 141.50225
        + Math.log(-0.00553 * Math.log(1 - mortalityScore)) / 0.090165;
    return Math.max(0, phenoAge);
}
// The final transformation is linear in the rolling total.
const scalingFactor = 1 / 0.090165;
function calculateLiverPhenoAgeContributor(markerValues) {
    return currentPhenoAgeApi().calculateLiverScore(markerValues) * scalingFactor;
}
function calculateKidneyPhenoAgeContributor(markerValues) {
    return currentPhenoAgeApi().calculateKidneyScore(markerValues) * scalingFactor;
}
function calculateMetabolicPhenoAgeContributor(markerValues) {
    return currentPhenoAgeApi().calculateMetabolicScore(markerValues) * scalingFactor;
}
function calculateInflammationPhenoAgeContributor(markerValues) {
    return currentPhenoAgeApi().calculateInflammationScore(markerValues) * scalingFactor;
}
function calculateImmunePhenoAgeContributor(markerValues) {
    return currentPhenoAgeApi().calculateImmuneScore(markerValues) * scalingFactor;
}
const api = {
    biomarkers,
    parseInput,
    calculateLiverScore,
    calculateKidneyScore,
    calculateMetabolicScore,
    calculateInflammationScore,
    calculateImmuneScore,
    calculatePhenoAge,
    calculateLiverPhenoAgeContributor,
    calculateKidneyPhenoAgeContributor,
    calculateMetabolicPhenoAgeContributor,
    calculateInflammationPhenoAgeContributor,
    calculateImmunePhenoAgeContributor
};
// Preserve the original namespace object when a page has initialized it early.
window.PhenoAge = Object.assign(window.PhenoAge || {}, api);
export {};
