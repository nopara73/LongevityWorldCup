let oldestMapping = {};
let youngestMapping = {};
let biologicallyYoungestMapping = {};
let ultimateLeagueMapping = {};

window.computeBadges = function (athleteResults) {
    // Compute the three chronologically oldest athletes
    const oldestAthletes = athleteResults.slice().sort((a, b) => b.chronologicalAge - a.chronologicalAge);
    oldestAthletes.slice(0, 3).forEach((athlete, index) => {
        oldestMapping[athlete.name] = index + 1; // 1 for oldest, 2 for second, 3 for third
    });

    // Compute the three chronologically youngest athletes
    const youngestAthletes = athleteResults.slice().sort((a, b) => a.chronologicalAge - b.chronologicalAge);
    youngestAthletes.slice(0, 3).forEach((athlete, index) => {
        youngestMapping[athlete.name] = index + 1; // 1 for youngest, 2 for 2nd youngest, 3 for 3rd youngest
    });

    // Compute the three biologically youngest athletes
    const biologicallyYoungestAthletes = athleteResults.slice().sort((a, b) => a.lowestPhenoAge - b.lowestPhenoAge);
    biologicallyYoungestAthletes.slice(0, 3).forEach((athlete, index) => {
        biologicallyYoungestMapping[athlete.name] = index + 1; // 1 for youngest, 2 for 2nd youngest, 3 for 3rd youngest
    });

    // Compute the three athletes with the smallest age reduction (Ultimate League)
    const ultimateLeagueAthletes = athleteResults.slice().sort((a, b) => a.ageReduction - b.ageReduction);
    ultimateLeagueAthletes.slice(0, 3).forEach((athlete, index) => {
        ultimateLeagueMapping[athlete.name] = index + 1; // 1 for 1st, 2 for 2nd, 3 for 3rd
    });
}

