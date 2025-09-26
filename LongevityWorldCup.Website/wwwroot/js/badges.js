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
let submissionOverTwoMapping = {};
let mostSubmissionMapping = {};
let phenoAgeDiffMapping = {};
let mostGuessedMapping = {};
let ageGapMapping = {};
let crowdAgeMapping = {};
let perfectGuessMapping = {};
let bestGuessMapping = {};
let bestGuessDiffMapping = {};
let perfectApplicationMapping = {};


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

    // Compute the best Liver biomarker profile (lowest Liver contribution); if ties, assign badge to all
    let bestLiverScore = Infinity;
    athleteResults.forEach(athlete => {
        const liverScore = window.PhenoAge.calculateLiverPhenoAgeContributor(athlete.bestBiomarkerValues);
        if (liverScore < bestLiverScore) {
            bestLiverScore = liverScore;
        }
    });
    athleteResults.forEach(athlete => {
        const liverScore = window.PhenoAge.calculateLiverPhenoAgeContributor(athlete.bestBiomarkerValues);
        if (liverScore === bestLiverScore) {
            // For liver, we use Albumin (index 1) and Alkaline phosphatase (index 9)
            const albumin = athlete.bestBiomarkerValues[1].toFixed(1);
            const ap = athlete.bestBiomarkerValues[9].toFixed(1);
            const tooltipText = `Liver King: Top Liver Profile (Albumin ${albumin} g/L, ALP ${ap} U/L)`;
            liverMapping[athlete.name] = tooltipText;
        }
    });

    // Compute the best Kidney biomarker profile (lowest Kidney contribution); if ties, assign badge to all
    let bestKidneyScore = Infinity;
    athleteResults.forEach(athlete => {
        const kidneyScore = window.PhenoAge.calculateKidneyPhenoAgeContributor(athlete.bestBiomarkerValues);
        if (kidneyScore < bestKidneyScore) {
            bestKidneyScore = kidneyScore;
        }
    });
    athleteResults.forEach(athlete => {
        const kidneyScore = window.PhenoAge.calculateKidneyPhenoAgeContributor(athlete.bestBiomarkerValues);
        if (kidneyScore === bestKidneyScore) {
            // For kidney, we use Creatinine (index 2)
            const creatinine = athlete.bestBiomarkerValues[2].toFixed(1);
            const tooltipText = `Kidney Overlord: Top Kidney Profile (Creatinine ${creatinine} µmol/L)`;
            kidneyMapping[athlete.name] = tooltipText;
        }
    });

    // Compute the best Metabolic biomarker profile (lowest Metabolic contribution); if ties, assign badge to all
    let bestMetabolicScore = Infinity;
    athleteResults.forEach(athlete => {
        const metabolicScore = window.PhenoAge.calculateMetabolicPhenoAgeContributor(athlete.bestBiomarkerValues);
        if (metabolicScore < bestMetabolicScore) {
            bestMetabolicScore = metabolicScore;
        }
    });
    athleteResults.forEach(athlete => {
        const metabolicScore = window.PhenoAge.calculateMetabolicPhenoAgeContributor(athlete.bestBiomarkerValues);
        if (metabolicScore === bestMetabolicScore) {
            // For metabolic, we use Glucose (index 3)
            const glucose = athlete.bestBiomarkerValues[3].toFixed(1);
            const tooltipText = `Glucose Gladiator: Top Metabolic Profile (Glucose ${glucose} mmol/L)`;
            metabolicMapping[athlete.name] = tooltipText;
        }
    });

    // Compute the best Inflammation biomarker profile (lowest Inflammation contribution); if ties, assign badge to all
    let bestInflammationScore = Infinity;
    athleteResults.forEach(athlete => {
        const inflammationScore = window.PhenoAge.calculateInflammationPhenoAgeContributor(athlete.bestBiomarkerValues);
        if (inflammationScore < bestInflammationScore) {
            bestInflammationScore = inflammationScore;
        }
    });
    athleteResults.forEach(athlete => {
        const inflammationScore = window.PhenoAge.calculateInflammationPhenoAgeContributor(athlete.bestBiomarkerValues);
        if (inflammationScore === bestInflammationScore) {
            // For inflammation, we use C-reactive protein (CRP at index 4)
            const crp = (Math.exp(athlete.bestBiomarkerValues[4]) * 10).toFixed(2);
            const tooltipText = `Inflammation Whisperer: Top Inflammation Profile (CRP ${crp} mg/L)`;
            inflammationMapping[athlete.name] = tooltipText;
        }
    });

    // Compute the best Immune biomarker profile (lowest Immune contribution); if ties, assign badge to all
    let bestImmuneScore = Infinity;
    athleteResults.forEach(athlete => {
        const immuneScore = window.PhenoAge.calculateImmunePhenoAgeContributor(athlete.bestBiomarkerValues);
        if (immuneScore < bestImmuneScore) {
            bestImmuneScore = immuneScore;
        }
    });
    athleteResults.forEach(athlete => {
        const immuneScore = window.PhenoAge.calculateImmunePhenoAgeContributor(athlete.bestBiomarkerValues);
        if (immuneScore === bestImmuneScore) {
            // For immune, display all four markers:
            // White blood cell count (index 5), Lymphocytes (index 6), Mean corpuscular volume (index 7) and Red cell distribution width (index 8)
            const wbc = athlete.bestBiomarkerValues[5].toFixed(1);
            const lymphocyte = athlete.bestBiomarkerValues[6].toFixed(1);
            const mcv = athlete.bestBiomarkerValues[7].toFixed(1);
            const rcdw = athlete.bestBiomarkerValues[8].toFixed(1);
            const tooltipText = `Pathogen Punisher: Top Immune Profile (WBC ${wbc} 10³ cells/µL, Lymphocyte ${lymphocyte}%, MCV ${mcv} fL, RDW ${rcdw}%)`;
            immuneMapping[athlete.name] = tooltipText;
        }
    });

    // Compute the best Submission Count badge (highest number of submissions); if ties, assign badge to all
    let bestSubmissionCount = 0;
    athleteResults.forEach(athlete => {
        if (athlete.submissionCount > bestSubmissionCount) {
            bestSubmissionCount = athlete.submissionCount;
        }
    });
    athleteResults.forEach(athlete => {
        if (athlete.submissionCount >= 2) {
            const tooltipText = `The Regular: Two or More Tests Submitted`;
            submissionOverTwoMapping[athlete.name] = tooltipText;
        }
        if (athlete.submissionCount === bestSubmissionCount) {
            const tooltipText = `The Submittinator: Most Tests Submitted: ${athlete.submissionCount}`;
            mostSubmissionMapping[athlete.name] = tooltipText;
        }
    });

    // Compute the best PhenoAge Difference badge (biggest improvement = lowest phenoAgeDifference); if ties, assign badge to all
    let bestPhenoAgeDiff = Infinity;
    athleteResults.forEach(athlete => {
        if (athlete.phenoAgeDifference < bestPhenoAgeDiff) {
            bestPhenoAgeDiff = athlete.phenoAgeDifference;
        }
    });
    athleteResults.forEach(athlete => {
        if (athlete.phenoAgeDifference === bestPhenoAgeDiff) {
            const tooltipText = `Redemption Arc: Greatest Age Reversal from Frist Submission (Baseline) (${athlete.phenoAgeDifference.toFixed(1)} years)`;
            phenoAgeDiffMapping[athlete.name] = tooltipText;
        }
    });

    // — Compute the three crowd-guess tiers (gold/silver/bronze), allowing ties —
    // 1) build a list of all positive counts
    const positiveCounts = athleteResults
        .map(a => a.crowdCount)
        .filter(c => c > 0);

    // 2) dedupe and sort descending
    const uniqueCounts = [...new Set(positiveCounts)].sort((a, b) => b - a);

    // 3) take the top-3 distinct values
    const topThree = uniqueCounts.slice(0, 3);

    // 4) assign everyone whose crowdCount matches one of those topThree
    athleteResults.forEach(athlete => {
        const rank = topThree.indexOf(athlete.crowdCount);
        if (rank !== -1) {
            // rank === 0 → gold, 1 → silver, 2 → bronze
            mostGuessedMapping[athlete.name] = rank + 1;
        }
    });

    // — Compute the three biggest age-guess gaps (real age vs crowd age), allowing ties —
    //    but only for athletes who have at least one guess
    const guessedAthletes = athleteResults.filter(a => a.crowdCount > 0);

    if (guessedAthletes.length > 0) {
        // 1) build a list of all positive gaps
        const allGaps = guessedAthletes
            .map(a => a.chronologicalAge - a.crowdAge) // no Math.abs()
            .filter(diff => diff > 0) // only when crowd guessed younger

        // 2) dedupe and sort descending
        const topGaps = [...new Set(allGaps)]
            .sort((a, b) => b - a)
            .slice(0, 3);

        // 3) assign everyone whose gap matches one of those top three
        guessedAthletes.forEach(athlete => {
            const gap = Math.abs(athlete.chronologicalAge - athlete.crowdAge);
            const rank = topGaps.indexOf(gap);
            if (rank !== -1) {
                // 0 → gold, 1 → silver, 2 → bronze
                ageGapMapping[athlete.name] = rank + 1;
            }
        });
    }

    // — Compute the three lowest crowd-guessed ages (gold/silver/bronze), allowing ties —
    //    only consider athletes who actually have at least one guess
    const crowdGuessers = athleteResults.filter(a => a.crowdCount > 0);
    if (crowdGuessers.length) {
        // 1) collect and dedupe all crowdAge values
        const uniqueAges = [...new Set(crowdGuessers.map(a => a.crowdAge))]
            .sort((a, b) => a - b)   // ascending = lowest first
            .slice(0, 3);            // take the top-3 distinct

        // 2) assign each athlete whose crowdAge matches one of those
        crowdGuessers.forEach(a => {
            const rank = uniqueAges.indexOf(a.crowdAge);
            if (rank !== -1) {
                // 0 → gold, 1 → silver, 2 → bronze
                crowdAgeMapping[a.name] = rank + 1;
            }
        });
    }

    // — User‐Guess‐Based badges —
    // 1) Load your guesses from localStorage
    const allGuesses = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}');

    // 2) Build an array of { name, diff, isExact } for athletes you actually guessed
    const guessEntries = athleteResults
        .map(a => {
            const slug = window.slugifyName(a.name, true);
            const g = allGuesses[slug];
            if (g && g.value != null) {
                const guessInt = parseInt(g.value, 10);
                const actualInt = parseInt(a.chronologicalAge, 10);
                const diff = Math.abs(guessInt - actualInt);
                return { name: a.name, diff, isExact: diff === 0 };
            }
            return null;
        })
        .filter(x => x !== null);

    // 3a) If you have any perfect (zero-error) guesses, badge them:
    const perfects = guessEntries.filter(x => x.isExact);
    if (perfects.length > 0) {
        perfects.forEach(x => {
            perfectGuessMapping[x.name] = true;
        });
    }
    // 3b) Otherwise, if you have guesses but none perfect, find the closest (min-diff)
    else if (guessEntries.length > 0) {
        const minDiff = Math.min(...guessEntries.map(x => x.diff));
        guessEntries
            .filter(x => x.diff === minDiff)
            .forEach(x => {
                bestGuessMapping[x.name] = true;
                bestGuessDiffMapping[x.name] = minDiff;
            });
    }
}

