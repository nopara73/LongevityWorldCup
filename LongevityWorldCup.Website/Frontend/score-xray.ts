(() => {
    type ClockId = "pheno" | "bortz";
    interface Athlete { slug: string; name: string; portrait: string | null; ultimateRank: number; isPro: boolean }
    interface Panel { number: number; date: string | null; phenoAge: number | null; bortzAge: number | null; selectedPheno: boolean; selectedBortz: boolean }
    interface Term {
        key: string; name: string; unit: string; inputUnit: string; reported: number; input: number; scored: number;
        model: number; coefficient: number; mean: number | null; contribution: number; cap: number | null;
        capMode: string; conversion: string | null; transform: string | null;
    }
    interface Clock {
        id: ClockId; panelNumber: number; date: string | null; ageAtTest: number; age: number; ageDifference: number;
        rank: number; fieldSize: number; phenoPanelCount: number; bortzPanelCount: number; suppliesUltimateScore: boolean;
        lab: { key: string; name: string; unit: string; value: number }[]; terms: Term[];
        calculation: { name: string; formula: string; value: number | null; unit: string }[];
        neighbors: { slug: string; name: string; rank: number; ageDifference: number }[];
    }
    interface Document { asOfUtc: string; athlete: Athlete; panels: Panel[]; proofs: string[]; pheno: Clock | null; bortz: Clock | null }
    interface Directory { asOfUtc: string; athletes: Athlete[] }
    interface View { slug: string | null; clock: ClockId | null; step: number }
    const el = <T extends HTMLElement = HTMLElement>(id: string): T => {
        const element = document.getElementById(id);
        if (!element) throw new Error(`Missing Score X-Ray element: ${id}`);
        return element as T;
    };
    const search = el<HTMLInputElement>("athleteSearch");
    const results = el("searchResults");
    const body = el("stageBody");
    const pipeline = el("pipeline");
    const playButton = el<HTMLButtonElement>("playWalkthrough");
    const proofDialog = el<HTMLDialogElement>("proofDialog");
    const proofImage = el<HTMLImageElement>("proofImage");
    const title = (id: ClockId): string => id === "pheno" ? "Pheno" : "Bortz";
    const stages = ["Dated panel", "Model inputs", "Scoring limits", "The clock", "Age reduction", "Leaderboard"];
    const escape = (value: string): string => value.replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);
    const number = (value: number | null, places = 2): string => value === null || !Number.isFinite(value) ? "—" : value.toLocaleString("en", { maximumFractionDigits: places, minimumFractionDigits: places });
    const rawNumber = (value: number | null): string => value === null ? "—" : value.toLocaleString("en", { maximumFractionDigits: 5 });
    const signed = (value: number, places = 2): string => `${value > 0 ? "+" : value < 0 ? "−" : ""}${number(Math.abs(value), places)}`;
    const exact = (value: number): string => escape(value.toString());
    const date = (value: string | null): string => value ? new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(`${value}T12:00:00Z`)) : "Date not recorded";
    const dateShort = (value: string | null): string => value ? new Intl.DateTimeFormat("en-GB", { month: "short", year: "2-digit", timeZone: "UTC" }).format(new Date(`${value}T12:00:00Z`)) : "Undated";
    const matchText = (value: string): string => value.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase().replace(/[_-]/g, " ");
    const asset = (value: string | null): string | null => {
        if (!value) return null;
        try {
            const url = new URL(value, location.origin);
            return (url.origin === location.origin || url.origin === "https://longevityworldcup.com") && ["http:", "https:"].includes(url.protocol) ? url.href : null;
        } catch { return null; }
    };
    const profile = (slug: string): string => `/athlete/${encodeURIComponent(slug.replace(/_/g, "-"))}`;
    let directory: Athlete[] = [];
    let data: Document | null = null;
    let clockId: ClockId = "bortz";
    let step = 0;
    let pendingView: View = readView();
    let request: AbortController | null = null;
    let sequence = 0;
    let playTimer: number | null = null;
    let playing = false;
    let matches: Athlete[] = [];
    let activeResult = -1;
    let proofIndex = 0;
    let proofSequence = 0;
    let copyBusy = false;
    const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");

    function readView(): View {
        const query = new URLSearchParams(location.search);
        const clock = query.get("clock");
        const stage = Number(query.get("step") ?? 1);
        return { slug: query.get("athlete"), clock: clock === "pheno" || clock === "bortz" ? clock : null, step: Number.isInteger(stage) && stage >= 1 && stage <= 6 ? stage - 1 : 0 };
    }

    function saveView(push: boolean): void {
        if (!data) return;
        const url = new URL(location.href);
        url.search = new URLSearchParams({ athlete: data.athlete.slug, clock: clockId, step: String(step + 1) }).toString();
        if (url.href === location.href) return;
        if (push) history.pushState(null, "", url); else history.replaceState(null, "", url);
    }

    async function fetchJson<T>(path: string, signal: AbortSignal): Promise<T> {
        const response = await fetch(path, { signal: AbortSignal.any([signal, AbortSignal.timeout(15000)]), cache: "no-store" });
        if (!response.ok) throw new Error(response.status === 404 ? "That athlete is not in the current published field. Try another name." : "The published field couldn’t be loaded. Try again in a moment.");
        return await response.json() as T;
    }

    function closeSearch(): void {
        results.hidden = true;
        search.setAttribute("aria-expanded", "false");
        search.removeAttribute("aria-activedescendant");
        activeResult = -1;
    }

    function showSearch(): void {
        const query = matchText(search.value.trim());
        matches = directory.filter(a => !query || matchText(a.name).includes(query) || matchText(a.slug).includes(query)).slice(0, 15);
        activeResult = -1;
        results.innerHTML = matches.length ? matches.map((a, i) => `<button type="button" id="athlete-option-${i}" role="option" aria-selected="false" data-result="${i}"><span>${escape(a.name)}</span><small>#${a.ultimateRank} · ${a.isPro ? "Pro" : "Amateur"}</small></button>`).join("") : "<p>No matching athlete. Try a different name.</p>";
        results.hidden = false;
        search.setAttribute("aria-expanded", "true");
        search.removeAttribute("aria-activedescendant");
    }

    function selectResult(index: number): void {
        const selected = matches[index];
        if (!selected) return;
        search.value = selected.name;
        closeSearch();
        void loadAthlete({ slug: selected.slug, clock: null, step: 0 }, true);
    }

    function stopPlay(): void {
        playing = false;
        if (playTimer !== null) window.clearTimeout(playTimer);
        playTimer = null;
        playButton.innerHTML = 'Play walkthrough <span aria-hidden="true">▶</span>';
        playButton.setAttribute("aria-pressed", "false");
    }

    function showError(error: unknown): void {
        el("loadStatus").hidden = true;
        el("loadErrorText").textContent = error instanceof Error && error.name !== "TimeoutError" ? error.message : "The published field is taking too long to respond. Please try again.";
        el("loadError").hidden = false;
        el("workspace").hidden = true;
    }

    async function initialize(): Promise<void> {
        const attempt = ++sequence;
        request?.abort();
        request = new AbortController();
        el("loadError").hidden = true;
        el("loadStatus").hidden = false;
        try {
            const value = await fetchJson<Directory>("/api/score-xray", request.signal);
            if (sequence !== attempt) return;
            directory = value.athletes;
            search.disabled = false;
            await loadAthlete(pendingView, false);
        } catch (error) { if (sequence === attempt) showError(error); }
    }

    async function loadAthlete(view: View, push: boolean): Promise<void> {
        stopPlay();
        closeSearch();
        if (proofDialog.open) proofDialog.close();
        pendingView = view;
        const attempt = ++sequence;
        request?.abort();
        request = new AbortController();
        el("loadStatus").textContent = "Opening the published blood panels…";
        el("loadStatus").hidden = false;
        el("loadError").hidden = true;
        el("workspace").hidden = true;
        try {
            const selected = view.slug ? directory.find(a => a.slug.toLowerCase() === view.slug!.toLowerCase().replace(/-/g, "_")) : directory[0];
            if (!selected) throw new Error("That athlete is not in the current published field. Try another name.");
            const value = await fetchJson<Document>(`/api/score-xray/${encodeURIComponent(selected.slug)}`, request.signal);
            if (sequence !== attempt) return;
            data = value;
            clockId = view.clock ?? (data.athlete.isPro ? "bortz" : "pheno");
            step = view.step;
            search.value = data.athlete.name;
            el("athleteName").textContent = data.athlete.name;
            el<HTMLAnchorElement>("athleteProfile").href = profile(data.athlete.slug);
            el("athleteMeta").textContent = `${data.athlete.isPro ? "Pro" : "Amateur"} · Ultimate League #${data.athlete.ultimateRank} · ${data.panels.length} published ${data.panels.length === 1 ? "panel" : "panels"}`;
            const portrait = el<HTMLImageElement>("portrait");
            portrait.src = asset(data.athlete.portrait) ?? portrait.dataset["fallback"]!;
            el("proofCount").textContent = `(${data.proofs.length})`;
            el<HTMLButtonElement>("openProofs").disabled = data.proofs.length === 0;
            el("shareStatus").textContent = "";
            el("copyFallback").hidden = true;
            el("workspace").hidden = false;
            el("loadStatus").hidden = true;
            render(false);
            saveView(push);
        } catch (error) { if (sequence === attempt) showError(error); }
    }

    function render(announce: boolean): void {
        if (!data) return;
        el("phenoClock").setAttribute("aria-pressed", String(clockId === "pheno"));
        el("bortzClock").setAttribute("aria-pressed", String(clockId === "bortz"));
        const clock = data[clockId];
        el("unavailable").hidden = clock !== null;
        el("walkthrough").hidden = clock === null;
        if (!clock) {
            el("unavailable").textContent = `${data.athlete.name} has no eligible published ${title(clockId)} panel. ${clockId === "bortz" ? "Bortz needs the complete 22-marker panel. Open Pheno to see the available path." : "A complete nine-marker Pheno panel is needed to trace this clock."}`;
            return;
        }
        const label = title(clockId);
        const changedCaps = clock.terms.filter(t => t.input !== t.scored).length;
        el("canvasCaption").textContent = `${label.toUpperCase()} PATH · PANEL ${clock.panelNumber} · ${date(clock.date).toUpperCase()}`;
        const snapshot = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", timeZone: "Asia/Singapore" }).format(new Date(data.asOfUtc));
        el("snapshotDate").textContent = `Live snapshot · ${snapshot} SGT`;
        const nodeValues = [dateShort(clock.date), `${clock.lab.length}`, `${changedCaps}`, number(clock.age, 1), signed(clock.ageDifference, 1), `#${clock.rank}`];
        const nodeNotes = ["original values", "blood markers", "limits applied", `${label} years`, "years vs. age at test", `${label} Age League`];
        pipeline.innerHTML = stages.map((name, i) => `<button type="button" class="pipe-node${i < step ? " reached" : ""}${i === step && announce && !reducedMotion.matches ? " arrived" : ""}" data-step="${i}"${i === step ? ' aria-current="step"' : ""}><span class="node-num">0${i + 1}</span><span class="node-name">${name}</span><strong>${nodeValues[i]}</strong><small>${nodeNotes[i]}</small></button>`).join("");
        el("stageNumber").textContent = `STEP 0${step + 1} / 06`;
        const descriptions = [
            `${label} uses one complete test, not a collection of each marker’s best-ever values. These are the numbers reported in the selected panel.`,
            "Different clocks speak different units. Here’s what changes before the numbers enter this model.",
            "LWC applies its competition scoring limits to model inputs. Your original blood results stay as reported.",
            clockId === "pheno" ? "Each input becomes a weighted model term. The model then turns their sum into a Pheno age." : "Each input is compared with its model reference mean. The weighted differences add up to age acceleration.",
            "Subtract the athlete’s chronological age on this test date. That signed difference is the competition score for this clock.",
            `This score places the athlete in the ${label} Age League. Lower age differences rank higher.`
        ];
        const titles = ["Start with the actual test.", "Get the units talking.", "Where the scoring limits kick in.", "Inside the clock.", "One subtraction. The score.", "Now it has a place."];
        el("stageTitle").textContent = titles[step]!;
        el("stageDescription").textContent = descriptions[step]!;
        body.innerHTML = [renderPanel, renderInputs, renderCaps, renderModel, renderDifference, renderRank][step]!(clock);
        el<HTMLButtonElement>("previousStep").disabled = step === 0;
        el<HTMLButtonElement>("nextStep").disabled = step === 5;
        el("stepPosition").textContent = `${step + 1} of 6`;
        if (announce) el("stageStatus").textContent = `Step ${step + 1} of 6: ${titles[step]}`;
    }

    function renderPanel(clock: Clock): string {
        if (!data) return "";
        const count = clockId === "pheno" ? clock.phenoPanelCount : clock.bortzPanelCount;
        const history = data.panels.map(p => {
            const age = clockId === "pheno" ? p.phenoAge : p.bortzAge;
            const selected = p.number === clock.panelNumber;
            return `<div class="test-row${selected ? " selected" : ""}"><span>${escape(date(p.date))}<small>Panel ${p.number}${selected ? " · selected" : age === null ? " · incomplete for this clock" : ""}</small></span><span class="num">${age === null ? "—" : `${number(age, 2)} yr`}</span></div>`;
        }).join("");
        return `<div class="panel-layout"><section><h3>Reported blood markers <span class="muted">· ${escape(date(clock.date))}</span></h3><dl class="lab-grid">${clock.lab.map(l => `<div class="lab-value"><dt>${escape(l.name)}</dt><dd class="num">${rawNumber(l.value)}<small>${escape(l.unit)}</small></dd></div>`).join("")}</dl></section><aside class="panel-history"><h3>Why this panel?</h3><p class="muted">It produced the lowest eligible ${clockId} age across ${count} complete ${count === 1 ? "test" : "tests"}.</p>${history}<p class="explanation-note">Equal biological ages keep the first panel in the published order. Each clock makes its own selection.</p><p class="explanation-note">Chronological age on this test: <strong class="num">${number(clock.ageAtTest)} years</strong>.</p></aside></div>`;
    }

    function renderInputs(clock: Clock): string {
        const transformed = clock.terms.filter(t => t.conversion);
        const rows = transformed.map(t => `<div class="transform-row"><div><h3>${escape(t.name)}</h3><small class="muted">${escape(t.conversion!)}</small></div><div class="formula"><span><strong>${number(t.reported, 3)}</strong> <small>${escape(t.unit)}</small></span><span class="arrow" aria-hidden="true">→</span><span><strong>${number(t.input, 5)}</strong> <small>${escape(t.inputUnit)}</small></span></div></div>`).join("");
        const logs = clock.terms.filter(t => t.transform).map(t => escape(t.name)).join(", ");
        return `<div class="transform-list">${rows}<div class="transform-row"><div><h3>Chronological age</h3><small class="muted">Measured on the selected test date</small></div><div class="formula"><strong>${number(clock.ageAtTest, 5)}</strong><small>years</small></div></div></div><p class="unchanged">The remaining blood values already use the model’s input units.</p><p class="explanation-note">Natural logarithms come after scoring limits in the clock stage: ${logs}. ${clockId === "bortz" ? "WBC supports the two cell-count conversions; it is not a separate weighted term." : "Pheno uses CRP in mg/dL; Bortz uses CRP in mg/L."}</p>`;
    }

    function renderCaps(clock: Clock): string {
        return `<div class="cap-grid">${clock.terms.filter(t => t.cap !== null).map(t => {
            const changed = t.input !== t.scored;
            const rule = t.capMode === "floor" ? "Minimum scored value" : "Maximum scored value";
            return `<div class="cap-row${changed ? " changed" : ""}"><div class="cap-label"><span>${escape(t.name)}</span><small>${changed ? "LIMIT APPLIED" : "UNCHANGED"}</small></div><div class="formula num"><span>${rawNumber(t.input)}</span><span aria-hidden="true">→</span><strong>${rawNumber(t.scored)}</strong><small>${escape(t.inputUnit)}</small></div><p>${rule}: ${rawNumber(t.cap)} ${escape(t.inputUnit)}</p></div>`;
        }).join("")}</div><p class="explanation-note">Values within the limits pass through unchanged. These are LWC scoring boundaries, not individual treatment targets.</p>`;
    }

    function renderModel(clock: Clock): string {
        const max = Math.max(...clock.terms.map(t => Math.abs(t.contribution)), 1);
        const terms = clock.terms.map(t => {
            const width = Math.abs(t.contribution) / max * 48;
            const left = t.contribution < 0 ? 50 - width : 50;
            const expression = clockId === "bortz" ? `(${exact(t.model)} − ${exact(t.mean!)}) × ${exact(t.coefficient)} × 10` : `${exact(t.model)} × ${exact(t.coefficient)}`;
            return `<details class="term"><summary><span>${escape(t.name)}</span><span class="term-bar" aria-hidden="true"><i class="${t.contribution > 0 ? "positive" : "negative"}" style="left:${left}%;width:${width}%"></i></span><span class="term-number">${signed(t.contribution, 3)}</span></summary><div class="term-detail">Model input: ${t.transform ? `ln(${exact(t.scored)}) = ` : ""}${exact(t.model)}<br>${expression} = ${exact(t.contribution)}${clockId === "bortz" ? " years" : " weighted units"}</div></details>`;
        }).join("");
        const equations = clock.calculation.map(c => `<div class="equation"><small>${escape(c.name)}</small><strong class="num">${number(c.value, c.unit === "years" ? 2 : 5)}</strong> <span class="muted">${escape(c.unit)}</span><code>${escape(c.formula)}</code>${c.value === null ? "<p class='muted'>The intermediate value is not finite; the model’s age floor still applies.</p>" : ""}</div>`).join("");
        return `<div class="model-layout"><section><h3>${clockId === "bortz" ? "Contributions to age acceleration · years" : "Weighted model terms · not years"}</h3><p class="muted">Open any row to see its full-precision arithmetic.</p>${terms}<p class="explanation-note">${clockId === "bortz" ? "Terms sum to age acceleration, which is added to age at test. The final age has a floor of zero." : "The terms feed a mortality-model transformation. A term is not a standalone number of years added or removed."}</p></section><section class="model-equations"><h3>The calculation</h3>${equations}</section></div>`;
    }

    function renderDifference(clock: Clock): string {
        const description = clock.ageDifference < 0 ? `${number(Math.abs(clock.ageDifference))} model years younger than their age on this test date.` : clock.ageDifference > 0 ? `${number(clock.ageDifference)} model years older than their age on this test date.` : "The clock’s age equals chronological age on this test date.";
        return `<div class="age-equation"><div><div class="age-number num">${number(clock.age)}</div><p>${title(clockId)} age</p></div><span class="operator" aria-hidden="true">−</span><div><div class="age-number num">${number(clock.ageAtTest)}</div><p>Age at test</p></div><span class="operator" aria-hidden="true">=</span><div class="result"><div class="age-number num">${signed(clock.ageDifference)}</div><p>Age difference · years</p></div></div><p class="age-caption">${description}</p><details class="precise"><summary>See the full-precision calculation</summary><code>${exact(clock.age)} − ${exact(clock.ageAtTest)} = ${exact(clock.ageDifference)} years</code></details><p class="explanation-note">The comparison uses ${escape(date(clock.date))}${clock.date ? ", not today’s age" : "; the date is missing, so the canonical calculator uses the snapshot date"}. It is a blood-based model estimate.</p>`;
    }

    function renderRank(clock: Clock): string {
        if (!data) return "";
        const ultimate = clock.suppliesUltimateScore ? `This ${title(clockId)} score supplies ${escape(data.athlete.name)}’s Ultimate League result: #${data.athlete.ultimateRank}.` : `This athlete’s Bortz score supplies their Ultimate League result. Their Pheno rank is a separate clock view.`;
        const rows = clock.neighbors.map(n => `<div class="neighbor${n.slug === data!.athlete.slug ? " current" : ""}"><span class="num">${n.rank}</span><a href="?${escape(new URLSearchParams({ athlete: n.slug, clock: clockId, step: "6" }).toString())}" data-neighbor="${escape(n.slug)}">${escape(n.name)}</a><span class="num">${signed(n.ageDifference)}</span></div>`).join("");
        return `<div class="ranking-layout"><section><div class="rank-hero"><span>#</span>${clock.rank}</div><p class="rank-subtitle">${title(clockId)} Age League · ${clock.fieldSize} athletes</p><a href="/league/${clockId}">Open this leaderboard ↗</a><div class="ranking-rules"><p>${ultimate}</p><p>The Ultimate League places Pro athletes (Bortz) before Amateurs (Pheno). Within a clock, the signed age difference sorts ascending. Exact ties favor the earlier date of birth, then the athlete’s name.</p></div></section><section><h3>The neighbors · age difference in years</h3>${rows}<p class="explanation-note">Ranking uses full-precision scores. These places and neighbors belong to the snapshot shown above.</p></section></div>`;
    }

    function goTo(next: number, push = true, manual = true): void {
        if (manual) stopPlay();
        step = Math.max(0, Math.min(5, next));
        render(true);
        saveView(push);
    }

    function schedulePlay(): void {
        playTimer = window.setTimeout(() => {
            if (!playing) return;
            if (step >= 5) { stopPlay(); return; }
            goTo(step + 1, false, false);
            if (step < 5) schedulePlay(); else stopPlay();
        }, 6500);
    }

    async function copyView(): Promise<void> {
        if (copyBusy || !data) return;
        copyBusy = true;
        const button = el<HTMLButtonElement>("copyLink");
        button.setAttribute("aria-busy", "true");
        const link = location.href;
        try {
            await navigator.clipboard.writeText(link);
            if (link !== location.href) return;
            el("shareStatus").textContent = "This view’s link is copied.";
            el("copyFallback").hidden = true;
        } catch {
            if (link !== location.href) return;
            const fallback = el<HTMLInputElement>("copyFallback");
            fallback.value = link;
            fallback.hidden = false;
            fallback.focus();
            fallback.select();
            el("shareStatus").textContent = "Copy access wasn’t available. The link is selected below.";
        } finally { copyBusy = false; button.removeAttribute("aria-busy"); }
    }

    function showProof(index: number): void {
        if (!data || !proofDialog.open) return;
        proofIndex = Math.max(0, Math.min(data.proofs.length - 1, index));
        const attempt = ++proofSequence;
        const source = asset(data.proofs[proofIndex]!);
        el("proofPosition").textContent = `Page ${proofIndex + 1} of ${data.proofs.length}`;
        el<HTMLButtonElement>("previousProof").disabled = proofIndex === 0;
        el<HTMLButtonElement>("nextProof").disabled = proofIndex === data.proofs.length - 1;
        el("retryProof").hidden = true;
        proofImage.hidden = true;
        el("proofStatus").textContent = "Loading the published evidence page…";
        el("proofPages").innerHTML = data.proofs.map((_, i) => `<button type="button" data-proof="${i}"${i === proofIndex ? ' aria-current="page"' : ""}>${i + 1}</button>`).join("");
        const fail = (): void => {
            if (attempt !== proofSequence) return;
            el("proofStatus").textContent = "This evidence page couldn’t be loaded. Retry or open the original below.";
            el("retryProof").hidden = !source;
        };
        const original = el<HTMLAnchorElement>("originalProof");
        original.hidden = source === null;
        if (!source) { fail(); return; }
        original.href = source;
        const image = new Image();
        image.onload = () => {
            if (attempt !== proofSequence || !proofDialog.open) return;
            proofImage.src = source;
            proofImage.alt = `Published evidence for ${data!.athlete.name}, page ${proofIndex + 1}`;
            proofImage.hidden = false;
            el("proofStatus").textContent = "";
        };
        image.onerror = fail;
        image.src = source;
    }

    search.disabled = true;
    search.addEventListener("input", showSearch);
    search.addEventListener("focus", () => { if (directory.length) { search.select(); showSearch(); } });
    search.addEventListener("keydown", event => {
        if (event.key === "Escape") { closeSearch(); return; }
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            if (results.hidden) showSearch();
            if (!matches.length) return;
            activeResult = activeResult < 0 ? (event.key === "ArrowDown" ? 0 : matches.length - 1) : (activeResult + (event.key === "ArrowDown" ? 1 : -1) + matches.length) % matches.length;
            results.querySelectorAll("[role=option]").forEach((option, i) => option.setAttribute("aria-selected", String(i === activeResult)));
            const option = el(`athlete-option-${activeResult}`);
            search.setAttribute("aria-activedescendant", option.id);
            option.scrollIntoView({ block: "nearest" });
        } else if (event.key === "Enter" && !results.hidden) {
            event.preventDefault();
            if (activeResult >= 0) selectResult(activeResult); else if (matches.length === 1) selectResult(0);
        }
    });
    results.addEventListener("click", event => {
        const button = (event.target as Element).closest<HTMLElement>("[data-result]");
        if (button) selectResult(Number(button.dataset["result"]));
    });
    document.addEventListener("pointerdown", event => { if (!(event.target as Element).closest(".athlete-search")) closeSearch(); });
    document.addEventListener("focusin", event => { if (!(event.target as Element).closest(".athlete-search")) closeSearch(); });
    el("retryLoad").addEventListener("click", () => { if (directory.length) void loadAthlete(pendingView, false); else void initialize(); });
    for (const id of ["pheno", "bortz"] as const) el(`${id}Clock`).addEventListener("click", () => { stopPlay(); clockId = id; step = 0; render(true); saveView(true); });
    pipeline.addEventListener("click", event => {
        const button = (event.target as Element).closest<HTMLElement>("[data-step]");
        if (button) { const index = Number(button.dataset["step"]); goTo(index); pipeline.querySelector<HTMLButtonElement>(`[data-step="${index}"]`)?.focus(); }
    });
    pipeline.addEventListener("keydown", event => {
        const next = event.key === "ArrowRight" ? step + 1 : event.key === "ArrowLeft" ? step - 1 : event.key === "Home" ? 0 : event.key === "End" ? 5 : null;
        if (next !== null) { event.preventDefault(); goTo(next); pipeline.querySelector<HTMLButtonElement>(`[data-step="${step}"]`)?.focus(); }
    });
    el("previousStep").addEventListener("click", () => goTo(step - 1));
    el("nextStep").addEventListener("click", () => goTo(step + 1));
    playButton.addEventListener("click", () => {
        if (playing) { stopPlay(); return; }
        if (step === 5) goTo(0, false);
        playing = true;
        playButton.innerHTML = 'Pause walkthrough <span aria-hidden="true">Ⅱ</span>';
        playButton.setAttribute("aria-pressed", "true");
        schedulePlay();
    });
    document.addEventListener("visibilitychange", () => { if (document.hidden) stopPlay(); });
    el("copyLink").addEventListener("click", () => { void copyView(); });
    el("portrait").addEventListener("error", () => { const image = el<HTMLImageElement>("portrait"); if (image.src !== new URL(image.dataset["fallback"]!, location.origin).href) image.src = image.dataset["fallback"]!; });
    body.addEventListener("click", event => {
        const link = (event.target as Element).closest<HTMLAnchorElement>("[data-neighbor]");
        if (link && !(event as MouseEvent).ctrlKey && !(event as MouseEvent).metaKey && !(event as MouseEvent).shiftKey && (event as MouseEvent).button === 0) { event.preventDefault(); void loadAthlete({ slug: link.dataset["neighbor"]!, clock: clockId, step: 5 }, true); }
    });
    el("openProofs").addEventListener("click", () => { stopPlay(); proofDialog.showModal(); showProof(0); });
    el("closeProofs").addEventListener("click", () => proofDialog.close());
    proofDialog.addEventListener("close", () => { proofSequence++; });
    el("previousProof").addEventListener("click", () => showProof(proofIndex - 1));
    el("nextProof").addEventListener("click", () => showProof(proofIndex + 1));
    el("retryProof").addEventListener("click", () => showProof(proofIndex));
    el("proofPages").addEventListener("click", event => { const button = (event.target as Element).closest<HTMLElement>("[data-proof]"); if (button) showProof(Number(button.dataset["proof"])); });
    window.addEventListener("popstate", () => {
        const view = readView();
        if (data && view.slug === data.athlete.slug) { stopPlay(); clockId = view.clock ?? (data.athlete.isPro ? "bortz" : "pheno"); step = view.step; render(true); }
        else void loadAthlete(view, false);
    });
    void initialize();
})();
