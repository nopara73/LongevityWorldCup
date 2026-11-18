(() => {
    const calendar = document.querySelector('.event-pulse');
    const grid = calendar.querySelector('.event-pulse-grid');
    const slot = calendar.parentElement;
    const rail = document.querySelector('.event-pulse-rail');
    const wide = matchMedia('(min-width: 1280px)');
    const formatter = new Intl.DateTimeFormat(undefined, { dateStyle: 'full', calendar: 'gregory', timeZone: 'UTC' });
    const weeks = new Map();
    let entries = new Map();
    let currentCell = null;
    let tabStop = null;
    let revealRow;

    function placeCalendar() {
        const parent = wide.matches ? rail : slot;
        if (calendar.parentElement === parent) return;
        const focus = calendar.contains(document.activeElement) ? document.activeElement : null;
        parent.appendChild(calendar);
        focus?.focus({ preventScroll: true });
    }
    placeCalendar();
    wide.addEventListener('change', placeCalendar);

    // Civil dates already belong to the feed's local timezone. UTC arithmetic
    // keeps their weekday and spacing independent of daylight-saving changes.
    const dayMs = 86400000;
    function dayNumber(key) {
        const [year, month, day] = key.split('-').map(Number);
        const date = new Date(0);
        date.setUTCFullYear(year, month - 1, day);
        return date.getTime() / dayMs;
    }
    const monday = day => day - ((day + 3) % 7 + 7) % 7;

    function createWeek(start) {
        const week = document.createElement('div');
        week.className = 'event-pulse-week';
        for (let i = 0; i < 7; i++) {
            const cell = document.createElement('button');
            cell.type = 'button';
            cell.className = 'event-pulse-cell';
            cell.disabled = true;
            cell.tabIndex = -1;
            cell.setAttribute('aria-hidden', 'true');
            if (new Date((start + i) * dayMs).getUTCDate() === 1) cell.classList.add('is-month-start');
            week.appendChild(cell);
        }
        return week;
    }

    function setTabStop(cell) {
        if (tabStop === cell) return;
        if (tabStop) tabStop.tabIndex = -1;
        tabStop = cell;
        if (cell) cell.tabIndex = 0;
    }

    function setReadingDay(key) {
        const cell = entries.get(key)?.cell || null;
        if (cell === currentCell) return;
        currentCell?.removeAttribute('aria-current');
        currentCell = cell;
        if (!cell) return;
        cell.setAttribute('aria-current', 'date');
        if (calendar.contains(document.activeElement)) return;
        setTabStop(cell);
        // Move only the calendar's own scrollport. The reader owns the page.
        const left = cell.offsetLeft, top = cell.offsetTop;
        if (left < calendar.scrollLeft) calendar.scrollLeft = left;
        else if (left + cell.offsetWidth > calendar.scrollLeft + calendar.clientWidth)
            calendar.scrollLeft = left + cell.offsetWidth - calendar.clientWidth;
        if (top < calendar.scrollTop) calendar.scrollTop = top;
        else if (top + cell.offsetHeight > calendar.scrollTop + calendar.clientHeight)
            calendar.scrollTop = top + cell.offsetHeight - calendar.clientHeight;
    }

    function update(units, reveal) {
        revealRow = reveal;
        const next = new Map();
        let busiest = 0, oldest = Infinity, newest = -Infinity;
        for (const [row] of units) {
            const key = row.dataset.day;
            if (!key) continue;
            let entry = next.get(key);
            if (!entry) {
                const day = entries.get(key)?.day ?? dayNumber(key);
                entry = { day, row, count: 0 };
                next.set(key, entry);
                oldest = Math.min(oldest, day);
                newest = Math.max(newest, day);
            }
            busiest = Math.max(busiest, ++entry.count);
        }

        const firstWeek = monday(oldest), lastWeek = monday(newest);
        for (const [start, week] of weeks) {
            if (next.size && start >= firstWeek && start <= lastWeek) continue;
            week.remove();
            weeks.delete(start);
        }
        let sibling = grid.firstElementChild;
        if (next.size) {
            for (let start = lastWeek; start >= firstWeek; start -= 7) {
                let week = weeks.get(start);
                if (!week) {
                    week = createWeek(start);
                    weeks.set(start, week);
                    grid.insertBefore(week, sibling);
                }
                sibling = week.nextElementSibling;
            }
        }
        for (const [key, old] of entries) {
            if (next.has(key)) continue;
            old.cell.disabled = true;
            old.cell.setAttribute('aria-hidden', 'true');
            old.cell.removeAttribute('aria-label');
            old.cell.removeAttribute('title');
            delete old.cell.dataset.day;
        }
        for (const [key, entry] of next) {
            const start = monday(entry.day);
            const cell = weeks.get(start).children[entry.day - start];
            entry.cell = cell;
            const old = entries.get(key);
            if (old?.count !== entry.count) {
                const date = formatter.format(new Date(entry.day * dayMs));
                const label = date + ' · ' + entry.count + (entry.count === 1 ? ' highlight' : ' highlights');
                cell.title = label;
                cell.setAttribute('aria-label', label);
                cell.removeAttribute('aria-hidden');
                cell.disabled = false;
                cell.dataset.day = key;
            }
            const level = Math.round(18 + 82 * Math.log1p(entry.count) / Math.log1p(busiest)) + '%';
            if (old?.level !== level) cell.style.setProperty('--pulse-level', level);
            entry.level = level;
        }
        entries = next;
        calendar.hidden = !next.size;
        slot.hidden = !next.size;
        if (!tabStop?.isConnected || tabStop.disabled) setTabStop(next.values().next().value?.cell || null);
    }

    grid.addEventListener('focusin', e => setTabStop(e.target.closest('button')));
    grid.addEventListener('keydown', e => {
        const entry = entries.get(e.target.dataset.day);
        if (!entry) return;
        const days = [...entries.values()];
        const direction = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -1, ArrowDown: 1 }[e.key];
        let target;
        if (e.key === 'Home') target = days[0];
        else if (e.key === 'End') target = days.at(-1);
        else if (direction) {
            const acrossWeeks = wide.matches ? ['ArrowUp','ArrowDown'].includes(e.key) : ['ArrowLeft','ArrowRight'].includes(e.key);
            if (acrossWeeks) {
                const nextWeek = days.map(x => monday(x.day)).filter(w => direction * (monday(entry.day) - w) > 0);
                if (nextWeek.length) {
                    const week = direction > 0 ? Math.max(...nextWeek) : Math.min(...nextWeek);
                    const desired = week + entry.day - monday(entry.day);
                    target = days.filter(x => monday(x.day) === week).sort((a,b) => Math.abs(a.day - desired) - Math.abs(b.day - desired))[0];
                }
            } else {
                target = days[days.indexOf(entry) - direction];
            }
        } else return;
        e.preventDefault();
        target?.cell.focus();
    });
    grid.addEventListener('click', e => {
        const entry = entries.get(e.target.closest('button')?.dataset.day);
        if (!entry) return;
        revealRow(entry.row);
        entry.row.scrollIntoView({ block: 'start', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' });
    });

    window.highlightsCalendar = { update, setReadingDay };
})();
