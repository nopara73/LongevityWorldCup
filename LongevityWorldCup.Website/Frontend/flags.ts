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

    function bindAutocomplete(input: HTMLInputElement, options: FlagAutocompleteOptions): { refresh(): void } {
        const listId = input.id + "-autocomplete-list";
        let list: HTMLDivElement | null = null;
        let currentFocus = -1;
        let wantsSuggestions = false;
        input.setAttribute("role", "combobox");
        input.setAttribute("aria-autocomplete", "list");
        input.setAttribute("aria-controls", listId);
        input.setAttribute("aria-expanded", "false");

        function close(): void {
            list?.remove();
            list = null;
            currentFocus = -1;
            input.setAttribute("aria-expanded", "false");
            input.removeAttribute("aria-activedescendant");
        }

        function dismiss(): void {
            wantsSuggestions = false;
            close();
        }

        function positionList(): void {
            const parent = input.parentElement;
            if (!list || !parent) return;
            const inputBox = input.getBoundingClientRect();
            const parentBox = parent.getBoundingClientRect();
            const viewport = window.visualViewport;
            const gap = 8;
            const visibleTop = (viewport?.offsetTop ?? 0) + gap;
            let visibleBottom = (viewport?.offsetTop ?? 0) + (viewport?.height ?? window.innerHeight) - gap;
            const dock = document.querySelector(".flow-action-stack--docked")?.getBoundingClientRect();
            if (dock && dock.height > 0 && dock.bottom > visibleTop && dock.top < visibleBottom) {
                visibleBottom = dock.top - gap;
            }

            // Measure the stylesheet's preferred limit before fitting to the available viewport.
            list.style.maxHeight = "";
            const preferredHeight = list.getBoundingClientRect().height;
            const above = Math.max(0, inputBox.top - visibleTop - gap);
            const below = Math.max(0, visibleBottom - inputBox.bottom - gap);
            const openAbove = below < preferredHeight && above > below;
            list.style.maxHeight = Math.min(preferredHeight, openAbove ? above : below) + "px";
            list.style.marginTop = "0";
            const inputTop = inputBox.top - parentBox.top - parent.clientTop + parent.scrollTop;
            list.style.top = openAbove ? "auto" : inputTop + inputBox.height + gap + "px";
            list.style.bottom = openAbove ? parent.clientHeight - inputTop + gap + "px" : "auto";
        }

        function setActive(index: number, scroll = true): void {
            const items = list ? Array.from(list.children) : [];
            if (!items.length) return;
            currentFocus = (index + items.length) % items.length;
            items.forEach((item, itemIndex) => {
                const active = itemIndex === currentFocus;
                item.classList.toggle("autocomplete-active", active);
                item.setAttribute("aria-selected", String(active));
            });
            const activeItem = items[currentFocus];
            if (!activeItem || !list) return;
            input.setAttribute("aria-activedescendant", activeItem.id);
            if (scroll) {
                // Scroll the popup only; navigating suggestions must not move the form.
                const itemBox = activeItem.getBoundingClientRect();
                const listBox = list.getBoundingClientRect();
                if (itemBox.top < listBox.top + 4) list.scrollTop -= listBox.top + 4 - itemBox.top;
                else if (itemBox.bottom > listBox.bottom - 4) list.scrollTop += itemBox.bottom - listBox.bottom + 4;
            }
        }

        function render(): void {
            close();
            const query = input.value.trim();
            const available = options.getOptions();
            if (options.hideExactMatch && available.some(option => option.name === query)) return;
            const terms = query.split(/\s+/).filter(Boolean);
            const matches = available.filter(option => terms.every(term => matchesFlagOption(option, term)))
                .slice(0, options.limit);
            if (!matches.length || !input.parentElement) return;
            list = document.createElement("div");
            list.id = listId;
            list.className = "autocomplete-items lwc-flag-suggestions";
            list.setAttribute("role", "listbox");
            list.setAttribute("aria-label", "Flag suggestions");
            matches.forEach((option, index) => {
                const item = document.createElement("div");
                item.className = "autocomplete-item";
                item.id = listId + "-option-" + index;
                item.setAttribute("role", "option");
                item.setAttribute("aria-selected", "false");
                item.dataset.value = option.name;
                item.innerHTML = renderFlagOptionLabel(option.name, query);
                item.addEventListener("mousedown", event => {
                    event.preventDefault();
                    input.value = option.name;
                    input.dispatchEvent(new Event("input", { bubbles: true }));
                    dismiss();
                });
                item.addEventListener("pointermove", () => setActive(index, false));
                list!.appendChild(item);
            });
            input.parentElement.appendChild(list);
            input.setAttribute("aria-expanded", "true");
            positionList();
        }

        function open(): void {
            wantsSuggestions = true;
            render();
        }

        input.addEventListener("focus", open);
        input.addEventListener("input", open);
        input.addEventListener("blur", dismiss);
        input.addEventListener("keydown", event => {
            if (event.isComposing) return;
            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                if (!list) open();
                if (!list) return;
                event.preventDefault();
                const next = currentFocus < 0
                    ? (event.key === "ArrowDown" ? 0 : list.childElementCount - 1)
                    : currentFocus + (event.key === "ArrowDown" ? 1 : -1);
                setActive(next);
            } else if (event.key === "Enter" && currentFocus >= 0 && list) {
                event.preventDefault();
                list.children[currentFocus]?.dispatchEvent(new MouseEvent("mousedown"));
            } else if (event.key === "Escape") {
                if (list) event.preventDefault();
                dismiss();
            } else if (event.key === "Tab") {
                dismiss();
            }
        });
        input.setAttribute("data-keydown-listener", "true");
        document.addEventListener("click", event => {
            if (event.target !== input && (!(event.target instanceof Node) || !list?.contains(event.target))) dismiss();
        });
        window.addEventListener("resize", positionList);
        window.addEventListener("scroll", event => { if (event.target !== list) positionList(); }, true);
        window.visualViewport?.addEventListener("resize", positionList);
        window.visualViewport?.addEventListener("scroll", positionList);
        return { refresh() { if (wantsSuggestions && document.activeElement === input) render(); } };
    }

    window.LwcFlags = {
        bindAutocomplete,
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