window.setBadges = function (athlete, athleteCell) {
    const badgeSection = athleteCell.querySelector('.badge-section');
    if (athlete.personalLink) {
        const linkHref = athlete.personalLink.startsWith('http')
            ? athlete.personalLink
            : 'https://' + athlete.personalLink;
        badgeSection.innerHTML = `
                            <a href="${linkHref}" target="_blank" rel="noopener" class="personal-link-icon" aria-label="Visit athlete's personal page" title="Visit personal page of ${athlete.name}">
                                <i class="fa fa-link"></i>
                            </a>
                        `;
    }

    // Append badge if the athlete is among the three chronologically oldest
    if (oldestMapping[athlete.name]) {
        const order = oldestMapping[athlete.name];
        let tooltipText = "";
        let iconClass = "";
        const ageText = athlete.chronologicalAge.toFixed(1); // Athlete's age formatted

        if (order === 1) {
            tooltipText = `Chronologically Oldest (Age: ${ageText} years)`;
            iconClass = "fa-infinity";
        } else if (order === 2) {
            tooltipText = `Chronologically 2nd Oldest (Age: ${ageText} years)`;
            iconClass = "fa-scroll";
        } else if (order === 3) {
            tooltipText = `Chronologically 3rd Oldest (Age: ${ageText} years)`;
            iconClass = "fa-leaf";
        }

        badgeSection.innerHTML += `
                        <span class="badge-class" title="${tooltipText}">
                            <i class="fa ${iconClass}"></i>
                        </span>`;
    }

    // Append badge if the athlete is among the three chronologically youngest
    if (youngestMapping[athlete.name]) {
        const order = youngestMapping[athlete.name];
        let tooltipText = "";
        let iconClass = "";
        const ageText = athlete.chronologicalAge.toFixed(1); // Athlete's age formatted

        if (order === 1) {
            tooltipText = `Chronologically Youngest (Age: ${ageText} years)`;
            iconClass = "fa-baby";
        } else if (order === 2) {
            tooltipText = `Chronologically 2nd Youngest (Age: ${ageText} years)`;
            iconClass = "fa-child";
        } else if (order === 3) {
            tooltipText = `Chronologically 3rd Youngest (Age: ${ageText} years)`;
            iconClass = "fa-running";
        }
        badgeSection.innerHTML += `
                        <span class="badge-class" title="${tooltipText}">
                            <i class="fa ${iconClass}"></i>
                        </span>`;
    }

    // Append badge if the athlete is among the three biologically youngest
    if (biologicallyYoungestMapping[athlete.name]) {
        const order = biologicallyYoungestMapping[athlete.name];
        let tooltipText = "";
        let iconClass = "";
        const ageText = athlete.lowestPhenoAge.toFixed(1); // Athlete's age formatted

        if (order === 1) {
            tooltipText = `Biologically Youngest (Pheno Age: ${ageText} years)`;
            iconClass = "fa-baby";
        } else if (order === 2) {
            tooltipText = `Biologically 2nd Youngest (Pheno Age: ${ageText} years)`;
            iconClass = "fa-child";
        } else if (order === 3) {
            tooltipText = `Biologically 3rd Youngest (Pheno Age: ${ageText} years)`;
            iconClass = "fa-running";
        }
        badgeSection.innerHTML += `
                        <span class="badge-class" title="${tooltipText}">
                            <i class="fa ${iconClass}"></i>
                        </span>`;
    }

    // Append badge if the athlete is one of the first three ever applicants
    const firstApplicantsMapping = {
        "Alan V": 1,
        "Cody Hergenroeder": 2,
        "Spiderius": 3,
        "Jesse": 4,
        "Tone Vays": 5,
        "Stellar Madic": 6,
        "RichLee": 7,
        "ScottBrylow": 8,
        "Mind4u2cn": 9,
        "Dave Pascoe": 10
    };

    if (firstApplicantsMapping[athlete.name]) {
        const order = firstApplicantsMapping[athlete.name];
        let tooltipText = "";
        let iconClass = "";
        if (order === 1) {
            tooltipText = "Athlete Zero: 1st Athlete to Join the Longevity World Cup";
            iconClass = "fa-circle-notch";
        } else if (order === 2) {
            tooltipText = "Athlete Alpha: 2nd Athlete to Join the Longevity World Cup";
            iconClass = "fa-star";
        } else if (order === 3) {
            tooltipText = "Athlete Beta: 3rd Athlete to Join the Longevity World Cup";
            iconClass = "fa-bolt";
        } else {
            tooltipText = "Early Bird: Among the First 10 Athletes to Join the Longevity World Cup";
            iconClass = "fa-dove";
        }
        badgeSection.innerHTML += `
                        <span class="badge-class" title="${tooltipText}">
                            <i class="fa ${iconClass}"></i>
                        </span>`;
    }

    if (athlete.name === "nopara73") {
        const tooltipText = "Host of the Longevity World Cup";
        const iconClass = "fa-house";

        badgeSection.innerHTML += `
                        <span class="badge-class" title="${tooltipText}">
                            <i class="fa ${iconClass}"></i>
                        </span>`;
    }

    // Append badge if the athlete is among the three with the largest age reduction (Ultimate League)
    if (ultimateLeagueMapping[athlete.name]) {
        const order = ultimateLeagueMapping[athlete.name];
        let tooltipText = "";
        let iconClass = "";

        if (order === 1) {
            tooltipText = `#1 in the Ultimate League`;
            iconClass = "fa-crown";
        } else if (order === 2) {
            tooltipText = `#2 in the Ultimate League`;
            iconClass = "fa-medal";
        } else if (order === 3) {
            tooltipText = `#3 in the Ultimate League`;
            iconClass = "fa-award";
        }
        badgeSection.innerHTML += `
        <span class="badge-class" title="${tooltipText}" style="cursor: pointer;" onclick="window.location.href='/leaderboard/leaderboard.html';">
            <i class="fa ${iconClass}"></i>
        </span>`;
    }
}