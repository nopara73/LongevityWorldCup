function getIcon(link) {
    // Normalize the link to lowercase for case-insensitive matching
    const normalizedLink = link.toLowerCase().trim();

    // Mapping of link identifiers to Font Awesome icon classes
    const iconMap = {
        email: '<i class="fas fa-envelope"></i>', // Email icon
        'facebook.com': '<i class="fab fa-facebook"></i>',
        'twitter.com': '<i class="fab fa-twitter"></i>',
        'x.com': '<i class="fab fa-twitter"></i>',
        'linkedin.com': '<i class="fab fa-linkedin"></i>',
        'instagram.com': '<i class="fab fa-instagram"></i>',
        'youtube.com': '<i class="fab fa-youtube"></i>',
        'github.com': '<i class="fab fa-github"></i>',
        'gitlab.com': '<i class="fab fa-gitlab"></i>',
        'bitbucket.org': '<i class="fab fa-bitbucket"></i>',
        'paypal.com': '<i class="fab fa-paypal"></i>',
        'stackoverflow.com': '<i class="fab fa-stack-overflow"></i>',
        'medium.com': '<i class="fab fa-medium"></i>',
        'reddit.com': '<i class="fab fa-reddit"></i>',
        'tumblr.com': '<i class="fab fa-tumblr"></i>',
        'pinterest.com': '<i class="fab fa-pinterest"></i>',
        'snapchat.com': '<i class="fab fa-snapchat"></i>',
        'whatsapp.com': '<i class="fab fa-whatsapp"></i>',
        'telegram.org': '<i class="fab fa-telegram"></i>',
        'discord.com': '<i class="fab fa-discord"></i>',
        'slack.com': '<i class="fab fa-slack"></i>',
        'dribbble.com': '<i class="fab fa-dribbble"></i>',
        'behance.net': '<i class="fab fa-behance"></i>',
        'flickr.com': '<i class="fab fa-flickr"></i>',
        'spotify.com': '<i class="fab fa-spotify"></i>',
        'vimeo.com': '<i class="fab fa-vimeo"></i>',
        'tiktok.com': '<i class="fab fa-tiktok"></i>',
        'skype.com': '<i class="fab fa-skype"></i>',
        'wordpress.org': '<i class="fab fa-wordpress"></i>',
        'stackoverflow.com': '<i class="fab fa-stack-overflow"></i>',
        'amazon.com': '<i class="fab fa-amazon"></i>',
        'google.com': '<i class="fab fa-google"></i>',
        'apple.com': '<i class="fab fa-apple"></i>',
        'microsoft.com': '<i class="fab fa-microsoft"></i>',
        'spotify.com': '<i class="fab fa-spotify"></i>',
        'npmjs.com': '<i class="fab fa-npm"></i>',
        'bitly.com': '<i class="fas fa-link"></i>', // Bitly doesn't have a specific icon
        // Add more mappings as needed
    };

    // Check for email first
    if (validator.isEmail(normalizedLink)) {
        return iconMap.email;
    }

    // Iterate through the iconMap to find a matching domain
    for (const [key, icon] of Object.entries(iconMap)) {
        if (normalizedLink.includes(key)) {
            return icon;
        }
    }

    // Return a generic link icon if no match is found
    return '<i class="fas fa-link"></i>';
}

function slugifyName(name, encode) {
    // Normalize the string: trim, lowercase, remove accents,
    // replace spaces with hyphens, remove any disallowed characters,
    // collapse multiple hyphens and trim hyphens from start/end.
    let normalized = name
        .trim()
        .toLowerCase()
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .replace(/\s+/g, '-')
        .replace(/[^a-z0-9\-]/g, '')
        .replace(/-+/g, '-')
        .replace(/^-|-$/g, '');

    // If encoding is desired, run encodeURIComponent.
    // If decoding, first decode then re-normalize (or simply return the normalized value)
    if (encode) {
        return encodeURIComponent(normalized);
    } else {
        // In our case the normalized string should be safe already;
        // calling decodeURIComponent won’t change it unless there are percent sequences.
        return decodeURIComponent(normalized);
    }
}
function normalizeString(str) {
    return str.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
}

function escapeHTML(string) {
    const div = document.createElement('div');
    div.textContent = string;
    return div.innerHTML;
}