window.setBadges = function (athlete, athleteCell) {
    const badgeSection = athleteCell.querySelector('.badge-section');

    let badgeElements = [];

    // Medal backgrounds (gold/silver/bronze) + default black
    const defaultBadgeBackground = "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;";
    const badgeBackgrounds = [
        "background: linear-gradient(135deg, #ffd700, #8b8000); border: 2px solid #8a6f00;", // Gold
        "background: linear-gradient(135deg, #c0c0c0, #696969); border: 2px solid #6e6e6e;", // Silver
        "background: linear-gradient(135deg, #cd7f32, #5c4033); border: 2px solid #6b3519;"  // Bronze
    ];

    // Thematic backgrounds
    const liverBackground        = "background: linear-gradient(135deg, #aa336a, #6e0f3c); border: 2px solid #4a0b27;";
    const kidneyBackground       = "background: linear-gradient(135deg, #128fa1, #0e4d64); border: 2px solid #082c3a;";
    const metabolicBackground    = "background: linear-gradient(135deg, #ff9800, #9c5700); border: 2px solid #5c3200;";
    const inflammationBackground = "background: linear-gradient(135deg, #b71c1c, #7f0000); border: 2px solid #4a0000;";
    const immuneBackground       = "background: linear-gradient(135deg, #43a047, #1b5e20); border: 2px solid #0d3a12;";
    const personalLinkColor      = "background: linear-gradient(135deg, #00bcd4, #006e7a); border: 2px solid #004f56;";

    // small helper for keyboard accessibility on clickable <span>
    const spanA11y = `role="link" tabindex="0" onkeydown="if(event.key==='Enter'||event.key===' '){this.click();}"`;

    // — Personal link (clickable <a>)
    if (athlete.personalLink) {
        const linkHref = athlete.personalLink.startsWith('http') ? athlete.personalLink : 'https://' + athlete.personalLink;
        const badgeHtml = `
            <a class="badge-class badge-clickable"
               href="${linkHref}" target="_blank" rel="noopener"
               title="Personal Page" aria-label="Visit athlete's personal page"
               style="${personalLinkColor}">
                <i class="fa fa-link"></i>
            </a>`;
        badgeElements.push({ order: 0, html: badgeHtml });
    }

    // — Podcast (clickable <a>)
    if (athlete.podcastLink) {
        const linkHref = athlete.podcastLink.startsWith('http') ? athlete.podcastLink : 'https://' + athlete.podcastLink;
        const podcastBadgeHtml = `
            <a class="badge-class badge-clickable badge-podcast"
               href="${linkHref}" target="_blank" rel="noopener"
               title="Podcast: hear to this athlete's story in depth"
               style="${defaultBadgeBackground}">
                <i class="fa fa-microphone"></i>
            </a>`;
        badgeElements.push({ order: 1, html: podcastBadgeHtml });
    }

    // — Chronologically Oldest
    if (oldestMapping[athlete.name]) {
        const rank = oldestMapping[athlete.name];
        const ageText = athlete.chronologicalAge.toFixed(1);
        let tooltipText = "", iconClass = "";
        if (rank === 1)      { tooltipText = `Yoda: Chronologically Oldest (Age: ${ageText} years)`;        iconClass = "fa-infinity"; }
        else if (rank === 2) { tooltipText = `Master Roshi: Chronologically 2nd Oldest (Age: ${ageText})`; iconClass = "fa-scroll"; }
        else if (rank === 3) { tooltipText = `Mr. Miyagi: Chronologically 3rd Oldest (Age: ${ageText})`;   iconClass = "fa-leaf"; }
        const badgeHtml = `
            <span class="badge-class"
                  title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // — Chronologically Youngest
    if (youngestMapping[athlete.name]) {
        const rank = youngestMapping[athlete.name];
        const ageText = athlete.chronologicalAge.toFixed(1);
        let tooltipText = "", iconClass = "";
        if (rank === 1)      { tooltipText = `Son Goten: Chronologically Youngest (Age: ${ageText} years)`; iconClass = "fa-baby"; }
        else if (rank === 2) { tooltipText = `Son Gohan: Chronologically 2nd Youngest (Age: ${ageText})`;   iconClass = "fa-child"; }
        else if (rank === 3) { tooltipText = `Son Goku: Chronologically 3rd Youngest (Age: ${ageText})`;    iconClass = "fa-running"; }
        const badgeHtml = `
            <span class="badge-class"
                  title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // — Biologically Youngest
    if (biologicallyYoungestMapping[athlete.name]) {
        const rank = biologicallyYoungestMapping[athlete.name];
        const ageText = athlete.lowestPhenoAge.toFixed(1);
        let tooltipText = "", iconClass = "";
        if (rank === 1)      { tooltipText = `Peter Pan: Biologically Youngest (Pheno Age: ${ageText} years)`; iconClass = "fa-feather"; }
        else if (rank === 2) { tooltipText = `Dorian Gray: Biologically 2nd Youngest (Pheno Age: ${ageText})`; iconClass = "fa-portrait"; }
        else if (rank === 3) { tooltipText = `Benjamin Button: Biologically 3rd Youngest (Pheno Age: ${ageText})`; iconClass = "fa-hourglass-start"; }
        const badgeHtml = `
            <span class="badge-class"
                  title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // — First Applicants
    const firstApplicantsMapping = {
        "Alan V": 1, "Cody Hergenroeder": 2, "Spiderius": 3, "Jesse": 4, "Tone Vays": 5,
        "Stellar Madic": 6, "RichLee": 7, "ScottBrylow": 8, "Mind4u2cn": 9, "Dave Pascoe": 10
    };
    if (firstApplicantsMapping[athlete.name]) {
        const rank = firstApplicantsMapping[athlete.name];
        let tooltipText = "", iconClass = "";
        if (rank === 1)      { tooltipText = "Athlete Zero: 1st Athlete to Join the Longevity World Cup"; iconClass = "fa-circle-notch"; }
        else if (rank === 2) { tooltipText = "Athlete Beta: 2nd Athlete to Join the Longevity World Cup";  iconClass = "fa-star"; }
        else if (rank === 3) { tooltipText = "Athlete Gamma: 3rd Athlete to Join the Longevity World Cup"; iconClass = "fa-bolt"; }
        else                 { tooltipText = "Early Bird: Among the First 10 Athletes to Join the Longevity World Cup"; iconClass = "fa-dove"; }
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa ${iconClass}"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // — Pregnancy
    const pregnancyMapping = ["Olga Vresca"];
    if (pregnancyMapping.includes(athlete.name)) {
        const badgeHtml = `
            <span class="badge-class" title="Baby on Board! Delivering in 2025" style="${defaultBadgeBackground}">
                <i class="fa fa-baby-carriage"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // — Host
    if (athlete.name === "nopara73") {
        const badgeHtml = `
            <span class="badge-class" title="Host: Organizer of the Longevity World Cup" style="${defaultBadgeBackground}">
                <i class="fa fa-house"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // — Perfect Application
    if (athlete.name === "Cornee") {
        const badgeHtml = `
            <span class="badge-class" title="Perfect Application: Most Flawless Entry Form Ever Submitted" style="${defaultBadgeBackground}">
                <i class="fa fa-ruler"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // — Ultimate League (clickable <span>)
    if (ultimateLeagueMapping[athlete.name]) {
        const rank = ultimateLeagueMapping[athlete.name];
        let tooltipText = "", iconClass = "";
        if (rank === 1)      { tooltipText = "Ultimate Lifeform: #1 in the Ultimate League"; iconClass = "fa-crown"; }
        else if (rank === 2) { tooltipText = "Near Immortal: #2 in the Ultimate League";     iconClass = "fa-medal"; }
        else if (rank === 3) { tooltipText = "Third and Threatening: #3 in the Ultimate League"; iconClass = "fa-award"; }
        const badgeHtml = `
            <span class="badge-class badge-clickable"
                  ${spanA11y}
                  title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}"
                  onclick="window.location.href='/leaderboard/leaderboard.html';">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // — Division rank (clickable <span>)
    if (divisionMapping[athlete.name]) {
        const { rank, division } = divisionMapping[athlete.name];
        let tooltipText;
        if (rank === 1) {
            if (division.toLowerCase() === "men's")      tooltipText = "The Alpha Male: #1 in Men's League";
            else if (division.toLowerCase() === "women's") tooltipText = "The Empress: #1 in Women's League";
            else if (division.toLowerCase() === "open")    tooltipText = "The Machine: #1 in Open League";
        } else {
            tooltipText = `#${rank} in ${division} League`;
        }
        const iconClass = window.TryGetDivisionFaIcon(division);
        const leagueSlug = window.slugifyName(division, true);
        const badgeHtml = `
            <span class="badge-class badge-clickable"
                  ${spanA11y}
                  title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}"
                  onclick="window.location.href='/league/${leagueSlug}';">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // — Generation rank (clickable <span>)
    if (generationMapping[athlete.name]) {
        const { rank, generation } = generationMapping[athlete.name];
        let tooltipText;
        if (rank === 1) {
            const gen = generation.toLowerCase();
            if (gen === "silent generation")      tooltipText = "The Grandmaster: #1 in Silent Generation League";
            else if (gen === "baby boomers")      tooltipText = "The Iron Throne: #1 in Baby Boomers League";
            else if (gen === "gen x")             tooltipText = "The Last Ronin: #1 in Gen X League";
            else if (gen === "millennials")       tooltipText = "The Chosen One: #1 in Millennials League";
            else if (gen === "gen z")             tooltipText = "The Meme Lord: #1 in Gen Z League";
            else if (gen === "gen alpha")         tooltipText = "The Singularity: #1 in Gen Alpha League";
        } else {
            tooltipText = `#${rank} in ${generation} League`;
        }
        const iconClass = window.TryGetGenerationFaIcon(generation);
        const leagueSlug = window.slugifyName(generation, true);
        const badgeHtml = `
            <span class="badge-class badge-clickable"
                  ${spanA11y}
                  title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}"
                  onclick="window.location.href='/league/${leagueSlug}';">
                <i class="fa ${iconClass}"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // — Exclusive league (clickable <span>)
    if (exclusiveLeagueMapping[athlete.name]) {
        const { rank, exclusiveLeague } = exclusiveLeagueMapping[athlete.name];
        const tooltipText = `#${rank} in ${exclusiveLeague} League`;
        const leagueSlug = window.slugifyName(exclusiveLeague, true);
        const badgeHtml = `
            <span class="badge-class badge-clickable"
                  ${spanA11y}
                  title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}"
                  onclick="window.location.href='/league/${leagueSlug}';">
                <i class="fa fa-umbrella-beach"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // — Submission Counts (non-clickable)
    if (mostSubmissionMapping[athlete.name]) {
        const tooltipText = mostSubmissionMapping[athlete.name];
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa fa-skull-crossbones"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }
    if (submissionOverTwoMapping[athlete.name]) {
        const tooltipText = submissionOverTwoMapping[athlete.name];
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa fa-calendar-check"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // — Most Guessed (medals)
    if (mostGuessedMapping[athlete.name]) {
        const rank = mostGuessedMapping[athlete.name];
        const count = athlete.crowdCount;
        let tooltipText;
        if (rank === 1)      tooltipText = `Popular AF: Most Age Guesses Received (${count})`;
        else if (rank === 2) tooltipText = `Pretty Damn Popular: 2nd Most Age Guesses Received (${count})`;
        else                 tooltipText = `Shockingly Popular: 3rd Most Age Guesses Received (${count})`;
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}">
                <i class="fa fa-users"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // — Age-Gap (medals)
    if (ageGapMapping[athlete.name]) {
        const rank = ageGapMapping[athlete.name];
        const gap = Math.abs(athlete.chronologicalAge - athlete.crowdAge).toFixed(1);
        const yearWord = (gap === '1.0') ? 'year' : 'years';
        let tooltipText;
        if (rank === 1)      tooltipText = `Skin Trafficker: Perceived ${gap} ${yearWord} younger`;
        else if (rank === 2) tooltipText = `Wrinkle Launderer: Perceived ${gap} ${yearWord} younger`;
        else                 tooltipText = `Collagen Smuggler: Perceived ${gap} ${yearWord} younger`;
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${badgeBackgrounds[rank - 1]}">
                <i class="fa fa-user-ninja"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // — Lowest crowd-guessed age (medals)
    if (crowdAgeMapping[athlete.name]) {
        const rank = crowdAgeMapping[athlete.name];
        const age = athlete.crowdAge.toFixed(1);
        const yearWord = age === '1.0' ? 'year' : 'years';
        let tooltip;
        if (rank === 1)      tooltip = `Baby Boss: Youngest Looking (Crowd Age: ${age} ${yearWord})`;
        else if (rank === 2) tooltip = `Lullaby Lord: 2nd Youngest Looking (Crowd Age: ${age} ${yearWord})`;
        else                 tooltip = `Diaper Don: 3rd Youngest Looking (Crowd Age: ${age} ${yearWord})`;
        const badgeHtml = `
            <span class="badge-class" title="${tooltip}" style="${badgeBackgrounds[rank - 1]}">
                <i class="fa fa-baby"></i>
            </span>`;
        const colorOrder = rank === 1 ? 2 : rank === 2 ? 3 : 4;
        badgeElements.push({ order: colorOrder, html: badgeHtml });
    }

    // — PhenoAge Difference (non-clickable)
    if (phenoAgeDiffMapping[athlete.name]) {
        const tooltipText = phenoAgeDiffMapping[athlete.name];
        const badgeHtml = `
            <span class="badge-class" title="${tooltipText}" style="${defaultBadgeBackground}">
                <i class="fa fa-clock"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // — Perfect Guess (non-clickable)
    if (perfectGuessMapping[athlete.name]) {
        const html = `
            <span class="badge-class" title="Bullseye: You Guessed Their Age Perfectly!" style="${defaultBadgeBackground}">
                <i class="fa fa-bullseye"></i>
            </span>`;
        badgeElements.push({ order: 1, html });
    }

    // — Best Guess (non-clickable)
    if (bestGuessMapping[athlete.name]) {
        const diff = bestGuessDiffMapping[athlete.name];
        const yearWord = diff === 1 ? 'year' : 'years';
        const html = `
            <span class="badge-class" title="Your Best Age Guess: Only ${diff} ${yearWord} Off!" style="${defaultBadgeBackground}">
                <i class="fa fa-crosshairs"></i>
            </span>`;
        badgeElements.push({ order: 1, html });
    }

    // — Biomarker contributions (non-clickable)
    if (liverMapping[athlete.name]) {
        const badgeHtml = `
            <span class="badge-class" title="${liverMapping[athlete.name]}" style="${liverBackground}">
                <i class="fa fa-droplet"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }
    if (kidneyMapping[athlete.name]) {
        const badgeHtml = `
            <span class="badge-class" title="${kidneyMapping[athlete.name]}" style="${kidneyBackground}">
                <i class="fa fa-toilet"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }
    if (metabolicMapping[athlete.name]) {
        const badgeHtml = `
            <span class="badge-class" title="${metabolicMapping[athlete.name]}" style="${metabolicBackground}">
                <i class="fa fa-fire"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }
    if (inflammationMapping[athlete.name]) {
        const badgeHtml = `
            <span class="badge-class" title="${inflammationMapping[athlete.name]}" style="${inflammationBackground}">
                <i class="fa fa-temperature-three-quarters"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }
    if (immuneMapping[athlete.name]) {
        const badgeHtml = `
            <span class="badge-class" title="${immuneMapping[athlete.name]}" style="${immuneBackground}">
                <i class="fa fa-virus"></i>
            </span>`;
        badgeElements.push({ order: 1, html: badgeHtml });
    }

    // Sort by color order then render
    badgeElements.sort((a, b) => a.order - b.order);
    badgeElements.forEach(badge => { badgeSection.innerHTML += badge.html; });

    // Pop animation (unchanged)
    if (badgeSection.classList.contains('badge-section')) {
        Array.from(badgeSection.children).forEach((badge, i) => {
            badge.style.animation = `pop .55s ease-out both ${i * 0.2}s`;
            badge.addEventListener('animationend', function handler() {
                badge.style.animation = '';
                badge.removeEventListener('animationend', handler);
            });
        });
    }
};
