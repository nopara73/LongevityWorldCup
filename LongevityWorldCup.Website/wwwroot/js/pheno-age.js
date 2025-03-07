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
}

window.PhenoAge.calculateAgeFromDOB = function (birthDate) {
    if (!(birthDate instanceof Date)) {
        throw new Error("Invalid input: dob must be a Date object");
    }

    if (isNaN(birthDate)) throw new Error("Invalid date of birth.");

    const today = new Date();

    if (birthDate > today) throw new Error("Date of birth cannot be in the future.");

    // Calculate total days lived
    const msPerDay = 1000 * 60 * 60 * 24;
    const utc1 = Date.UTC(birthDate.getFullYear(), birthDate.getMonth(), birthDate.getDate());
    const utc2 = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate());
    const totalDays = (utc2 - utc1) / msPerDay;

    // Convert days to years with improved precision
    return Math.round((totalDays / 365.2425) * 100) / 100;
}

// Helper function to calculate PhenoAge based on biomarkers
window.PhenoAge.calculatePhenoAge = function (markerValues, coefficients) {
    // Cap marker values to reference ranges
    let cappedMarkerValues = [];
    for (let i = 0; i < markerValues.length; i++) {
        if (i == 0 || i == 1 || i == 4 || i == 7 || i == 9) {
            // Age marker is not capped
            // CRP is not capped
            cappedMarkerValues.push(markerValues[i]);
        }
        else {
            if (window.PhenoAge.biomarkers[i].coeff < 0) {
                var cappedValue = Math.min(markerValues[i], window.PhenoAge.biomarkers[i].cap);
                cappedMarkerValues.push(cappedValue);
            }
            else {
                var cappedValue = Math.max(markerValues[i], window.PhenoAge.biomarkers[i].cap);
                cappedMarkerValues.push(cappedValue);
            }
        }
    }

    // Sum all coefficients multiplied by the respective marker values
    let rollingTotal = 0;
    for (let i = 0; i < cappedMarkerValues.length; i++) {
        rollingTotal += cappedMarkerValues[i] * coefficients[i];
    }

    const b0 = -19.9067;
    const gamma = 0.0076927;
    rollingTotal += b0;

    // Ten years is long enough to capture significant biological changes and mortality risk shifts while being manageable for statistical models and meaningful for human lifespan considerations.
    let tmonths = 120;

    // Calculate mortality score and risk of death
    const mortalityScore = 1 - Math.exp(-Math.exp(rollingTotal) * (Math.exp(gamma * tmonths) - 1) / gamma);
    return 141.50225 + Math.log(-0.00553 * Math.log(1 - mortalityScore)) / 0.090165;
}