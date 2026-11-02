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
        return String(flag || "").normalize("NFD").replace(/\p{Mn}/gu, "").toLowerCase()
            .replace(/[._-]+/g, " ")
            .replace(/[^\p{L}\p{Nd}\s]/gu, " ")
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
        return getCanonicalFlagName(flag).normalize("NFD").replace(/\p{Mn}/gu, "").toLowerCase()
            .replace(/[^\p{L}\p{Nd}]+/gu, "-")
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
        return new RegExp(`(?:^|\\s)${escapeRegExp(normalizedQuery)}`).test(normalizeFlagKey(option.name));
    }

    function renderHighlightedFlagName(name: unknown, query: unknown): string {
        const text = String(name || "");
        const trimmedQuery = String(query || "").trim();
        if (!trimmedQuery) return escapeHtml(text);

        const pattern = new RegExp(`(^|[^\\p{L}\\p{Nd}])${escapeRegExp(trimmedQuery.toLowerCase())}`, "u");
        const found = pattern.exec(text.toLowerCase());
        if (!found) return escapeHtml(text);
        const match = found.index + (found[1] || "").length;

        return `${escapeHtml(text.slice(0, match))}<strong>${escapeHtml(text.slice(match, match + trimmedQuery.length))}</strong>${escapeHtml(text.slice(match + trimmedQuery.length))}`;
    }

    function renderFlagOptionLabel(flag: unknown, query: unknown = ""): string {
        const canonicalFlag = getCanonicalFlagName(flag);
        return `${renderFlagIcon(canonicalFlag)}<span class="lwc-flag-name">${renderHighlightedFlagName(canonicalFlag, query)}</span>`;
    }

    // A flag tile: the real flag when one exists, otherwise a monogram tile of the
    // same size, so custom flags line up with countries in lists and the field.
    function renderFlagTile(flag: unknown): string {
        const canonicalFlag = getCanonicalFlagName(flag);
        const icon = renderFlagIcon(canonicalFlag, "lwc-flag-icon lwc-flag-tile");
        if (icon) return icon;
        const letter = Array.from(canonicalFlag.replace(/[^\p{L}\p{Nd}]/gu, ""))[0] ?? "";
        return `<span class="lwc-flag-tile lwc-flag-tile--mono" aria-hidden="true">${escapeHtml(letter.toUpperCase())}</span>`;
    }

    function prefersMotion(): boolean {
        return !window.matchMedia?.("(prefers-reduced-motion: reduce)").matches
            && document.documentElement.dataset["lwcMotion"] !== "off";
    }

    function bindAutocomplete(input: HTMLInputElement, options: FlagAutocompleteOptions): { refresh(): void } {
        const listId = input.id + "-autocomplete-list";
        let list: HTMLDivElement | null = null;
        let glider: HTMLSpanElement | null = null;
        let currentFocus = -1;
        let wantsSuggestions = false;
        let clearanceFrame = 0;
        const geometryObserver = new ResizeObserver(resizeList);
        input.setAttribute("role", "combobox");
        input.setAttribute("aria-autocomplete", "list");
        input.setAttribute("aria-controls", listId);
        input.setAttribute("aria-expanded", "false");

        // Field slot: the flag being typed sits at the start of the field and
        // turns over when the value lands on a different flag.
        const slot = document.createElement("span");
        slot.className = "lwc-flag-slot";
        slot.setAttribute("aria-hidden", "true");
        let slotKey = "\u0000";
        input.classList.add("lwc-flag-input");
        input.parentElement?.appendChild(slot);

        function placeSlot(): void {
            const parent = input.parentElement;
            if (!parent || slot.parentElement !== parent) return;
            if (window.getComputedStyle(parent).position === "static") parent.style.position = "relative";
            slot.style.left = input.offsetLeft + "px";
            slot.style.top = input.offsetTop + input.offsetHeight / 2 + "px";
            slot.hidden = input.offsetHeight === 0;
        }

        function syncSlot(): void {
            const value = getCanonicalFlagName(input.value);
            const code = getFlagIconCode(value);
            const key = code ? "flag:" + code : value ? "mono:" + normalizeFlagKey(value).charAt(0) : "";
            if (key === slotKey) return;
            const first = slotKey === "\u0000";
            slotKey = key;
            const face = document.createElement("span");
            face.className = "lwc-flag-slot__face";
            face.innerHTML = key ? renderFlagTile(value) : `<span class="lwc-flag-tile lwc-flag-tile--empty"></span>`;
            const previous = Array.from(slot.querySelectorAll<HTMLElement>(".lwc-flag-slot__face:not(.is-leaving)"));
            slot.appendChild(face);
            slot.dataset["state"] = code ? "flag" : key ? "custom" : "empty";
            if (first || !prefersMotion()) {
                previous.forEach(element => element.remove());
                return;
            }
            previous.forEach(element => {
                element.classList.add("is-leaving");
                element.animate([{ transform: "rotateX(0deg)", opacity: 1 }, { transform: "rotateX(90deg)", opacity: 0 }],
                    { duration: 140, easing: "cubic-bezier(0.3, 0, 1, 1)", fill: "forwards" })
                    .finished.then(() => element.remove(), () => element.remove());
            });
            face.animate([{ transform: "rotateX(-90deg)", opacity: 0 }, { transform: "rotateX(0deg)", opacity: 1 }],
                { duration: 240, delay: previous.length ? 110 : 0, easing: "cubic-bezier(0.34, 1.4, 0.64, 1)", fill: "backwards" });
        }

        // Scripts restore drafts and originals by assigning value directly.
        const valueDescriptor = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value");
        if (valueDescriptor?.get && valueDescriptor.set) {
            const { get, set } = valueDescriptor;
            Object.defineProperty(input, "value", {
                configurable: true,
                get() { return get.call(this); },
                set(next: string) { set.call(this, next); syncSlot(); }
            });
        }
        input.addEventListener("input", syncSlot);
        input.addEventListener("change", syncSlot);
        // Native autofill/history restoration can bypass the instance setter.
        window.addEventListener("pageshow", () => { syncSlot(); placeSlot(); });
        new ResizeObserver(placeSlot).observe(input);
        if (input.parentElement) new ResizeObserver(placeSlot).observe(input.parentElement);
        syncSlot();
        placeSlot();

        function close(): void {
            geometryObserver.disconnect();
            if (clearanceFrame) window.cancelAnimationFrame(clearanceFrame);
            clearanceFrame = 0;
            const closing = list;
            if (closing && prefersMotion() && closing.isConnected) {
                // Fade out where it is; the id moves on so a new list can open at once.
                closing.removeAttribute("id");
                closing.querySelectorAll("[id]").forEach(option => option.removeAttribute("id"));
                if (closing.classList.contains("lwc-flag-suggestions--inline")) {
                    // An inline list leaves the flow at once so the form does not wait for it.
                    Object.assign(closing.style, {
                        position: "absolute", top: closing.offsetTop + "px", left: closing.offsetLeft + "px",
                        width: closing.offsetWidth + "px", maxHeight: closing.offsetHeight + "px"
                    });
                }
                closing.classList.add("lwc-flag-suggestions--leaving");
                closing.setAttribute("aria-hidden", "true");
                closing.animate([{ opacity: 1, transform: "none" }, { opacity: 0, transform: "translateY(-4px) scale(0.985)" }],
                    { duration: 130, easing: "cubic-bezier(0.3, 0, 1, 1)", fill: "forwards" })
                    .finished.then(() => closing.remove(), () => closing.remove());
            } else {
                closing?.remove();
            }
            list = null;
            glider = null;
            currentFocus = -1;
            input.setAttribute("aria-expanded", "false");
            input.removeAttribute("aria-activedescendant");
        }

        function optionRows(): HTMLElement[] {
            return list ? Array.from(list.querySelectorAll<HTMLElement>(":scope > [role='option']")) : [];
        }

        // Lands the chosen row's flag in the field slot. Coordinates are page
        // based: closing an inline list can scroll the page under the flight.
        function flyToSlot(from: { x: number; y: number; width: number; height: number } | null): void {
            if (!from || !prefersMotion()) return;
            window.requestAnimationFrame(() => {
                const face = slot.querySelector<HTMLElement>(".lwc-flag-slot__face:not(.is-leaving)");
                const box = slot.getBoundingClientRect();
                if (!face || !box.width || slot.hidden) return;
                const target = { x: box.left + window.scrollX, y: box.top + window.scrollY, width: box.width, height: box.height };
                const ghost = document.createElement("span");
                ghost.className = "lwc-flag-flight";
                ghost.setAttribute("aria-hidden", "true");
                ghost.innerHTML = face.innerHTML;
                Object.assign(ghost.style, {
                    left: target.x + "px", top: target.y + "px", width: target.width + "px", height: target.height + "px"
                });
                document.body.appendChild(ghost);
                face.getAnimations().forEach(animation => animation.cancel());
                face.style.opacity = "0";
                const dx = from.x + from.width / 2 - (target.x + target.width / 2);
                const dy = from.y + from.height / 2 - (target.y + target.height / 2);
                const scale = from.width / target.width;
                ghost.animate([
                    { transform: `translate(${dx}px, ${dy}px) scale(${scale})` },
                    { transform: `translate(${dx * 0.35}px, ${dy * 0.35 - 10}px) scale(${1 + (scale - 1) * 0.3})`, offset: 0.6 },
                    { transform: "none" }
                ], { duration: 340, easing: "cubic-bezier(0.45, 0, 0.15, 1)" }).finished.finally(() => {
                    ghost.remove();
                    face.style.opacity = "";
                    face.animate([{ transform: "scale(1.22)" }, { transform: "scale(0.94)", offset: 0.55 }, { transform: "none" }],
                        { duration: 260, easing: "ease-out" });
                    input.classList.remove("lwc-flag-input--landed");
                    void input.offsetWidth;
                    input.classList.add("lwc-flag-input--landed");
                });
            });
        }
        input.addEventListener("animationend", () => input.classList.remove("lwc-flag-input--landed"));

        function dismiss(): void {
            wantsSuggestions = false;
            close();
        }

        function scheduleInlineClearance(): void {
            if (!options.inline || !list || clearanceFrame) return;
            clearanceFrame = window.requestAnimationFrame(() => {
                clearanceFrame = 0;
                if (list && document.activeElement === input) {
                    window.LwcFlowActionDock?.ensureClear?.(list);
                }
            });
        }

        function resizeList(): void {
            positionList();
            scheduleInlineClearance();
        }

        function positionList(): void {
            const parent = input.parentElement;
            if (!list || !parent) return;
            const inputBox = input.getBoundingClientRect();
            const parentBox = parent.getBoundingClientRect();
            const viewport = window.visualViewport;
            const gap = 8;
            if (options.inline) {
                // Long forms can reserve space below the field instead of covering
                // earlier inputs with an upward-opening popup.
                list.style.maxHeight = "";
                const preferredHeight = list.getBoundingClientRect().height;
                list.style.maxHeight = Math.min(preferredHeight,
                    Math.max(48, (viewport?.height ?? window.innerHeight) * 0.42)) + "px";
                if (currentFocus >= 0) setActive(currentFocus);
                return;
            }
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
            if (currentFocus >= 0) setActive(currentFocus);
        }

        // One highlight glides between rows instead of each row lighting up.
        function moveGlider(row: HTMLElement | undefined): void {
            if (!glider) return;
            if (!row) {
                glider.style.opacity = "0";
                return;
            }
            const wasHidden = glider.style.opacity !== "1";
            if (wasHidden) glider.style.transition = "none";
            glider.style.transform = `translateY(${row.offsetTop}px)`;
            glider.style.height = row.offsetHeight + "px";
            glider.style.opacity = "1";
            if (wasHidden) {
                void glider.offsetWidth;
                glider.style.transition = "";
            }
        }

        function setActive(index: number, scroll = true): void {
            const items = optionRows();
            if (!items.length) return;
            currentFocus = (index + items.length) % items.length;
            items.forEach((item, itemIndex) => {
                const active = itemIndex === currentFocus;
                item.classList.toggle("autocomplete-active", active);
                item.setAttribute("aria-selected", String(active));
            });
            const activeItem = items[currentFocus];
            if (!activeItem || !list) return;
            moveGlider(activeItem);
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
            const query = input.value.trim();
            const available = options.getOptions();
            const terms = query.split(/\s+/).filter(Boolean);
            const matches = options.hideExactMatch && available.some(option => option.name === query)
                ? []
                : available.filter(option => terms.every(term => matchesFlagOption(option, term))).slice(0, options.limit);
            if (!matches.length || !input.parentElement) {
                close();
                return;
            }

            // Keep an open list and move its rows: matches that stay glide to
            // their new place, new ones fade in, instead of rebuilding per key.
            const reuse = list !== null && list.parentElement === input.parentElement;
            const previousTops = new Map<string, number>();
            if (reuse) {
                optionRows().forEach(row => previousTops.set(row.dataset["value"] ?? "", row.offsetTop - list!.scrollTop));
                optionRows().forEach(row => row.remove());
                currentFocus = -1;
                input.removeAttribute("aria-activedescendant");
                moveGlider(undefined);
            } else {
                close();
                list = document.createElement("div");
                list.id = listId;
                list.className = "autocomplete-items lwc-flag-suggestions";
                if (options.inline) list.classList.add("lwc-flag-suggestions--inline");
                list.setAttribute("role", "listbox");
                list.setAttribute("aria-label", "Flag suggestions");
                glider = document.createElement("span");
                glider.className = "lwc-flag-glider";
                glider.setAttribute("aria-hidden", "true");
                list.appendChild(glider);
                list.addEventListener("pointerleave", () => {
                    if (currentFocus >= 0) moveGlider(optionRows()[currentFocus]);
                });
            }
            const activeList = list!;
            const busiest = Math.max(1, ...available.map(option => option.count));
            const rows = matches.map((option, index) => {
                const item = document.createElement("div");
                item.className = "autocomplete-item";
                item.id = listId + "-option-" + index;
                item.setAttribute("role", "option");
                item.setAttribute("aria-selected", "false");
                item.dataset["value"] = option.name;
                item.style.setProperty("--lwc-flag-share", String(option.count / busiest));
                item.innerHTML = renderFlagTile(option.name)
                    + `<span class="lwc-flag-name">${renderHighlightedFlagName(getCanonicalFlagName(option.name), query)}</span>`
                    + (option.count > 0 ? `<span class="lwc-flag-share" aria-hidden="true"></span>` : "");
                item.addEventListener("mousedown", event => {
                    event.preventDefault();
                    const tileBox = item.querySelector(".lwc-flag-tile")?.getBoundingClientRect();
                    const tile = tileBox ? {
                        x: tileBox.left + window.scrollX, y: tileBox.top + window.scrollY, width: tileBox.width, height: tileBox.height
                    } : null;
                    input.value = option.name;
                    input.dispatchEvent(new Event("input", { bubbles: true }));
                    dismiss();
                    flyToSlot(tile);
                });
                item.addEventListener("pointermove", () => setActive(index, false));
                activeList.appendChild(item);
                return item;
            });
            if (!reuse) input.parentElement.appendChild(activeList);
            input.setAttribute("aria-expanded", "true");
            if (prefersMotion()) {
                rows.forEach((row, index) => {
                    const before = previousTops.get(row.dataset["value"] ?? "");
                    if (before !== undefined) {
                        const delta = before - (row.offsetTop - activeList.scrollTop);
                        if (Math.abs(delta) > 0.5) {
                            row.animate([{ transform: `translateY(${delta}px)` }, { transform: "none" }],
                                { duration: 200, easing: "cubic-bezier(0.2, 0, 0, 1)" });
                        }
                    } else {
                        row.animate([{ opacity: 0, transform: "translateY(6px)" }, { opacity: 1, transform: "none" }],
                            { duration: 180, delay: reuse ? 40 : index * 28, easing: "cubic-bezier(0, 0, 0, 1)", fill: "backwards" });
                    }
                });
            }
            positionList();
            scheduleInlineClearance();
            // Docking and font/layout changes can move the available space without a viewport event.
            geometryObserver.observe(input, { box: "border-box" });
            geometryObserver.observe(input.parentElement, { box: "border-box" });
            document.querySelectorAll("[data-flow-dock]").forEach(dock => {
                geometryObserver.observe(dock, { box: "border-box" });
            });
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
                    ? (event.key === "ArrowDown" ? 0 : optionRows().length - 1)
                    : currentFocus + (event.key === "ArrowDown" ? 1 : -1);
                setActive(next);
            } else if (event.key === "Enter" && currentFocus >= 0 && list) {
                event.preventDefault();
                optionRows()[currentFocus]?.dispatchEvent(new MouseEvent("mousedown"));
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
        window.addEventListener("resize", resizeList);
        window.addEventListener("scroll", event => { if (event.target !== list) positionList(); }, true);
        window.visualViewport?.addEventListener("resize", resizeList);
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
