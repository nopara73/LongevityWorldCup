let oldestMapping = {};
let youngestMapping = {};
let biologicallyYoungestMapping = {};
let ultimateLeagueMapping = {};
let generationMapping = {};
let divisionMapping = {};
let exclusiveLeagueMapping = {};
let liverMapping = {};
let kidneyMapping = {};
let metabolicMapping = {};
let inflammationMapping = {};
let immuneMapping = {};

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

    // Compute generation badges: top 3 athletes per generation
    const generationGroups = {};
    athleteResults.forEach(athlete => {
        const gen = athlete.generation;
        if (!generationGroups[gen]) {
            generationGroups[gen] = [];
        }
        generationGroups[gen].push(athlete);
    });
    Object.keys(generationGroups).forEach(gen => {
        generationGroups[gen].slice(0, 3).forEach((athlete, index) => {
            generationMapping[athlete.name] = { rank: index + 1, generation: gen };
        });
    });

    // Compute division badges: top 3 athletes per division
    const divisionGroups = {};
    athleteResults.forEach(athlete => {
        const div = athlete.division;
        if (!divisionGroups[div]) {
            divisionGroups[div] = [];
        }
        divisionGroups[div].push(athlete);
    });
    Object.keys(divisionGroups).forEach(div => {
        divisionGroups[div].slice(0, 3).forEach((athlete, index) => {
            divisionMapping[athlete.name] = { rank: index + 1, division: div };
        });
    });

    // Compute exclusive league badges: top 3 athletes per exclusive league
    const exclusiveLeagueGroups = {};
    athleteResults.forEach(athlete => {
        const league = athlete.exclusiveLeague;
        if (!league) return;
        if (!exclusiveLeagueGroups[league]) {
            exclusiveLeagueGroups[league] = [];
        }
        exclusiveLeagueGroups[league].push(athlete);
    });
    Object.keys(exclusiveLeagueGroups).forEach(league => {
        exclusiveLeagueGroups[league].slice(0, 3).forEach((athlete, index) => {
            exclusiveLeagueMapping[athlete.name] = { rank: index + 1, exclusiveLeague: league };
        });
    });

    // Compute the top 3 best Liver biomarker profiles (lowest Liver contribution)
    const liverAthletes = athleteResults.slice().sort((a, b) => {
        return window.PhenoAge.calculateLiverPhenoAgeContributor(a.bestBiomarkerValues) -
            window.PhenoAge.calculateLiverPhenoAgeContributor(b.bestBiomarkerValues);
    });
    liverAthletes.slice(0, 3).forEach((athlete, index) => {
        liverMapping[athlete.name] = index + 1; // 1 = best, 2 = second, 3 = third
    });

    // Compute the top 3 best Kidney biomarker profiles (lowest Kidney contribution)
    const kidneyAthletes = athleteResults.slice().sort((a, b) => {
        return window.PhenoAge.calculateKidneyPhenoAgeContributor(a.bestBiomarkerValues) -
            window.PhenoAge.calculateKidneyPhenoAgeContributor(b.bestBiomarkerValues);
    });
    kidneyAthletes.slice(0, 3).forEach((athlete, index) => {
        kidneyMapping[athlete.name] = index + 1;
    });

    // Compute the top 3 best Metabolic biomarker profiles (lowest Metabolic contribution)
    const metabolicAthletes = athleteResults.slice().sort((a, b) => {
        return window.PhenoAge.calculateMetabolicPhenoAgeContributor(a.bestBiomarkerValues) -
            window.PhenoAge.calculateMetabolicPhenoAgeContributor(b.bestBiomarkerValues);
    });
    metabolicAthletes.slice(0, 3).forEach((athlete, index) => {
        metabolicMapping[athlete.name] = index + 1;
    });

    // Compute the top 3 best Inflammation biomarker profiles (lowest Inflammation contribution)
    const inflammationAthletes = athleteResults.slice().sort((a, b) => {
        return window.PhenoAge.calculateInflammationPhenoAgeContributor(a.bestBiomarkerValues) -
            window.PhenoAge.calculateInflammationPhenoAgeContributor(b.bestBiomarkerValues);
    });
    inflammationAthletes.slice(0, 3).forEach((athlete, index) => {
        inflammationMapping[athlete.name] = index + 1;
    });

    // Compute the top 3 best Immune biomarker profiles (lowest Immune contribution)
    const immuneAthletes = athleteResults.slice().sort((a, b) => {
        return window.PhenoAge.calculateImmunePhenoAgeContributor(a.bestBiomarkerValues) -
            window.PhenoAge.calculateImmunePhenoAgeContributor(b.bestBiomarkerValues);
    });
    immuneAthletes.slice(0, 3).forEach((athlete, index) => {
        immuneMapping[athlete.name] = index + 1;
    });
}

