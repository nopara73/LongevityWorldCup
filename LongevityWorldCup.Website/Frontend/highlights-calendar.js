(() => {
    const calendar = document.querySelector('.event-pulse');
    const grid = calendar.querySelector('.event-pulse-grid');
    const slot = calendar.parentElement;
    const rail = document.querySelector('.event-pulse-rail');
    const wide = matchMedia('(min-width: 1280px)');
    const formatter = new Intl.DateTimeFormat(undefined, { dateStyle: 'full', calendar: 'gregory', timeZone: 'UTC' });
    let blocks = new Map();
    let entries = new Map();
    let currentCell = null;
    let tabStop = null;
    let revealRow;
    let jump = 0;

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

    function makeCell(day, active) {
        const cell = document.createElement(active ? 'button' : 'span');
        cell.className = 'event-pulse-cell';
        if (active) {
            cell.type = 'button';
            cell.tabIndex = -1;
        } else cell.setAttribute('aria-hidden', 'true');
        if (new Date(day * dayMs).getUTCDate() === 1) cell.classList.add('is-month-start');
        return cell;
    }

    function createWeek(start) {
        const week = document.createElement('div');
        week.className = 'event-pulse-week';
        for (let i = 0; i < 7; i++) week.appendChild(makeCell(start + i, false));
        return week;
    }

    function createGap(firstDay, lastDay) {
        const gap = document.createElement('span');
        gap.className = 'event-pulse-gap';
        const range = formatter.formatRange(new Date(firstDay * dayMs), new Date(lastDay * dayMs));
        gap.title = 'No highlights · ' + range;
        gap.setAttribute('role', 'img');
        gap.setAttribute('aria-label', gap.title);
        return gap;
    }

    function arrangeWeeks(occupied) {
        const next = new Map();
        const addWeek = start => next.set(start, blocks.get(start) || createWeek(start));
        let newer;
        for (const start of [...occupied].sort((a, b) => b - a)) {
            if (newer !== undefined) {
                const emptyWeeks = (newer - start) / 7 - 1;
                if (emptyWeeks >= 4) {
                    // Leave short gaps to scale. Mark a month or more of empty weeks once.
                    const key = start + '/' + newer;
                    next.set(key, blocks.get(key) || createGap(start + 7, newer - 1));
                } else {
                    for (let week = newer - 7; week > start; week -= 7) addWeek(week);
                }
            }
            addWeek(start);
            newer = start;
        }
        for (const [key, node] of blocks) if (!next.has(key)) node.remove();
        let sibling = grid.firstElementChild;
        for (const node of next.values()) {
            if (node === sibling) sibling = sibling.nextElementSibling;
            else grid.insertBefore(node, sibling);
        }
        blocks = next;
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
        jump++;
        revealRow = reveal;
        const next = new Map();
        const occupied = new Set();
        let busiest = 0;
        for (const [row] of units) {
            const key = row.dataset.day;
            if (!key) continue;
            let entry = next.get(key);
            if (!entry) {
                const day = entries.get(key)?.day ?? dayNumber(key);
                entry = { day, row, count: 0 };
                next.set(key, entry);
                occupied.add(monday(day));
            }
            busiest = Math.max(busiest, ++entry.count);
        }

        arrangeWeeks(occupied);
        for (const [key, old] of entries) {
            if (!next.has(key) && old.cell.isConnected) old.cell.replaceWith(makeCell(old.day, false));
        }
        for (const [key, entry] of next) {
            const old = entries.get(key);
            const cell = old?.cell || makeCell(entry.day, true);
            if (!old) {
                const start = monday(entry.day);
                blocks.get(start).children[entry.day - start].replaceWith(cell);
                cell.dataset.day = key;
            }
            entry.cell = cell;
            if (old?.count !== entry.count) {
                const date = formatter.format(new Date(entry.day * dayMs));
                const label = date + ' · ' + entry.count + (entry.count === 1 ? ' highlight' : ' highlights');
                cell.title = label;
                cell.setAttribute('aria-label', label);
            }
            const level = Math.round(18 + 82 * Math.log1p(entry.count) / Math.log1p(busiest)) + '%';
            if (old?.level !== level) cell.style.setProperty('--pulse-level', level);
            entry.level = level;
        }
        entries = next;
        calendar.hidden = !next.size;
        slot.hidden = !next.size;
        if (!tabStop?.isConnected) setTabStop(next.values().next().value?.cell || null);
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
    grid.addEventListener('click', async e => {
        const entry = entries.get(e.target.closest('button')?.dataset.day);
        if (!entry) return;
        const request = ++jump;
        await revealRow(entry.row, () => request === jump);
        if (request !== jump || !entry.row.isConnected) return;
        entry.row.scrollIntoView({ block: 'start', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' });
    });

    window.highlightsCalendar = { update, setReadingDay };
})();
