// Helper function to parse input values
function parseInput(value) {
    return value === '' ? NaN : Number(value);
}

function calculateAgeFromDOB(birthDate) {
    const today = new Date();
    if (birthDate > today) throw new Error("DOB is in the future.");
    if (!(birthDate instanceof Date)) throw new Error("Invalid input: dob must be a Date object");
    if (isNaN(birthDate)) throw new Error("Invalid date of birth.");

    let age = today.getFullYear() - birthDate.getFullYear();
    const monthDiff = today.getMonth() - birthDate.getMonth();
    if (monthDiff < 0 || (monthDiff === 0 && today.getDate() < birthDate.getDate())) {
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