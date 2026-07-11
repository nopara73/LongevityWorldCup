(function () {
    const flagAliases = new Map<string, string>([
        ["america", "United States"],
        ["brasil", "Brazil"],
        ["magyarorszag", "Hungary"],
        ["nld", "Netherlands"],
        ["nz", "New Zealand"],
        ["nzd", "New Zealand"],
        ["slovak republic", "Slovakia"],
        ["svk", "Slovakia"],
        ["turkiye", "Turkey"],
        ["u s", "United States"],
        ["u s a", "United States"],
        ["united states of america", "United States"],
        ["us", "United States"],
        ["usa", "United States"]
    ]);

    const standardFlagIconCodes = new Map<string, string>([
        ["argentina", "ar"], ["australia", "au"], ["austria", "at"],
        ["bolivia", "bo"], ["brazil", "br"], ["bulgaria", "bg"],
        ["canada", "ca"], ["czech republic", "cz"], ["ecuador", "ec"],
        ["egypt", "eg"], ["estonia", "ee"], ["france", "fr"],
        ["germany", "de"], ["greece", "gr"], ["honduras", "hn"],
        ["hungary", "hu"], ["iceland", "is"], ["india", "in"],
        ["iraq", "iq"], ["italy", "it"], ["japan", "jp"],
        ["latvia", "lv"], ["malaysia", "my"], ["netherlands", "nl"],
        ["new zealand", "nz"], ["norway", "no"], ["poland", "pl"],
        ["portugal", "pt"], ["puerto rico", "pr"], ["romania", "ro"],
        ["russia", "ru"], ["serbia", "rs"], ["sierra leone", "sl"],
        ["singapore", "sg"], ["slovakia", "sk"], ["south africa", "za"],
        ["sweden", "se"], ["switzerland", "ch"], ["thailand", "th"],
        ["turkey", "tr"], ["united arab emirates", "ae"],
        ["united kingdom", "gb"], ["united states", "us"]
    ]);

    function normalizeText(value: unknown): string {
        const input = String(value || "");
        if (typeof window.normalizeString === "function") {
            return window.normalizeString(input);
        }

        return input.normalize("NFKD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
    }

    function normalizeFlagKey(flag: unknown): string {
        return normalizeText(flag)
            .replace(/[._-]+/g, " ")
            .replace(/[^a-z0-9\s]/g, " ")
            .replace(/\s+/g, " ")
            .trim();
    }

    function getCanonicalFlagName(flag: unknown): string {
        const cleaned = String(flag || "").trim().replace(/\s+/g, " ");
        if (!cleaned) return "";
        return flagAliases.get(normalizeFlagKey(cleaned)) ?? cleaned;
    }

    function getFlagFilterKey(flag: unknown): string {
        return normalizeFlagKey(getCanonicalFlagName(flag));
    }

    function getFlagRouteSlug(flag: unknown): string {
        return normalizeText(getCanonicalFlagName(flag))
            .replace(/[^a-z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "");
    }

    function getFlagHref(flag: unknown): string {
        const slug = getFlagRouteSlug(flag);
        return slug ? `/flag/${encodeURIComponent(slug)}` : "/leaderboard";
    }

    function getFlagIconCode(flag: unknown): string {
        return standardFlagIconCodes.get(getFlagFilterKey(flag)) ?? "";
    }

    function escapeHtml(text: unknown): string {
        if (typeof window.escapeHTML === "function") {
            return window.escapeHTML(String(text || ""));
        }

        const div = document.createElement("div");
        div.textContent = String(text || "");
        return div.innerHTML;
    }

    function renderFlagIcon(flag: unknown, className = "lwc-flag-icon"): string {
        const iconCode = getFlagIconCode(flag);
        return iconCode
            ? `<span class="fi fi-${escapeHtml(iconCode)} ${escapeHtml(className)}" aria-hidden="true"></span>`
            : "";
    }

    function renderFlagLabel(flag: unknown): string {
        const canonicalFlag = getCanonicalFlagName(flag);
        return `${renderFlagIcon(canonicalFlag)}${escapeHtml(canonicalFlag)}`;
    }

    function isFlagAthlete(value: unknown): value is FlagAthlete {
        return typeof value === "object" && value !== null;
    }

    function getAthleteFlag(athlete: FlagAthlete | null | undefined): unknown {
        if (!athlete) return "";
        return athlete.Flag || athlete.flag || athlete.canonicalFlag || "";
    }

    function countFlagUsage(
        athletes: unknown,
        flagAccessor: (athlete: FlagAthlete) => unknown = getAthleteFlag
    ): FlagOption[] {
        const flagCounts = new Map<string, FlagOption>();
        const athleteValues: readonly unknown[] = Array.isArray(athletes) ? athletes : [];
        athleteValues.forEach(value => {
            if (!isFlagAthlete(value)) return;
            const athlete = value;
            const flagName = getCanonicalFlagName(flagAccessor(athlete));
            const flagKey = getFlagFilterKey(flagName);
            if (!flagName || !flagKey) return;

            const existing = flagCounts.get(flagKey);
            if (existing) {
                existing.count += 1;
            } else {
                flagCounts.set(flagKey, { key: flagKey, name: flagName, count: 1 });
            }
        });

        return Array.from(flagCounts.values()).sort(compareFlagOptions);
    }

    function compareFlagOptions(a: FlagOption, b: FlagOption): number {
        return b.count - a.count || a.name.localeCompare(b.name);
    }

    function buildFlagOptions(flags: unknown, athletes: unknown): FlagOption[] {
        const optionsByKey = new Map<string, FlagOption>(
            countFlagUsage(athletes).map(option => [option.key, { ...option }])
        );
        const flagValues: readonly unknown[] = Array.isArray(flags) ? flags : [];
        flagValues.forEach(flag => {
            const name = getCanonicalFlagName(flag);
            const key = getFlagFilterKey(name);
            if (!name || !key || optionsByKey.has(key)) return;
            optionsByKey.set(key, { key, name, count: 0 });
        });

        return Array.from(optionsByKey.values()).sort(compareFlagOptions);
    }

    function escapeRegExp(value: unknown): string {
        return String(value || "").replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    }

    function matchesFlagOption(option: FlagOption, query: unknown): boolean {
        const normalizedQuery = normalizeFlagKey(query);
        if (!normalizedQuery) return true;
        return new RegExp(`\\b${escapeRegExp(normalizedQuery)}`).test(normalizeFlagKey(option.name));
    }

    function renderHighlightedFlagName(name: unknown, query: unknown): string {
        const text = String(name || "");
        const trimmedQuery = String(query || "").trim();
        if (!trimmedQuery) return escapeHtml(text);

        const pattern = new RegExp(`\\b${escapeRegExp(trimmedQuery.toLowerCase())}`);
        const match = text.toLowerCase().search(pattern);
        if (match < 0) return escapeHtml(text);

        return `${escapeHtml(text.slice(0, match))}<strong>${escapeHtml(text.slice(match, match + trimmedQuery.length))}</strong>${escapeHtml(text.slice(match + trimmedQuery.length))}`;
    }

    function renderFlagOptionLabel(flag: unknown, query: unknown = ""): string {
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

export {};
