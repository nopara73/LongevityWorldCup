(() => {
    const script = document.currentScript;
    const list = script?.previousElementSibling;
    if (!list?.matches('.ro-milestones')) return;

    const guide = list.closest('.ro-guide');
    const defaultPortrait = script.dataset.defaultPortrait || '';
    const motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
    const moving = () => !motionQuery.matches;

    // ---- Closing timeline: live states, rail legs and the ticking clock ----

    const items = [...list.children];
    // Date-only milestones include their entire UTC calendar day, independently
    // of the visitor's timezone. Keep the authored dates as the source of truth.
    const ends = items.map(item => Date.parse(item.querySelector('time')?.dateTime) + 864e5);
    const timelineValid = items.length && !ends.some((end, i) => !Number.isFinite(end) || (i > 0 && end <= ends[i - 1]));

    const labels = { past: 'Deadline passed.', next: 'Next deadline.', future: 'Upcoming deadline.' };
    let timer;
    let clockTimer;
    let clock;

    // The clock is a row of single characters so only the digits that change roll.
    const renderClock = (host, text) => {
        if (!clock || clock.parentElement !== host) {
            clock?.remove();
            clock = document.createElement('span');
            clock.className = 'ro-clock';
            clock.setAttribute('aria-hidden', 'true');
            host.querySelector('time')?.after(clock);
        }
        const cells = clock.children;
        if (cells.length !== text.length) {
            clock.replaceChildren(...[...text].map(char => {
                const cell = document.createElement('span');
                cell.textContent = char;
                if (!/\d/.test(char)) cell.className = 'is-sep';
                return cell;
            }));
            return;
        }
        [...text].forEach((char, i) => {
            const cell = cells[i];
            if (cell.textContent === char) return;
            cell.textContent = char;
            if (!moving()) return;
            cell.classList.remove('is-rolling');
            void cell.offsetWidth;
            cell.classList.add('is-rolling');
        });
    };

    const pad = value => String(value).padStart(2, '0');
    const tick = () => {
        clearTimeout(clockTimer);
        const next = ends.findIndex(end => Date.now() < end);
        if (next === -1) {
            clock?.remove();
            clock = null;
            return;
        }
        const left = Math.max(0, ends[next] - Date.now());
        const days = Math.floor(left / 864e5);
        const hours = Math.floor(left / 36e5) % 24;
        const minutes = Math.floor(left / 6e4) % 60;
        const seconds = Math.floor(left / 1e3) % 60;
        renderClock(items[next], `${days}d ${pad(hours)}:${pad(minutes)}:${pad(seconds)}`);
        if (!document.hidden) clockTimer = setTimeout(tick, 1000 - (Date.now() % 1000) + 8);
    };

    const refresh = () => {
        clearTimeout(timer);
        const now = Date.now();
        if (!Number.isFinite(now)) return;
        const next = ends.findIndex(end => now < end);
        items.forEach((item, i) => {
            const state = next === -1 || i < next ? 'past' : i === next ? 'next' : 'future';
            if (item.dataset.roState !== state) {
                item.dataset.roState = state;
                item.setAttribute('aria-label', labels[state]);
                if (state === 'next') item.setAttribute('aria-current', 'step');
                else item.removeAttribute('aria-current');
            }
            const leg = i + 1 < ends.length ? (now - ends[i]) / (ends[i + 1] - ends[i]) : 0;
            item.style.setProperty('--ro-leg', String(Math.min(1, Math.max(0, leg))));
        });
        list.dataset.roLive = '';
        tick();
        // Refresh progress while open, and meet the next deadline exactly.
        if (next !== -1) timer = setTimeout(refresh, Math.min(60000, ends[next] - now));
    };

    if (timelineValid) {
        refresh();
        window.addEventListener('pageshow', refresh);
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden) refresh();
        });
    }

    // ---- Scroll reveals: below-the-fold bands arrive as they enter the viewport ----

    const revealed = new WeakMap();
    const onReveal = (element, callback) => {
        if (!element) return;
        const callbacks = revealed.get(element);
        if (callbacks === true) callback();
        else if (callbacks) callbacks.push(callback);
        else revealed.set(element, [callback]);
    };
    const reveal = element => {
        const callbacks = revealed.get(element);
        revealed.set(element, true);
        element.classList.add('is-in');
        if (Array.isArray(callbacks)) callbacks.forEach(callback => callback());
    };

    const bands = guide ? [...guide.querySelectorAll('.ro-timeline, .ro-standings, .ro-actions')] : [];
    if (bands.length && 'IntersectionObserver' in window && moving()) {
        const fold = window.innerHeight;
        const observer = new IntersectionObserver(entries => entries.forEach(entry => {
            if (!entry.isIntersecting) return;
            observer.unobserve(entry.target);
            reveal(entry.target);
        }), { rootMargin: '0px 0px -12% 0px', threshold: 0.08 });
        bands.forEach(band => {
            // Anything already on screen when the script runs stays put: no flash, no late entrance.
            if (band.getBoundingClientRect().top < fold * 0.88) {
                reveal(band);
                return;
            }
            band.classList.add('ro-reveal');
            observer.observe(band);
        });
    } else {
        bands.forEach(reveal);
    }

    // ---- Standings board: the current top of the Ultimate League ----

    const standings = guide?.querySelector('[data-ro-standings]');
    if (!standings) return;

    const podium = standings.querySelector('.ro-podium');
    const board = standings.querySelector('.ro-board');
    const formatScore = (value, decimals) => (value > 0 ? '+' : '') + value.toFixed(decimals) + ' years';

    const make = (tag, className, text) => {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (text != null) element.textContent = text;
        return element;
    };

    const portrait = (row, className) => {
        const img = make('img', className);
        img.src = row.thumb || defaultPortrait;
        img.alt = '';
        img.width = 96;
        img.height = 96;
        img.decoding = 'async';
        img.addEventListener('error', () => img.classList.add('is-missing'), { once: true });
        return img;
    };

    const climbOf = row => Number.isFinite(row.lastMonthRank) && row.lastMonthRank > row.rank
        ? row.lastMonthRank - row.rank
        : 0;

    const climbCue = places => {
        const cue = make('span', 'ro-climb');
        const label = places === 1 ? 'Ranked up 1 place since last month' : `Ranked up ${places} places since last month`;
        cue.title = label;
        cue.setAttribute('aria-label', label);
        cue.innerHTML = '<svg viewBox="0 0 10 10" aria-hidden="true"><path d="M5 1.5 9 7H1z" fill="currentColor"/></svg>';
        cue.append(make('span', null, String(places)));
        return cue;
    };

    const scoreCell = (className, row, scale) => {
        const score = make('span', className, formatScore(row.ageReduction, row.decimals));
        score.dataset.value = String(row.ageReduction);
        score.dataset.decimals = String(row.decimals);
        score.style.setProperty('--ro-score', scale.toFixed(3));
        return score;
    };

    const renderStep = (row, scale) => {
        const step = make('li', 'ro-step');
        step.dataset.place = String(row.rank);
        const link = make('a', 'ro-step-link');
        link.href = row.path;
        const frame = make('span', 'ro-step-portrait');
        frame.append(portrait(row, null));
        const block = make('span', 'ro-step-block');
        block.append(make('span', 'ro-step-rank', String(row.rank)));
        link.append(frame, make('span', 'ro-step-name', row.name), scoreCell('ro-step-score', row, scale), block);
        const climb = climbOf(row);
        if (climb) frame.append(climbCue(climb));
        step.append(link);
        return step;
    };

    const renderRow = (row, scale) => {
        const item = make('li', 'ro-row');
        item.dataset.rank = String(row.rank);
        const link = make('a', 'ro-row-link');
        link.href = row.path;
        const bar = make('span', 'ro-row-bar');
        bar.style.setProperty('--ro-score', scale.toFixed(3));
        bar.setAttribute('aria-hidden', 'true');
        bar.append(make('span'));
        link.append(
            make('span', 'ro-row-rank', String(row.rank)),
            portrait(row, 'ro-row-portrait'),
            make('span', 'ro-row-name', row.name),
            bar,
            scoreCell('ro-row-score', row, scale));
        const climb = climbOf(row);
        if (climb) {
            item.classList.add('is-climber');
            link.append(climbCue(climb));
        }
        item.append(link);
        return item;
    };

    // Scores count up from zero in the board's own rhythm, then settle on the exact text.
    const countUp = (element, delay) => {
        const value = Number(element.dataset.value);
        const decimals = Number(element.dataset.decimals) || 1;
        if (!Number.isFinite(value)) return;
        const duration = 720;
        const start = performance.now() + delay;
        element.textContent = formatScore(0, decimals);
        const frame = time => {
            const t = Math.min(1, Math.max(0, (time - start) / duration));
            const eased = 1 - Math.pow(1 - t, 3);
            element.textContent = formatScore(value * eased, decimals);
            if (t < 1) requestAnimationFrame(frame);
            else element.textContent = formatScore(value, decimals);
        };
        requestAnimationFrame(frame);
    };

    const ease = 'cubic-bezier(0.2, 0.8, 0.2, 1)';
    const settle = 'cubic-bezier(0.34, 1.4, 0.64, 1)';

    const playEntrance = (rows, steps, boardRows) => {
        if (!moving()) {
            standings.classList.add('is-settled');
            return;
        }
        standings.classList.add('is-playing');
        // Podium: third, then second, then the champion lands last for the biggest moment.
        const order = [3, 2, 1];
        steps.forEach(step => {
            const beat = order.indexOf(Number(step.dataset.place));
            const delay = 80 + beat * 150;
            step.querySelector('.ro-step-block')?.animate(
                [{ transform: 'scaleY(0)' }, { transform: 'scaleY(1)' }],
                { duration: 340, delay, easing: ease, fill: 'backwards' });
            step.querySelector('.ro-step-portrait')?.animate(
                [{ opacity: 0, transform: 'translateY(-28px) scale(0.86)' }, { opacity: 1, transform: 'none' }],
                { duration: 420, delay: delay + 140, easing: settle, fill: 'backwards' });
            step.querySelectorAll('.ro-step-name, .ro-step-score').forEach(text => text.animate(
                [{ opacity: 0, transform: 'translateY(6px)' }, { opacity: 1, transform: 'none' }],
                { duration: 260, delay: delay + 260, easing: ease, fill: 'backwards' }));
            const score = step.querySelector('.ro-step-score');
            if (score) countUp(score, delay + 260);
            if (step.dataset.place === '1') {
                window.setTimeout(() => step.classList.add('is-crowned'), delay + 520);
            }
        });

        // Rows start from last month's order and overtake into today's, climbers flashing on arrival.
        const lastOrder = [...rows.slice(3)].sort((a, b) =>
            (Number.isFinite(a.lastMonthRank) ? a.lastMonthRank : 1e6 + a.rank) -
            (Number.isFinite(b.lastMonthRank) ? b.lastMonthRank : 1e6 + b.rank));
        const rowHeight = boardRows[0]?.getBoundingClientRect().height || 56;
        boardRows.forEach((item, i) => {
            const was = lastOrder.findIndex(row => String(row.rank) === item.dataset.rank);
            const offset = (was - i) * rowHeight;
            const delay = 520 + i * 70;
            item.animate(
                [
                    { opacity: 0, transform: `translateY(${offset + 18}px)` },
                    { opacity: 1, transform: `translateY(${offset}px)`, offset: 0.35 },
                    { opacity: 1, transform: 'none' }
                ],
                { duration: offset ? 760 : 460, delay, easing: ease, fill: 'backwards' });
            const score = item.querySelector('.ro-row-score');
            if (score) countUp(score, delay + 200);
            window.setTimeout(() => item.classList.add('is-landed'), delay + (offset ? 760 : 460));
        });
        window.setTimeout(() => standings.classList.add('is-settled'), 520 + boardRows.length * 70 + 900);
    };

    const render = rows => {
        if (!Array.isArray(rows) || rows.length < 3) throw new Error('No standings');
        const best = Math.max(...rows.map(row => Math.abs(Math.min(0, Number(row.ageReduction) || 0))), 1);
        const scaleOf = row => Math.max(0.04, Math.abs(Math.min(0, Number(row.ageReduction) || 0)) / best);
        const steps = rows.slice(0, 3).map(row => renderStep(row, scaleOf(row)));
        const boardRows = rows.slice(3).map(row => renderRow(row, scaleOf(row)));
        podium.replaceChildren(...steps);
        board.replaceChildren(...boardRows);
        board.start = 4;
        standings.removeAttribute('aria-busy');
        standings.classList.add('is-ready');
        onReveal(standings, () => playEntrance(rows, steps, boardRows));
        if (!standings.classList.contains('ro-reveal') && !standings.classList.contains('is-in')) reveal(standings);
    };

    const fail = () => {
        // Without data the finish line hands straight over to the closing actions.
        standings.remove();
    };

    fetch('/api/data/standings', { headers: { Accept: 'application/json' } })
        .then(response => {
            if (!response.ok) throw new Error(String(response.status));
            return response.json();
        })
        .then(render)
        .catch(fail);
})();
