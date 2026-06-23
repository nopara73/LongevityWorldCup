(function () {
    const flagAliases = new Map([
        ['america', 'United States'],
        ['brasil', 'Brazil'],
        ['nld', 'Netherlands'],
        ['nz', 'New Zealand'],
        ['nzd', 'New Zealand'],
        ['slovak republic', 'Slovakia'],
        ['svk', 'Slovakia'],
        ['turkiye', 'Turkey'],
        ['u s', 'United States'],
        ['u s a', 'United States'],
        ['united states of america', 'United States'],
        ['us', 'United States'],
        ['usa', 'United States']
    ]);

    const standardFlagIconCodes = new Map([
        ['argentina', 'ar'],
        ['australia', 'au'],
        ['austria', 'at'],
        ['bolivia', 'bo'],
        ['brazil', 'br'],
        ['bulgaria', 'bg'],
        ['canada', 'ca'],
        ['czech republic', 'cz'],
        ['ecuador', 'ec'],
        ['egypt', 'eg'],
        ['estonia', 'ee'],
        ['france', 'fr'],
        ['germany', 'de'],
        ['greece', 'gr'],
        ['honduras', 'hn'],
        ['hungary', 'hu'],
        ['iceland', 'is'],
        ['india', 'in'],
        ['iraq', 'iq'],
        ['italy', 'it'],
        ['japan', 'jp'],
        ['latvia', 'lv'],
        ['malaysia', 'my'],
        ['netherlands', 'nl'],
        ['new zealand', 'nz'],
        ['norway', 'no'],
        ['poland', 'pl'],
        ['portugal', 'pt'],
        ['puerto rico', 'pr'],
        ['romania', 'ro'],
        ['russia', 'ru'],
        ['serbia', 'rs'],
        ['sierra leone', 'sl'],
        ['singapore', 'sg'],
        ['slovakia', 'sk'],
        ['south africa', 'za'],
        ['sweden', 'se'],
        ['switzerland', 'ch'],
        ['thailand', 'th'],
        ['turkey', 'tr'],
        ['united arab emirates', 'ae'],
        ['united kingdom', 'gb'],
        ['united states', 'us']
    ]);

    function normalizeText(value) {
        const input = String(value || '');
        if (typeof window.normalizeString === 'function') {
            return window.normalizeString(input);
        }

        return input.normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
    }

    function normalizeFlagKey(flag) {
        return normalizeText(flag)
            .replace(/[._-]+/g, ' ')
            .replace(/[^a-z0-9\s]/g, ' ')
            .replace(/\s+/g, ' ')
            .trim();
    }

    function getCanonicalFlagName(flag) {
        const cleaned = String(flag || '').trim().replace(/\s+/g, ' ');
        if (!cleaned) return '';

        const alias = flagAliases.get(normalizeFlagKey(cleaned));
        return alias || cleaned;
    }

    function getFlagFilterKey(flag) {
        return normalizeFlagKey(getCanonicalFlagName(flag));
    }

    function getFlagRouteSlug(flag) {
        return normalizeText(getCanonicalFlagName(flag))
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '');
    }

    function getFlagHref(flag) {
        const slug = getFlagRouteSlug(flag);
        return slug ? `/flag/${encodeURIComponent(slug)}` : '/leaderboard';
    }

    function getFlagIconCode(flag) {
        return standardFlagIconCodes.get(getFlagFilterKey(flag)) || '';
    }

    function escapeHtml(text) {
        if (typeof window.escapeHTML === 'function') {
            return window.escapeHTML(String(text || ''));
        }

        const div = document.createElement('div');
        div.textContent = String(text || '');
        return div.innerHTML;
    }

    function renderFlagIcon(flag, className = 'lwc-flag-icon') {
        const iconCode = getFlagIconCode(flag);
        return iconCode
            ? `<span class="fi fi-${escapeHtml(iconCode)} ${escapeHtml(className)}" aria-hidden="true"></span>`
            : '';
    }

    function renderFlagLabel(flag) {
        const canonicalFlag = getCanonicalFlagName(flag);
        return `${renderFlagIcon(canonicalFlag)}${escapeHtml(canonicalFlag)}`;
    }

    function getAthleteFlag(athlete) {
        if (!athlete) return '';
        return athlete.Flag || athlete.flag || athlete.canonicalFlag || '';
    }

    function countFlagUsage(athletes, flagAccessor = getAthleteFlag) {
        const flagCounts = new Map();
        (Array.isArray(athletes) ? athletes : []).forEach(athlete => {
            const rawFlag = flagAccessor(athlete);
            const flagName = getCanonicalFlagName(rawFlag);
            const flagKey = getFlagFilterKey(flagName);
            if (!flagName || !flagKey) return;

            if (!flagCounts.has(flagKey)) {
                flagCounts.set(flagKey, { key: flagKey, name: flagName, count: 0 });
            }
            flagCounts.get(flagKey).count += 1;
        });

        return Array.from(flagCounts.values())
            .sort(compareFlagOptions);
    }

    function compareFlagOptions(a, b) {
        return b.count - a.count || a.name.localeCompare(b.name);
    }

    function buildFlagOptions(flags, athletes) {
        const optionsByKey = new Map(countFlagUsage(athletes).map(option => [option.key, { ...option }]));
        (Array.isArray(flags) ? flags : []).forEach(flag => {
            const name = getCanonicalFlagName(flag);
            const key = getFlagFilterKey(name);
            if (!name || !key || optionsByKey.has(key)) return;

            optionsByKey.set(key, { key, name, count: 0 });
        });

        return Array.from(optionsByKey.values()).sort(compareFlagOptions);
    }

    function escapeRegExp(value) {
        return String(value || '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    function matchesFlagOption(option, query) {
        const normalizedQuery = normalizeFlagKey(query);
        if (!normalizedQuery) return true;

        return new RegExp(`\\b${escapeRegExp(normalizedQuery)}`).test(normalizeFlagKey(option.name));
    }

    function renderHighlightedFlagName(name, query) {
        const text = String(name || '');
        const trimmedQuery = String(query || '').trim();
        if (!trimmedQuery) return escapeHtml(text);

        const match = text.toLowerCase().search(new RegExp(`\\b${escapeRegExp(trimmedQuery.toLowerCase())}`));
        if (match < 0) return escapeHtml(text);

        return `${escapeHtml(text.slice(0, match))}<strong>${escapeHtml(text.slice(match, match + trimmedQuery.length))}</strong>${escapeHtml(text.slice(match + trimmedQuery.length))}`;
    }

    function renderFlagOptionLabel(flag, query = '') {
        const canonicalFlag = getCanonicalFlagName(flag);
        return `${renderFlagIcon(canonicalFlag)}<span class="lwc-flag-name">${renderHighlightedFlagName(canonicalFlag, query)}</span>`;
    }

    window.LwcFlags = {
        buildFlagOptions,
        countFlagUsage,
        getCanonicalFlagName,
        getFlagFilterKey,
        getFlagHref,
        getFlagIconCode,
        getFlagRouteSlug,
        matchesFlagOption,
        normalizeFlagKey,
        renderFlagIcon,
        renderFlagLabel,
        renderFlagOptionLabel
    };
})();
