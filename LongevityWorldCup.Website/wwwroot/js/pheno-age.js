// Create a namespace
window.PhenoAge = window.PhenoAge || {};

// Attach biomarkers to the namespace
window.PhenoAge.biomarkers = [
    { id: 'age', name: 'Age', coeff: 0.0804 },
    { id: 'albumin', name: 'Albumin', coeff: -0.0336 },
    { id: 'creatinine', name: 'Creatinine', coeff: 0.0095 },
    { id: 'glucose', name: 'Glucose', coeff: 0.1953 },
    { id: 'crp', name: 'C-reactive protein', coeff: 0.0954 },
    { id: 'wbc', name: 'White blood cell count', coeff: 0.0554 },
    { id: 'lymphocyte', name: 'Lymphocytes', coeff: -0.012 },
    { id: 'mcv', name: 'Mean corpuscular volume', coeff: 0.0268 },
    { id: 'rcdw', name: 'Red cell distribution width', coeff: 0.3306 },
    { id: 'ap', name: 'Alkaline phosphatase', coeff: 0.0019 }
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

    if (birthDate > today) throw new Error("DOB is in the future.");

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
    let rollingTotal = 0;

    // Sum all coefficients multiplied by the respective marker values
    for (let i = 0; i < markerValues.length; i++) {
        rollingTotal += markerValues[i] * coefficients[i];
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