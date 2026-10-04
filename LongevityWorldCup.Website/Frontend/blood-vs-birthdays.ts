(() => {
    interface Athlete {
        slug: string; name: string; portraitUrl: string; chronologicalAge: number;
        bortzAge: number; reductionYears: number; testDate: string; profileUrl: string; proofUrls: string[];
    }
    interface Round { left: Athlete; right: Athlete; winnerSlug: string; }
    interface Puzzle { version: number; id: string; day: string; timeZone: string; rounds: Round[]; }
    interface Response { serverNowUtc: string; nextPuzzleAtUtc: string; puzzle: Puzzle; }
    interface Progress {
        day: string; started: boolean; timed: boolean; cursor: number;
        answers: (string | null)[]; deadlineUtc: number | null;
    }
    interface Saved { version: number; games: Record<string, Progress>; completedDays: string[]; }

    const key = 'lwc.blood-vs-birthdays.v1';
    const root = document.getElementById('gameMain');
    if (!root) return;
    const element = <T extends HTMLElement>(id: string): T => {
        const value = document.getElementById(id);
        if (!value) throw new Error('Missing game element: ' + id);
        return value as T;
    };
    const intro = element('introPanel');
    const arena = element('arena');
    const results = element('resultsPanel');
    const cards = element('matchCards');
    const question = element('roundTitle');
    const timer = element('timer');
    const next = element<HTMLButtonElement>('nextButton');
    const start = element<HTMLButtonElement>('startButton');
    const timedMode = element<HTMLInputElement>('timedMode');
    const placeholder = root.dataset['placeholder'] || '';
    let puzzle: Puzzle | null = null;
    let progress: Progress | null = null;
    let pending: Response | null = null;
    let nextAt = Infinity;
    let clockOffset = 0;
    let loading = false;
    let roundLoading = '';
    let focusAfterLoad = false;
    let storageWorks = true;
    let memory = emptySaved();
    const number = new Intl.NumberFormat('en', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const dayFormat = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' });

    function emptySaved(): Saved { return { version: 1, games: {}, completedDays: [] }; }
    function object(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null; }
    function day(value: unknown): value is string { return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(value)); }
    function localAsset(value: unknown): value is string {
        return typeof value === 'string' && /^\/(athletes|generated|assets)\//.test(value) && !value.includes('..') && !value.includes('\\');
    }
    function isAthlete(value: unknown): value is Athlete {
        return object(value) && typeof value['slug'] === 'string' && typeof value['name'] === 'string'
            && localAsset(value['portraitUrl']) && typeof value['profileUrl'] === 'string'
            && /^\/athlete\/[a-z0-9_-]+$/i.test(value['profileUrl'])
            && ['chronologicalAge', 'bortzAge', 'reductionYears'].every(k => typeof value[k] === 'number' && Number.isFinite(value[k]))
            && day(value['testDate']) && Array.isArray(value['proofUrls']) && value['proofUrls'].length > 0 && value['proofUrls'].every(localAsset);
    }
    function isResponse(value: unknown): value is Response {
        if (!object(value) || !object(value['puzzle'])) return false;
        const candidate = value['puzzle'];
        return typeof value['serverNowUtc'] === 'string' && Number.isFinite(Date.parse(value['serverNowUtc']))
            && typeof value['nextPuzzleAtUtc'] === 'string' && Number.isFinite(Date.parse(value['nextPuzzleAtUtc']))
            && candidate['version'] === 1 && typeof candidate['id'] === 'string' && day(candidate['day'])
            && candidate['timeZone'] === 'Asia/Singapore' && Array.isArray(candidate['rounds']) && candidate['rounds'].length === 5
            && candidate['rounds'].every((round: unknown) => object(round) && isAthlete(round['left']) && isAthlete(round['right'])
                && round['winnerSlug'] === (round['left'].reductionYears > round['right'].reductionYears ? round['left'].slug : round['right'].slug));
    }
    function isProgress(value: unknown, candidate?: Puzzle): value is Progress {
        if (!object(value) || !day(value['day']) || typeof value['started'] !== 'boolean' || typeof value['timed'] !== 'boolean'
            || typeof value['cursor'] !== 'number' || !Number.isInteger(value['cursor']) || value['cursor'] < 0 || value['cursor'] > 5
            || !Array.isArray(value['answers']) || value['answers'].length > 5 || value['answers'].length < value['cursor']
            || value['answers'].length > value['cursor'] + 1
            || !(value['deadlineUtc'] === null || (typeof value['deadlineUtc'] === 'number' && Number.isFinite(value['deadlineUtc'])))) return false;
        return value['answers'].every((answer: unknown, index: number) => {
            if (answer !== null && typeof answer !== 'string') return false;
            const round = candidate?.rounds[index];
            return !round || answer === null || answer === round.left.slug || answer === round.right.slug;
        }) && (!candidate || value['day'] === candidate.day);
    }
    function readSaved(): Saved {
        if (!storageWorks) return memory;
        try {
            const raw: unknown = JSON.parse(localStorage.getItem(key) || 'null');
            if (!object(raw) || raw['version'] !== 1 || !object(raw['games']) || !Array.isArray(raw['completedDays'])) return emptySaved();
            const games: Record<string, Progress> = {};
            for (const [id, state] of Object.entries(raw['games'])) if (isProgress(state)) games[id] = state;
            return { version: 1, games, completedDays: raw['completedDays'].filter(day) };
        } catch { return memory; }
    }
    function save(): void {
        if (!puzzle || !progress) return;
        const saved = readSaved();
        saved.games[puzzle.id] = { ...progress, answers: [...progress.answers] };
        if (progress.answers.length === 5 && !saved.completedDays.includes(puzzle.day)) saved.completedDays.push(puzzle.day);
        saved.completedDays = [...new Set(saved.completedDays)].sort();
        saved.games = Object.fromEntries(Object.entries(saved.games).sort((a, b) => a[1].day.localeCompare(b[1].day)).slice(-90));
        memory = saved;
        try { localStorage.setItem(key, JSON.stringify(saved)); }
        catch { storageWorks = false; element('storageWarning').hidden = false; }
        renderStreak();
    }
    function syncSaved(): boolean {
        if (!puzzle || !progress) return false;
        const latest = readSaved().games[puzzle.id];
        if (isProgress(latest, puzzle) && (latest.answers.length > progress.answers.length || latest.cursor > progress.cursor
            || (latest.started && !progress.started)
            || (latest.cursor === progress.cursor && progress.deadlineUtc === null && latest.deadlineUtc !== null))) {
            progress = latest;
            return true;
        }
        return false;
    }
    function now(): number { return Date.now() + clockOffset; }
    function dateLabel(value: string): string { return dayFormat.format(new Date(value + 'T00:00:00Z')); }
    function today(): string { return new Date(now() + 8 * 3600000).toISOString().slice(0, 10); }
    function previousDay(value: string): string { return new Date(Date.parse(value + 'T00:00:00Z') - 86400000).toISOString().slice(0, 10); }
    function renderStreak(): void {
        const completed = new Set(readSaved().completedDays);
        let current = today();
        if (!completed.has(current)) current = previousDay(current);
        let streak = 0;
        while (completed.has(current)) { streak++; current = previousDay(current); }
        element('streakDisplay').textContent = `${streak} day${streak === 1 ? '' : 's'} streak`;
    }
    function score(): number { return puzzle && progress ? progress.answers.filter((answer, i) => answer === puzzle?.rounds[i]?.winnerSlug).length : 0; }
    function node<K extends keyof HTMLElementTagNameMap>(tag: K, className = '', text = ''): HTMLElementTagNameMap[K] {
        const value = document.createElement(tag); value.className = className; value.textContent = text; return value;
    }
    function link(href: string, text: string): HTMLAnchorElement {
        const value = node('a', '', text); value.href = href; value.target = '_blank'; value.rel = 'noopener'; return value;
    }
    function portrait(athlete: Athlete): HTMLImageElement {
        const image = node('img'); image.src = athlete.portraitUrl; image.alt = athlete.name;
        image.decoding = 'async'; image.onerror = () => { image.onerror = null; image.src = placeholder; }; return image;
    }
    function evidence(athlete: Athlete): HTMLElement {
        const links = node('div', 'game-evidence-links');
        links.append(link(athlete.profileUrl, 'Athlete profile ↗'));
        const details = node('details');
        details.append(node('summary', '', `Proofs (${athlete.proofUrls.length})`));
        const pages = node('div', 'game-proof-pages');
        athlete.proofUrls.forEach((url, i) => { const page = link(url, String(i + 1)); page.setAttribute('aria-label', `Proof page ${i + 1} for ${athlete.name}`); pages.append(page); });
        details.append(pages); links.append(details); return links;
    }
    function receipt(athlete: Athlete, scale: number): HTMLElement {
        const container = node('div', 'game-receipt');
        const stats = node('dl', 'game-lab-stats');
        for (const [label, value] of [['Birthday age', athlete.chronologicalAge], ['bortz age', athlete.bortzAge]] as const) {
            const section = node('div'); section.append(node('dt', '', label), node('dd', '', number.format(value))); stats.append(section);
        }
        const gap = node('p', 'game-gap-number'); gap.append(node('strong', '', number.format(athlete.reductionYears)), ' years younger');
        const track = node('div', 'game-age-track'); track.setAttribute('aria-hidden', 'true');
        track.style.setProperty('--blood-position', (Math.max(0, athlete.bortzAge) / scale * 100) + '%');
        track.style.setProperty('--birthday-position', (athlete.chronologicalAge / scale * 100) + '%');
        track.append(node('span', 'game-age-gap'));
        container.append(stats, gap, track, node('p', 'game-test-date', 'Blood test · ' + dateLabel(athlete.testDate)), evidence(athlete));
        return container;
    }
    function renderProgress(): void {
        if (!puzzle || !progress) return;
        const steps = element('roundProgress'); steps.replaceChildren();
        puzzle.rounds.forEach((round, i) => {
            const answered = i < progress!.answers.length;
            const correct = answered && progress!.answers[i] === round.winnerSlug;
            const step = node('li', answered ? (correct ? 'is-correct' : 'is-wrong') : (i === progress!.cursor ? 'is-current' : ''), answered ? (correct ? '✓' : '×') : String(i + 1));
            step.setAttribute('aria-label', `Round ${i + 1}${answered ? (correct ? ': correct' : ': incorrect') : ''}`);
            if (i === progress!.cursor) step.setAttribute('aria-current', 'step');
            steps.append(step);
        });
        element('roundScore').textContent = `${score()} / 5 correct`;
    }
    function renderMatch(): void {
        if (!puzzle || !progress) return;
        const round = puzzle.rounds[progress.cursor]; if (!round) return;
        const revealed = progress.answers.length > progress.cursor;
        const selected = progress.answers[progress.cursor];
        const scale = Math.max(round.left.chronologicalAge, round.right.chronologicalAge) * 1.08;
        cards.replaceChildren();
        [round.left, round.right].forEach((athlete, i) => {
            const wrap = node('article', 'game-choice-wrap');
            const button = node('button', 'game-choice' + (revealed && athlete.slug === round.winnerSlug ? ' is-winner' : ''));
            button.type = 'button'; button.dataset['slug'] = athlete.slug;
            button.disabled = revealed || (progress!.timed && progress!.deadlineUtc === null);
            button.setAttribute('aria-label', `Choose ${athlete.name}, birthday age ${number.format(athlete.chronologicalAge)} at the test`);
            const photo = node('div', 'game-photo'); photo.append(portrait(athlete));
            photo.append(node('span', 'game-choice-label', revealed && athlete.slug === round.winnerSlug ? '✓ Bigger age reduction' : (i === 0 ? 'A' : 'B')));
            if (revealed && selected === athlete.slug) photo.append(node('span', 'game-picked', 'Your pick'));
            const identity = node('div', 'game-identity');
            identity.append(node('span', 'game-athlete-name', athlete.name));
            identity.append(node('span', 'game-birthday-age', `Birthday age ${number.format(athlete.chronologicalAge)} at test`));
            if (!revealed) identity.append(node('span', 'game-pick-hint', 'Pick the bigger reduction →'));
            button.append(photo, identity); button.addEventListener('click', () => answer(athlete.slug));
            wrap.append(button); if (revealed) wrap.append(receipt(athlete, scale)); cards.append(wrap);
        });
        element('answerPanel').hidden = !revealed;
        if (revealed) {
            const winner = round.left.slug === round.winnerSlug ? round.left : round.right;
            const correct = selected === round.winnerSlug;
            element('answerHeading').textContent = selected === null ? 'Time got away. The blood test didn’t.' : (correct ? 'Correct. Your instincts clocked it.' : 'The blood test has a plot twist.');
            element('answerDetail').textContent = `${winner.name} beats their birthday by ${number.format(winner.reductionYears)} years.`;
            next.textContent = progress.cursor === 4 ? 'See my score →' : 'Next matchup →';
        }
        renderProgress(); tick();
    }
    async function warmImages(round: Round): Promise<void> {
        await Promise.all([round.left, round.right].map(athlete => new Promise<void>(resolve => {
            const image = new Image();
            const timeout = window.setTimeout(finish, 4000);
            function finish(): void { clearTimeout(timeout); image.onload = null; image.onerror = null; resolve(); }
            image.onload = finish; image.onerror = finish; image.src = athlete.portraitUrl;
        })));
    }
    async function armRound(focus: boolean): Promise<void> {
        if (!puzzle || !progress || progress.answers.length > progress.cursor) return;
        const identity = puzzle.id + ':' + progress.cursor;
        if (roundLoading === identity) { focusAfterLoad ||= focus; return; }
        const round = puzzle.rounds[progress.cursor]; if (!round) return;
        roundLoading = identity;
        focusAfterLoad = focus;
        await warmImages(round);
        if (!puzzle || !progress || identity !== puzzle.id + ':' + progress.cursor || progress.answers.length > progress.cursor) return;
        roundLoading = '';
        syncSaved();
        if (progress.answers.length > progress.cursor) { render(); return; }
        if (progress.timed && progress.deadlineUtc === null) { progress.deadlineUtc = now() + 10000; save(); }
        renderMatch();
        if (focusAfterLoad) { question.focus({ preventScroll: true }); question.scrollIntoView({ block: 'nearest' }); }
        focusAfterLoad = false;
    }
    function answer(chosen: string | null): void {
        if (!puzzle || !progress || !progress.started || progress.cursor >= 5) return;
        syncSaved();
        if (progress.answers.length > progress.cursor) { render(); return; }
        if (progress.timed && progress.deadlineUtc === null) return;
        const round = puzzle.rounds[progress.cursor]; if (!round) return;
        if (progress.timed && now() >= (progress.deadlineUtc ?? Infinity)) chosen = null;
        if (chosen !== null && chosen !== round.left.slug && chosen !== round.right.slug) return;
        progress.answers.push(chosen); progress.deadlineUtc = null; save(); renderMatch(); next.focus({ preventScroll: true });
    }
    function tick(): void {
        if (!progress || !progress.started || progress.cursor >= 5) return;
        if (progress.answers.length > progress.cursor) { timer.textContent = 'Revealed'; timer.classList.remove('is-urgent'); return; }
        if (!progress.timed) { timer.textContent = 'Untimed'; return; }
        if (progress.deadlineUtc === null) { timer.textContent = 'Loading'; return; }
        const seconds = Math.max(0, Math.ceil((progress.deadlineUtc - now()) / 1000));
        timer.textContent = `${seconds}s`; timer.classList.toggle('is-urgent', seconds <= 3);
        if (seconds === 0) answer(null);
    }
    function renderResults(): void {
        if (!puzzle || !progress) return;
        const total = score();
        element('resultTitle').textContent = total === 5 ? 'Your instincts have good bloodwork.' : total >= 3 ? 'You’ve got blood instinct.' : 'Birthdays can be convincing.';
        const final = element('finalScore'); final.replaceChildren(node('span', '', String(total)), node('small', '', ' / 5'));
        const tiles = element('resultTiles'); tiles.replaceChildren();
        puzzle.rounds.forEach((round, i) => {
            const correct = progress!.answers[i] === round.winnerSlug;
            const tile = node('span', 'game-result-tile' + (correct ? ' is-correct' : ''), correct ? '✓' : '×');
            tile.setAttribute('aria-label', `Round ${i + 1}: ${correct ? 'correct' : 'incorrect'}`); tiles.append(tile);
        });
        element('resultMessage').textContent = total === 5 ? 'Five matchups. Five receipts. You read them all right.' : 'The calendar tells one story. The blood test sometimes tells another. Come back for a fresh five.';
        element('nextGameInfo').textContent = pending ? 'Today’s next set is ready above.' : 'A fresh five at midnight Singapore time.';
        const review = element('reviewRounds'); review.replaceChildren();
        puzzle.rounds.forEach((round, i) => {
            const section = node('section', 'game-review-round');
            const correct = progress!.answers[i] === round.winnerSlug;
            section.append(node('h3', '', `Round ${i + 1} · ${correct ? 'Correct' : progress!.answers[i] === null ? 'Time’s up' : 'Incorrect'}`));
            const people = node('div', 'game-review-athletes');
            [round.left, round.right].forEach(athlete => {
                const person = node('div', 'game-review-athlete');
                const profile = link(athlete.profileUrl, athlete.name); profile.prepend(portrait(athlete));
                person.append(profile, node('p', '', `${number.format(athlete.chronologicalAge)} birthday → ${number.format(athlete.bortzAge)} bortz`),
                    node('p', '', `${number.format(athlete.reductionYears)} years younger${athlete.slug === round.winnerSlug ? ' · bigger reduction' : ''}`),
                    node('p', '', dateLabel(athlete.testDate)), evidence(athlete)); people.append(person);
            });
            section.append(people); review.append(section);
        });
    }
    function render(): void {
        if (!puzzle || !progress) return;
        element('loadStatus').hidden = true; element('loadError').hidden = true;
        element('puzzleDate').textContent = dateLabel(puzzle.day);
        intro.hidden = progress.started; arena.hidden = !progress.started || progress.cursor >= 5; results.hidden = progress.cursor < 5;
        if (progress.cursor >= 5) renderResults();
        else if (progress.started) { renderMatch(); void armRound(false); }
        renderStreak();
    }
    function acceptResponse(response: Response): void {
        puzzle = response.puzzle; nextAt = Date.parse(response.nextPuzzleAtUtc);
        const stored = readSaved().games[puzzle.id];
        progress = isProgress(stored, puzzle) ? stored : { day: puzzle.day, started: false, timed: true, cursor: 0, answers: [], deadlineUtc: null };
        roundLoading = ''; pending = null; element('dayChange').hidden = true; render();
        element('shareStatus').textContent = ''; element('shareFallback').hidden = true;
    }
    async function loadToday(): Promise<void> {
        if (loading) return; loading = true;
        if (!puzzle) { element('loadStatus').hidden = false; element('loadError').hidden = true; }
        try {
            let response: unknown;
            for (let attempt = 0; attempt < 2; attempt++) {
                try {
                    const request = await fetch('/api/blood-vs-birthdays', { cache: 'no-store', signal: AbortSignal.timeout(10000) });
                    if (!request.ok) throw new Error('Daily matchups unavailable');
                    response = await request.json(); if (!isResponse(response)) throw new Error('Invalid daily puzzle'); break;
                } catch (error) { if (attempt === 1) throw error; }
            }
            if (!isResponse(response)) throw new Error('Invalid daily puzzle');
            clockOffset = Date.parse(response.serverNowUtc) - Date.now(); nextAt = Date.parse(response.nextPuzzleAtUtc);
            if (puzzle && progress?.started && puzzle.id !== response.puzzle.id) {
                pending = response; element('dayChange').hidden = false; renderStreak();
            } else if (puzzle?.id === response.puzzle.id) {
                if (syncSaved()) render();
                else { renderStreak(); tick(); }
            }
            else acceptResponse(response);
        } catch {
            element('loadStatus').hidden = true;
            if (!puzzle) element('loadError').hidden = false;
        } finally { loading = false; }
    }
    start.addEventListener('click', () => {
        if (!progress || !puzzle || progress.started) return;
        progress.started = true; progress.timed = timedMode.checked; save(); render(); void armRound(true);
    });
    next.addEventListener('click', () => {
        if (!progress || !puzzle) return;
        syncSaved(); if (progress.answers.length !== progress.cursor + 1) { render(); return; }
        progress.cursor++; progress.deadlineUtc = null; roundLoading = ''; save(); render();
        if (progress.cursor === 5) element('resultTitle').focus({ preventScroll: true });
        else void armRound(true);
    });
    element('retryButton').addEventListener('click', () => void loadToday());
    element('newDayButton').addEventListener('click', () => { if (pending) acceptResponse(pending); else void loadToday(); });
    element('shareButton').addEventListener('click', () => {
        if (!puzzle || !progress) return;
        const marks = puzzle.rounds.map((round, i) => progress!.answers[i] === round.winnerSlug ? '🟩' : '⬜').join('');
        const text = `Blood vs. Birthdays · ${puzzle.day}\n${score()}/5 ${progress.timed ? 'on the clock' : 'untimed'}\n${marks}\nhttps://longevityworldcup.com/blood-vs-birthdays`;
        const originatingId = puzzle.id;
        const shareStatus = element('shareStatus');
        const fallback = element<HTMLTextAreaElement>('shareFallback');
        fallback.hidden = true; shareStatus.textContent = '';
        const copy = navigator.clipboard?.writeText(text) ?? Promise.reject(new Error('Clipboard unavailable'));
        void copy.then(() => { if (puzzle?.id === originatingId) shareStatus.textContent = 'Score copied. The answers stay a mystery.'; }).catch(() => {
            if (puzzle?.id !== originatingId) return;
            fallback.value = text; fallback.hidden = false; fallback.focus(); fallback.select(); shareStatus.textContent = 'Your score is ready to copy below.';
        });
    });
    window.addEventListener('storage', event => { if (event.key === key) { if (syncSaved()) render(); else renderStreak(); } });
    window.addEventListener('focus', () => void loadToday());
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { tick(); void loadToday(); } });
    window.setInterval(tick, 200);
    window.setInterval(() => { if (now() >= nextAt) void loadToday(); }, 30000);
    void loadToday();
})();

export {};
