window.getIcon = function (link) {
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

window.slugifyName = function (name, encode) {
    // Normalize the string: trim, lowercase, remove accents,
    // replace spaces with hyphens, remove any disallowed characters,
    // collapse multiple hyphens and trim hyphens from start/end.
    let normalized = name
        .trim()
        .toLowerCase()
        .normalize('NFKD')
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
window.normalizeString = function (str) {
    return str.normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
}

window.escapeHTML = function (string) {
    const div = document.createElement('div');
    div.textContent = string;
    return div.innerHTML;
}

/**
 * Comparator function to rank athletes based on competition rules.
 * The ranking is determined by:
 * 1. Age Reduction (more negative value indicates greater reversal)
 * 2. Chronological Age (older athletes rank higher)
 * 3. Alphabetical Order (as a last resort, hopefully it'll never have to come down to this)
 */
window.compareAthleteRank = function (a, b) {
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

window.getGeneration = function (birthYear) {
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

window.getRankText = function (rank) {
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

window.showWithDelay = function (element) {
    element.style.display = ''; // Remove display: none to make it visible
    element.classList.add('fade-in'); // Start fade-in transition

    setTimeout(() => {
        element.classList.remove('fade-in'); // Clean up fade-in class after transition
    }, 300); // Match the fade-out delay for smoothness
}
window.calculateAgeBetweenDates = function (startDate, endDate) {
    const diffInMs = endDate - startDate;
    const diffInYears = diffInMs / (1000 * 60 * 60 * 24 * 365.25);
    return diffInYears;
}

window.calculateAgeAtDate = function (dob, currentDate) {
    return window.calculateAgeBetweenDates(dob, currentDate);
}

window.removeAllHighlights = function () {
    document.querySelectorAll('.athlete-name').forEach(element => {
        element.innerHTML = window.escapeHTML(element.textContent);
    });
}

window.highlightText = function (element, searchTerms) {
    const originalText = element.textContent;
    const normalizedText = window.normalizeString(originalText);

    // Build a mapping from positions in normalizedText to positions in originalText
    const mapping = [];
    let originalIndex = 0;
    let normalizedIndex = 0;

    while (originalIndex < originalText.length) {
        const originalChar = originalText[originalIndex];
        const normalizedChar = window.normalizeString(originalChar);

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
        highlightedHTML += window.escapeHTML(originalText.slice(currentIndex, match.start));

        // Append the matched text with highlight
        highlightedHTML += '<span class="highlight">' +
            window.escapeHTML(originalText.slice(match.start, match.end)) +
            '</span>';

        currentIndex = match.end;
    });

    // Append any remaining text
    highlightedHTML += window.escapeHTML(originalText.slice(currentIndex));

    element.innerHTML = highlightedHTML;
}

window.goBackOrHome = function () {
    try {
        const ref = document.referrer;
        const origin = new URL(ref).origin;
        if (
            ref &&
            origin === window.location.origin &&
            window.history.length > 1
        ) {
            window.history.back();
        } else {
            window.location.href = '/';
        }
    } catch (e) {
        window.location.href = '/';
    }
}

window.optimizeImageClient = async function (dataUri) {
    const MaxBase64Length = 50 * 1024 * 1024; // 50 MB
    if (!dataUri || dataUri.length > MaxBase64Length) {
        return { dataUrl: null, contentType: null, extension: null };
    }

    // Parse the data URI
    const match = dataUri.match(/^data:(?<type>.+?);base64,(?<data>.+)$/);
    if (!match) {
        return { dataUrl: null, contentType: null, extension: null };
    }
    const contentType = match.groups.type;
    const base64Data = match.groups.data;

    // Decode Base64 to a Blob
    const binaryString = atob(base64Data);
    const len = binaryString.length;
    const bytes = new Uint8Array(len);
    for (let i = 0; i < len; i++) {
        bytes[i] = binaryString.charCodeAt(i);
    }
    const blob = new Blob([bytes], { type: contentType });

    // Create ImageBitmap for resizing
    const img = await createImageBitmap(blob);

    // Determine new size
    const maxSize = 2048;
    let { width, height } = img;
    if (width > maxSize || height > maxSize) {
        const ratio = Math.min(maxSize / width, maxSize / height);
        width = Math.round(width * ratio);
        height = Math.round(height * ratio);
    }

    // Draw into canvas
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(img, 0, 0, width, height);

    // Export as WebP Blob
    const webpBlob = await new Promise(resolve =>
        canvas.toBlob(resolve, 'image/webp', 0.8)
    );
    if (!webpBlob) {
        // fallback to original if conversion failed
        return { dataUrl: dataUri, contentType, extension: contentType.split('/')[1] };
    }

    // Convert WebP Blob back to Base64 data URI
    const arrayBuffer = await webpBlob.arrayBuffer();
    const u8 = new Uint8Array(arrayBuffer);
    let binary = '';
    const chunk = 0x8000;
    for (let i = 0; i < u8.length; i += chunk) {
        binary += String.fromCharCode.apply(null, u8.subarray(i, i + chunk));
    }
    const webpBase64 = btoa(binary);
    const webpDataUrl = 'data:image/webp;base64,' + webpBase64;

    return {
        dataUrl: webpDataUrl,
        contentType: 'image/webp',
        extension: 'webp'
    };
};