// Utility function to escape special characters in the search query
function escapeRegExp(string) {
    return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * Comparator function to rank athletes based on competition rules.
 * The ranking is determined by:
 * 1. Age Reduction (more negative value indicates greater reversal)
 * 2. Chronological Age (older athletes rank higher)
 * 3. Alphabetical Order (as a last resort, hopefully it'll never have to come down to this)
 */
function compareAthleteRank(a, b) {
    // First Criterion: Age Reduction (more negative is better)
    // Since ageReduction = PhenoAge - ChronologicalAge,
    // a more negative value indicates a greater reversal.
    if (a.ageReduction < b.ageReduction) {
        return -1; // Athlete 'a' ranks higher
    }
    if (a.ageReduction > b.ageReduction) {
        return 1; // Athlete 'b' ranks higher
    }

    // Second Criterion: Date of Birth
    // Older athletes rank higher when age reversal is equal.
    // Credit goes to Dave Pascoe for the idea of using date of birth as a tiebreaker.
    if (a.dateOfBirth < b.dateOfBirth) {
        return -1; // Athlete 'a' is older; ranks higher
    }
    if (a.dateOfBirth > b.dateOfBirth) {
        return 1; // Athlete 'b' is older; ranks higher
    }

    // Third Criterion: Alphabetical Order of Names
    // As a last resort, sort alphabetically by athlete's name.
    if (a.name < b.name) {
        return -1; // Athlete 'a' comes first alphabetically
    }
    if (a.name > b.name) {
        return 1; // Athlete 'b' comes first alphabetically
    }

    // If all criteria are equal, which is impossible, then the athlete that registered first ranks higher.
    return 1;
}

function getGeneration(birthYear) {
    if (birthYear >= 1928 && birthYear <= 1945) {
        return "Silent Generation";
    } else if (birthYear >= 1946 && birthYear <= 1964) {
        return "Baby Boomers";
    } else if (birthYear >= 1965 && birthYear <= 1980) {
        return "Gen X";
    } else if (birthYear >= 1981 && birthYear <= 1996) {
        return "Millennials";
    } else if (birthYear >= 1997 && birthYear <= 2012) {
        return "Gen Z";
    } else if (birthYear >= 2013) {
        return "Gen Alpha";
    } else {
        return "Unknown Generation";
    }
}

function getRankText(rank) {
    let rankText = ` (#${rank})`;
    if (rank === 1) {
        rankText = "🥇"; // Gold medal for 1st place
    } else if (rank === 2) {
        rankText = "🥈"; // Silver medal for 2nd place
    } else if (rank === 3) {
        rankText = "🥉"; // Bronze medal for 3rd place
    }
    return rankText;
}

function debounce(func, delay) {
    let debounceTimer;
    return function (...args) {
        const context = this;
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => func.apply(context, args), delay);
    };
}

function hideWithDelay(element) {
    element.classList.add('fade-out'); // Start fade-out transition
    setTimeout(() => {
        element.style.display = 'none'; // Apply display: none after transition
        element.classList.remove('fade-out'); // Reset class
    }, 300); // Delay in milliseconds
}

function showWithDelay(element) {
    element.style.display = ''; // Remove display: none to make it visible
    element.classList.add('fade-in'); // Start fade-in transition

    setTimeout(() => {
        element.classList.remove('fade-in'); // Clean up fade-in class after transition
    }, 300); // Match the fade-out delay for smoothness
}
function calculateAgeBetweenDates(startDate, endDate) {
    const diffInMs = endDate - startDate;
    const diffInYears = diffInMs / (1000 * 60 * 60 * 24 * 365.25);
    return diffInYears;
}

function calculateAgeAtDate(dob, currentDate) {
    return calculateAgeBetweenDates(dob, currentDate);
}

function removeAllHighlights() {
    document.querySelectorAll('.athlete-name').forEach(element => {
        element.innerHTML = escapeHTML(element.textContent);
    });
}

function highlightText(element, searchTerms) {
    const originalText = element.textContent;
    const normalizedText = normalizeString(originalText);

    // Build a mapping from positions in normalizedText to positions in originalText
    const mapping = [];
    let originalIndex = 0;
    let normalizedIndex = 0;

    while (originalIndex < originalText.length) {
        const originalChar = originalText[originalIndex];
        const normalizedChar = normalizeString(originalChar);

        for (let i = 0; i < normalizedChar.length; i++) {
            mapping[normalizedIndex] = originalIndex;
            normalizedIndex++;
        }

        originalIndex++;
    }

    // Now search for matches in normalizedText
    let matchIndices = [];

    searchTerms.forEach(term => {
        let startIndex = 0;
        const termLength = term.length;

        while (startIndex <= normalizedText.length - termLength) {
            if (normalizedText.substr(startIndex, termLength) === term) {
                // Map the normalized indices back to original indices
                const originalStart = mapping[startIndex];
                const originalEnd = mapping[startIndex + termLength - 1] + 1;
                matchIndices.push({ start: originalStart, end: originalEnd });
                startIndex += termLength;
            } else {
                startIndex++;
            }
        }
    });

    // Merge overlapping matches
    matchIndices.sort((a, b) => a.start - b.start);
    let mergedIndices = [];
    matchIndices.forEach(match => {
        if (mergedIndices.length === 0) {
            mergedIndices.push(match);
        } else {
            const last = mergedIndices[mergedIndices.length - 1];
            if (match.start <= last.end) {
                last.end = Math.max(last.end, match.end);
            } else {
                mergedIndices.push(match);
            }
        }
    });

    // Build the highlighted HTML
    let highlightedHTML = '';
    let currentIndex = 0;

    mergedIndices.forEach(match => {
        // Append text before the match
        highlightedHTML += escapeHTML(originalText.slice(currentIndex, match.start));

        // Append the matched text with highlight
        highlightedHTML += '<span class="highlight">' +
            escapeHTML(originalText.slice(match.start, match.end)) +
            '</span>';

        currentIndex = match.end;
    });

    // Append any remaining text
    highlightedHTML += escapeHTML(originalText.slice(currentIndex));

    element.innerHTML = highlightedHTML;
}

function formatLeagueName(leagueName, leagueType) {
    // Replace underscores with spaces
    leagueName = leagueName.replace('_', ' ');
    return `in the ${leagueName} League`;
}