(() => {
    const script = document.currentScript;
    const list = script?.previousElementSibling;
    if (!list?.matches('.ro-milestones')) return;

    const guide = list.closest('.ro-guide');
    const standings = guide?.querySelector('[data-ro-standings]');
    // No-script pages use the finish-line fallback. Measure the enhanced board only after revealing it.
    if (standings) standings.hidden = false;
    const defaultPortrait = script.dataset.defaultPortrait || '';
    const motionQuery = typeof window.matchMedia === 'function' ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;
    const moving = () => !motionQuery?.matches;

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
        const cells = clock.querySelectorAll('.ro-clock-part > span');
        if (cells.length !== text.replace(' ', '').length) {
            clock.replaceChildren(...text.split(' ').map(part => {
                const group = document.createElement('span');
                group.className = 'ro-clock-part';
                group.append(...[...part].map(char => {
                const cell = document.createElement('span');
                cell.textContent = char;
                if (!/\d/.test(char)) cell.className = 'is-sep';
                return cell;
                }));
                return group;
            }));
            return;
        }
        [...text.replace(' ', '')].forEach((char, i) => {
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
        if (next !== -1 && !document.hidden) timer = setTimeout(refresh, Math.min(60000, ends[next] - now));
    };

    if (timelineValid) {
        refresh();
        window.addEventListener('pageshow', refresh);
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden) refresh();
            else { clearTimeout(timer); clearTimeout(clockTimer); }
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

    const bands = guide ? [...guide.querySelectorAll('.ro-body, .ro-timeline, .ro-standings, .ro-actions')] : [];
    if (bands.length && 'IntersectionObserver' in window && moving()) {
        const fold = window.innerHeight;
        const observer = new IntersectionObserver(entries => entries.forEach(entry => {
            if (!entry.isIntersecting) return;
            observer.unobserve(entry.target);
            reveal(entry.target);
        }), { rootMargin: '0px 0px -12% 0px', threshold: 0 });
        bands.forEach(band => {
            // Anything already on screen when the script runs stays put: no flash, no late entrance.
            if (band.getBoundingClientRect().top < fold) {
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

    if (!standings) return;

    const podium = standings.querySelector('.ro-podium');
    const board = standings.querySelector('.ro-board');
    const formatScore = (value, decimals) => Number.isFinite(value)
        ? (value > 0 ? '+' : '') + value.toFixed(decimals) + ' years' : '\u2014';

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
        img.dataset.athleteName = row.name;
        img.width = 96;
        img.height = 96;
        img.decoding = 'async';
        img.addEventListener('error', () => {
            if (defaultPortrait && img.getAttribute('src') !== defaultPortrait) img.src = defaultPortrait;
            else img.classList.add('is-missing');
        });
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
        if (Number.isFinite(row.ageReduction)) score.dataset.value = String(row.ageReduction);
        score.dataset.decimals = String(row.decimals);
        score.style.setProperty('--ro-score', scale.toFixed(3));
        return score;
    };

    const renderStep = (row, scale) => {
        const step = make('li', 'ro-step');
        step.dataset.place = String(row.rank);
        const link = make('a', 'ro-step-link');
        link.href = row.path;
        link.setAttribute('aria-label', `${row.name}, rank ${row.rank}, age reduction ${formatScore(row.ageReduction, row.decimals)}`);
        const frame = make('span', 'ro-step-portrait');
        frame.append(portrait(row, null));
        const block = make('span', 'ro-step-block');
        block.append(make('span', 'ro-step-rank', String(row.rank)));
        link.append(frame, make('span', 'ro-step-name', row.name), scoreCell('ro-step-score', row, scale), block);
        const climb = climbOf(row);
        if (climb) link.setAttribute('aria-label', `${link.getAttribute('aria-label')}, ranked up ${climb} ${climb === 1 ? 'place' : 'places'} since last month`);
        if (climb) frame.append(climbCue(climb));
        step.append(link);
        return step;
    };

    const renderRow = (row, scale) => {
        const item = make('li', 'ro-row');
        item.dataset.rank = String(row.rank);
        const link = make('a', 'ro-row-link');
        link.href = row.path;
        link.setAttribute('aria-label', `${row.name}, rank ${row.rank}, age reduction ${formatScore(row.ageReduction, row.decimals)}`);
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
        if (climb) link.setAttribute('aria-label', `${link.getAttribute('aria-label')}, ranked up ${climb} ${climb === 1 ? 'place' : 'places'} since last month`);
        if (climb) {
            item.classList.add('is-climber');
            link.append(climbCue(climb));
        }
        item.append(link);
        return item;
    };

    // Scores count up from zero in the board's own rhythm, then settle on the exact text.
    const entranceTimers = new Set();
    const frames = new Set();
    const later = (callback, delay) => {
        const id = setTimeout(() => { entranceTimers.delete(id); callback(); }, delay);
        entranceTimers.add(id);
    };
    const nextFrame = callback => {
        const id = requestAnimationFrame(time => { frames.delete(id); callback(time); });
        frames.add(id);
    };
    const settleEntrance = () => {
        entranceTimers.forEach(clearTimeout);
        entranceTimers.clear();
        frames.forEach(cancelAnimationFrame);
        frames.clear();
        standings.classList.remove('is-playing');
        standings.classList.add('is-settled', 'is-in');
        // Commit final styles before canceling transitions created by settling.
        void standings.offsetWidth;
        standings.getAnimations({ subtree: true }).forEach(animation => animation.cancel());
        standings.querySelectorAll('[data-value]').forEach(element => {
            element.textContent = formatScore(Number(element.dataset.value), Number(element.dataset.decimals));
        });
    };
    const countUp = (element, delay) => {
        if (!element.hasAttribute('data-value')) return;
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
            if (t < 1) nextFrame(frame);
            else element.textContent = formatScore(value, decimals);
        };
        nextFrame(frame);
    };

    const ease = 'cubic-bezier(0.2, 0.8, 0.2, 1)';
    const settle = 'cubic-bezier(0.34, 1.4, 0.64, 1)';

    const playEntrance = (rows, steps, boardRows) => {
        if (!moving() || standings.classList.contains('is-settled') || document.hidden) {
            settleEntrance();
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
                later(() => step.classList.add('is-crowned'), delay + 520);
            }
        });

        // Rows start from last month's order and overtake into today's, climbers flashing on arrival.
        const lastOrder = [...rows.slice(3)].sort((a, b) =>
            (Number.isFinite(a.lastMonthRank) ? a.lastMonthRank : 1e6 + a.rank) -
            (Number.isFinite(b.lastMonthRank) ? b.lastMonthRank : 1e6 + b.rank));
        const heights = new Map(boardRows.map(item => [Number(item.dataset.rank), item.getBoundingClientRect().height]));
        const top = (order, index) => order.slice(0, index).reduce((sum, row) => sum + heights.get(row.rank), 0);
        boardRows.forEach((item, i) => {
            const was = lastOrder.findIndex(row => String(row.rank) === item.dataset.rank);
            const offset = top(lastOrder, was) - top(rows.slice(3), i);
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
            later(() => item.classList.add('is-landed'), delay + (offset ? 760 : 460));
        });
        later(settleEntrance, 520 + boardRows.length * 70 + 900);
    };

    const render = rows => {
        if (!Array.isArray(rows) || rows.length < 3) throw new Error('No standings');
        const best = Math.max(...rows.map(row => Math.abs(Math.min(0, Number(row.ageReduction) || 0))), 1);
        const scaleOf = row => Math.max(0.04, Math.abs(Math.min(0, Number(row.ageReduction) || 0)) / best);
        const bronze = rows[2].ageReduction;
        const cut = scaleOf(rows[2]).toFixed(3);
        const steps = rows.slice(0, 3).map(row => renderStep(row, scaleOf(row)));
        const boardRows = rows.slice(3).map(row => {
            const scale = scaleOf(row);
            const item = renderRow(row, scale);
            // A tie, a stronger fill (possible across tracks), or a placeholder
            // score has no visible distance left to this bronze marker.
            item.classList.toggle('is-chasing', Number.isFinite(bronze) && bronze < 0
                && Number.isFinite(row.ageReduction) && Number(scale.toFixed(3)) < Number(cut));
            return item;
        });
        podium.replaceChildren(...steps);
        board.replaceChildren(...boardRows);
        board.start = 4;
        // The chasing pack measures itself against the last podium place.
        board.style.setProperty('--ro-cut', cut);
        standings.removeAttribute('aria-busy');
        standings.classList.add('is-ready');
        onReveal(standings, () => playEntrance(rows, steps, boardRows));
        if (!standings.classList.contains('ro-reveal') && !standings.classList.contains('is-in')) reveal(standings);
    };

    guide.addEventListener('focusin', event => {
        const band = event.target.closest('.ro-reveal');
        if (band) { band.classList.remove('ro-reveal'); reveal(band); }
        settleEntrance();
    });
    motionQuery?.addEventListener('change', () => {
        if (!moving()) { bands.forEach(reveal); settleEntrance(); }
    });
    document.addEventListener('visibilitychange', () => { if (document.hidden) settleEntrance(); });
    window.addEventListener('pagehide', () => {
        clearTimeout(timer);
        clearTimeout(clockTimer);
        settleEntrance();
    });

    const fail = () => {
        // Without data the finish line hands straight over to the closing actions.
        settleEntrance();
        standings.remove();
    };

    const controller = new AbortController();
    // The deadline includes JSON body consumption, not just response headers.
    const deadline = setTimeout(() => { controller.abort(); fail(); }, 10000);
    fetch('/api/data/standings', { signal: controller.signal, headers: { Accept: 'application/json' } })
        .then(response => {
            if (!response.ok) throw new Error(String(response.status));
            return response.json();
        })
        .then(rows => { if (!controller.signal.aborted) render(rows); })
        .catch(fail)
        .finally(() => clearTimeout(deadline));
})();

// The running lane: the poster's track continues down the page beside the
// sheet, from the poster's edge to the chequered finish line. The runner holds
// the reader's line (CSS sticky, so it tracks the scroll exactly); this block
// only measures the lane and lights each waypoint as the runner passes it.
(() => {
    const guide = document.querySelector('.ro-guide');
    const essay = guide?.querySelector('.ro-essay');
    if (!guide || !essay) return;
    const poster = guide.querySelector('.ro-artwork');

    const lane = document.createElement('div');
    lane.className = 'ro-lane';
    lane.setAttribute('aria-hidden', 'true');
    const trail = document.createElement('span');
    trail.className = 'ro-lane-trail';
    const fill = document.createElement('span');
    fill.className = 'ro-lane-fill';
    trail.append(fill);
    const runner = document.createElement('span');
    runner.className = 'ro-lane-runner';
    lane.append(trail, runner);
    guide.append(lane);

    let marks = [];
    let length = 0;
    let line = 0;
    let laneTop = 0;
    let frame = 0;
    let measuring = 0;
    let synced = false;

    const mark = (y, finish) => {
        const element = document.createElement('span');
        element.className = finish ? 'ro-lane-mark is-finish' : 'ro-lane-mark';
        element.style.top = `${y}px`;
        return { element, y };
    };

    // Offsets are layout positions, so the sheet's entrance transform never
    // shifts the waypoints off their sections. Everything is read before
    // anything is written, so a measure costs one style and layout pass.
    function layout() {
        measuring = 0;
        if (!synced) {
            synced = true;
            // The lane runs the sheet's entrance on the sheet's clock, so the
            // waypoints stay on their sections while the sheet slides in (and a
            // late script doesn't give the lane an entrance of its own).
            const sheetEntrance = essay.getAnimations?.().find(animation => animation.animationName === 'ro-sheet-up');
            const laneEntrance = lane.getAnimations?.().find(animation => animation.animationName === 'ro-sheet-up');
            if (sheetEntrance && laneEntrance) sheetEntrance.ready.then(() => { laneEntrance.startTime = sheetEntrance.startTime; }, () => {});
        }
        const finish = essay.querySelector(':scope > .ro-standings:not([hidden])') || essay.querySelector(':scope > .ro-actions');
        const laneStyle = getComputedStyle(lane);
        const shown = finish && laneStyle.display !== 'none';
        let top = 0;
        let stops = [];
        let gutter = 0;
        let scrollTop = 0;
        let lastScroll = 0;
        if (shown) {
            const posterEnd = poster ? poster.offsetTop + poster.offsetHeight : 0;
            // Where the axis passes the poster's corner (phones), the lane starts just clear of it.
            const lead = parseFloat(laneStyle.getPropertyValue('--ro-lane-lead')) || 0;
            top = Math.max(essay.offsetTop, posterEnd + lead);
            // Sections are offset from inside the sheet's border.
            const start = top - essay.offsetTop - essay.clientTop;
            // The finish waypoint sits on the middle of the chequered strip.
            const check = parseFloat(getComputedStyle(guide).getPropertyValue('--ro-check')) || 0;
            length = Math.max(0, finish.offsetTop + check - start);
            stops = [...essay.querySelectorAll(':scope > :is(.ro-body, .ro-timeline)')]
                .map(section => section.offsetTop - start)
                .filter(y => y > 0 && y < length);
            const guideBox = guide.getBoundingClientRect();
            gutter = guideBox.left + essay.offsetLeft;
            scrollTop = window.scrollY;
            laneTop = guideBox.top + scrollTop + top;
            lastScroll = document.documentElement.scrollHeight - window.innerHeight;
        }

        marks.forEach(({ element }) => element.remove());
        marks = [];
        if (!shown) { length = 0; return; }
        lane.style.top = `${top}px`;
        lane.style.left = `${essay.offsetLeft}px`;
        lane.style.height = `${length}px`;
        lane.style.setProperty('--ro-gutter', `${gutter}px`);
        marks = [mark(0, false), ...stops.map(y => mark(y, false)), mark(length, true)];
        lane.append(...marks.map(({ element }) => element));

        // The reader's line sits a little above the middle of the view; on a
        // short page it drops just far enough that the finish is always reached.
        line = Math.round(Math.min(window.innerHeight - 24, Math.max(window.innerHeight * 0.38, laneTop + length - lastScroll)));
        lane.style.setProperty('--ro-line', `${line}px`);
        update(scrollTop);
    }

    // Scrolling reads only the scroll position: the lane's place on the page is
    // measured in layout().
    function update(scrollTop = window.scrollY) {
        frame = 0;
        const at = Math.min(Math.max(line - (laneTop - scrollTop), 0), length);
        marks.forEach(({ element, y }) => element.classList.toggle('is-passed', y <= at + 0.5));
        lane.classList.toggle('is-finished', length > 0 && at >= length - 0.5);
    }

    const schedule = () => { if (!frame) frame = requestAnimationFrame(() => update()); };
    const remeasure = () => { if (!measuring) measuring = requestAnimationFrame(layout); };
    window.addEventListener('scroll', schedule, { passive: true });
    window.addEventListener('resize', remeasure, { passive: true });
    if (typeof ResizeObserver === 'function') {
        const resized = new ResizeObserver(remeasure);
        resized.observe(essay);
        // The page's height moves the lane's place and the last scroll position.
        resized.observe(document.body);
    }
    if (document.readyState !== 'complete') window.addEventListener('load', remeasure, { once: true });
    // First measure with the next frame's layout, not a forced one now.
    remeasure();
    // Waypoints take their first state without a transition.
    requestAnimationFrame(() => requestAnimationFrame(() => lane.classList.add('is-live')));
})();