window.setBadges = function (athlete, athleteCell) {
    const badgeSection = athleteCell.querySelector('.badge-section');

    // If a personal link exists, add its badge first
    if (athlete.personalLink) {
        const linkHref = athlete.personalLink.startsWith('http')
            ? athlete.personalLink
            : 'https://' + athlete.personalLink;
        badgeSection.innerHTML = `
            <a href="${linkHref}" target="_blank" rel="noopener" class="personal-link-icon" aria-label="Visit athlete's personal page" title="Visit personal page of ${athlete.name}">
                <i class="fa fa-link"></i>
            </a>
        `;
    } else {
        badgeSection.innerHTML = "";
    }

    // Create an array to collect computed badge elements
    let badgeElements = [];

    // Define the badge backgrounds and their ordering:
    // defaultBadgeBackground is black (order = 1)
    // badgeBackgrounds[0] is gold (order = 2)
    // badgeBackgrounds[1] is silver / ezust (order = 3)
    // badgeBackgrounds[2] is bronze / bronz (order = 4)
    const defaultBadgeBackground = "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;";
    const badgeBackgrounds = [
        "background: linear-gradient(135deg, #ffd700, #8b8000); border: 2px solid #8a6f00;", // Gold
        "background: linear-gradient(135deg, #c0c0c0, #696969); border: 2px solid #6e6e6e;", // Silver (Ezust)
        "background: linear-gradient(135deg, #cd7f32, #5c4033); border: 2px solid #6b3519;"  // Bronze (Bronz)
    ];

    // Chronologically Oldest badge (uses black)
    if (oldestMapping[athlete.name]) {
        const rank = oldestMapping[athlete.name];
        let tooltipText = "";
        let iconClass = "";
        const ageText = athlete.chronologicalAge.toFixed(1);
        if (rank === 1) {
            tooltipText = `Chronologically Oldest (Age: ${ageText} years)`;
            iconClass = "fa-infinity";
        } else if (rank === 2) {
            tooltipText = `Chronologically 2nd Oldest (Age: ${ageText} years)`;
            iconClass = "fa-scroll";
        } else if (rank === 3) {
            tooltipText = `Chronologically 3rd Oldest (Age: ${ageText} years)`;
            iconClass = "fa-leaf";
        }
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // Chronologically Youngest badge (uses black)
    if (youngestMapping[athlete.name]) {
        const rank = youngestMapping[athlete.name];
        let tooltipText = "";
        let iconClass = "";
        const ageText = athlete.chronologicalAge.toFixed(1);
        if (rank === 1) {
            tooltipText = `Chronologically Youngest (Age: ${ageText} years)`;
            iconClass = "fa-baby";
        } else if (rank === 2) {
            tooltipText = `Chronologically 2nd Youngest (Age: ${ageText} years)`;
            iconClass = "fa-child";
        } else if (rank === 3) {
            tooltipText = `Chronologically 3rd Youngest (Age: ${ageText} years)`;
            iconClass = "fa-running";
        }
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // Biologically Youngest badge (uses black)
    if (biologicallyYoungestMapping[athlete.name]) {
        const rank = biologicallyYoungestMapping[athlete.name];
        let tooltipText = "";
        let iconClass = "";
        const ageText = athlete.lowestPhenoAge.toFixed(1);
        if (rank === 1) {
            tooltipText = `Biologically Youngest (Pheno Age: ${ageText} years)`;
            iconClass = "fa-baby";
        } else if (rank === 2) {
            tooltipText = `Biologically 2nd Youngest (Pheno Age: ${ageText} years)`;
            iconClass = "fa-child";
        } else if (rank === 3) {
            tooltipText = `Biologically 3rd Youngest (Pheno Age: ${ageText} years)`;
            iconClass = "fa-running";
        }
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // First Applicants badge (uses black)
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
        const rank = firstApplicantsMapping[athlete.name];
        let tooltipText = "";
        let iconClass = "";
        if (rank === 1) {
            tooltipText = "Athlete Zero: 1st Athlete to Join the Longevity World Cup";
            iconClass = "fa-circle-notch";
        } else if (rank === 2) {
            tooltipText = "Athlete Alpha: 2nd Athlete to Join the Longevity World Cup";
            iconClass = "fa-star";
        } else if (rank === 3) {
            tooltipText = "Athlete Beta: 3rd Athlete to Join the Longevity World Cup";
            iconClass = "fa-bolt";
        } else {
            tooltipText = "Early Bird: Among the First 10 Athletes to Join the Longevity World Cup";
            iconClass = "fa-dove";
        }
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // Host badge for "nopara73" (uses black)
    if (athlete.name === "nopara73") {
        const tooltipText = "Host of the Longevity World Cup";
        const iconClass = "fa-house";
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // Ultimate League ranking badge (colored backgrounds)
    if (ultimateLeagueMapping[athlete.name]) {
        const rank = ultimateLeagueMapping[athlete.name];
        let tooltipText = "";
        let iconClass = "";
        if (rank === 1) {
            tooltipText = "#1 in the Ultimate League";
            iconClass = "fa-crown";
        } else if (rank === 2) {
            tooltipText = "#2 in the Ultimate League";
            iconClass = "fa-medal";
        } else if (rank === 3) {
            tooltipText = "#3 in the Ultimate League";
            iconClass = "fa-award";
        }
        // Use badgeBackgrounds[0] for rank 1 (gold), [1] for rank 2 (silver), [2] for rank 3 (bronze)
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="cursor: pointer; ${badgeBackgrounds[rank - 1]}" onclick="window.location.href='/leaderboard/leaderboard.html';">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // Generation ranking badge (colored backgrounds)
    if (generationMapping[athlete.name]) {
        const { rank, generation } = generationMapping[athlete.name];
        const tooltipText = `#${rank} in ${generation} League`;
        let iconClass = "";
        if (rank === 1) {
            iconClass = "fa-trophy";
        } else if (rank === 2) {
            iconClass = "fa-medal";
        } else if (rank === 3) {
            iconClass = "fa-award";
        }
        const leagueSlug = slugifyName(generation, true);
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="cursor: pointer; ${badgeBackgrounds[rank - 1]}" onclick="window.location.href='/league/${leagueSlug}';">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // Division ranking badge (colored backgrounds)
    if (divisionMapping[athlete.name]) {
        const { rank, division } = divisionMapping[athlete.name];
        const tooltipText = `#${rank} in ${division} League`;
        let iconClass = "";
        if (rank === 1) {
            iconClass = "fa-trophy";
        } else if (rank === 2) {
            iconClass = "fa-medal";
        } else if (rank === 3) {
            iconClass = "fa-award";
        }
        const leagueSlug = slugifyName(division, true);
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="cursor: pointer; ${badgeBackgrounds[rank - 1]}" onclick="window.location.href='/league/${leagueSlug}';">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // Exclusive league ranking badge (colored backgrounds)
    if (exclusiveLeagueMapping[athlete.name]) {
        const { rank, exclusiveLeague } = exclusiveLeagueMapping[athlete.name];
        const tooltipText = `#${rank} in ${exclusiveLeague} League`;
        let iconClass = "";
        if (rank === 1) {
            iconClass = "fa-trophy";
        } else if (rank === 2) {
            iconClass = "fa-medal";
        } else if (rank === 3) {
            iconClass = "fa-award";
        }
        const leagueSlug = slugifyName(exclusiveLeague, true);
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="cursor: pointer; ${badgeBackgrounds[rank - 1]}" onclick="window.location.href='/league/${leagueSlug}';">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // Liver Biomarker Contribution ranking badge (colored backgrounds)
    if (liverMapping[athlete.name]) {
        const rank = liverMapping[athlete.name];
        let tooltipText = "";
        const iconClass = "fa-droplet";
        if (rank === 1) {
            tooltipText = `Liver King: Best Liver Profile`;
        } else if (rank === 2) {
            tooltipText = `2nd Best Liver Profile`;
        } else if (rank === 3) {
            tooltipText = `3rd Best Liver Profile`;
        }
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // Kidney Biomarker Contribution ranking badge (colored backgrounds)
    if (kidneyMapping[athlete.name]) {
        const rank = kidneyMapping[athlete.name];
        let tooltipText = "";
        const iconClass = "fa-toilet";
        if (rank === 1) {
            tooltipText = `Best Kidney Profile`;
        } else if (rank === 2) {
            tooltipText = `2nd Best Kidney Profile`;
        } else if (rank === 3) {
            tooltipText = `3rd Best Kidney Profile`;
        }
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // Metabolic Biomarker Contribution ranking badge (colored backgrounds)
    if (metabolicMapping[athlete.name]) {
        const rank = metabolicMapping[athlete.name];
        let tooltipText = "";
        const iconClass = "fa-fire";
        if (rank === 1) {
            tooltipText = `Best Metabolic Profile`;
        } else if (rank === 2) {
            tooltipText = `2nd Best Metabolic Profile`;
        } else if (rank === 3) {
            tooltipText = `3rd Best Metabolic Profile`;
        }
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // Inflammation Biomarker Contribution ranking badge (colored backgrounds)
    if (inflammationMapping[athlete.name]) {
        const rank = inflammationMapping[athlete.name];
        let tooltipText = "";
        const iconClass = "fa-temperature-three-quarters";
        if (rank === 1) {
            tooltipText = `Best Inflammation Profile`;
        } else if (rank === 2) {
            tooltipText = `2nd Best Inflammation Profile`;
        } else if (rank === 3) {
            tooltipText = `3rd Best Inflammation Profile`;
        }
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // Immune Biomarker Contribution ranking badge (colored backgrounds)
    if (immuneMapping[athlete.name]) {
        const rank = immuneMapping[athlete.name];
        let tooltipText = "";
        const iconClass = "fa-shield-virus";
        if (rank === 1) {
            tooltipText = `Best Immune Profile`;
        } else if (rank === 2) {
            tooltipText = `2nd Best Immune Profile`;
        } else if (rank === 3) {
            tooltipText = `3rd Best Immune Profile`;
        }
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // Sort the badge elements by the color order: black (1) first, then gold (2), silver/ezust (3), and bronze/bronz (4)
    badgeElements.sort((a, b) => a.order - b.order);

    // Append the sorted badges to the badge section
    badgeElements.forEach(badge => {
        badgeSection.innerHTML += badge.html;
    });
}