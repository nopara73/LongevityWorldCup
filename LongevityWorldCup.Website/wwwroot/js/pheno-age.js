// Helper function to parse input values
function parseInput(value) {
    return value === '' ? NaN : Number(value);
}

// Helper function to calculate age from date of birth
function calculateAgeFromDOB(dob) {
    const today = new Date();
    let age = today.getFullYear() - dob.getFullYear();
    const m = today.getMonth() - dob.getMonth();

    if (m < 0 || (m === 0 && today.getDate() < dob.getDate())) {
        age--;
    }

    return age;
}

// Helper function to calculate PhenoAge based on biomarkers
function calculatePhenoAge(markerValues, coefficients, tmonths = 120) {
    let rollingTotal = 0;

    // Sum all coefficients multiplied by the respective marker values
    for (let i = 0; i < markerValues.length; i++) {
        rollingTotal += markerValues[i] * coefficients[i];
    }

    const b0 = -19.9067;
    const gamma = 0.0076927;
    rollingTotal += b0;

    // Calculate mortality score and risk of death
    const mortalityScore = 1 - Math.exp(-Math.exp(rollingTotal) * (Math.exp(gamma * tmonths) - 1) / gamma);
    return 141.50225 + Math.log(-0.00553 * Math.log(1 - mortalityScore)) / 0.090165;
}

// Exporting the functions if using modules (optional, if needed in certain setups)
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { parseInput, calculateAgeFromDOB, calculatePhenoAge };
}