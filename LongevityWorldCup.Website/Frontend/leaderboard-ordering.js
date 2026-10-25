// Presentation ordering is independent of the competition ranking arrays.
(() => {
    const byId = id => document.getElementById(id);
    let catalog = { fields: [], asOf: new Date().toISOString().slice(0, 10) };
    try { catalog = JSON.parse(byId('leaderboardOrderCatalog')?.textContent || '{}'); } catch { /* Dialog-only embeds do not have sorting controls. */ }
    // Wire row: key, label, unit, source key, decimals, direction, kind. Expand once;
    // sorting and controls use readable field objects throughout.
    const catalogFields = catalog.groups ? catalog.groups.flatMap(group => group.fields.map(
        ([key, label, unit, sourceKey, decimals, direction, kind]) => ({ key, label, group: group.label,
            unit: unit ?? '', sourceKey, decimals: decimals ?? 1, direction: direction ?? 'asc', kind: kind ?? 'number' })
    )) : catalog.fields || [];
    const fields = new Map(catalogFields.map(field => [field.key, { unit: '', decimals: 1, direction: 'asc', kind: 'number', ...field }]));
    const toggle = byId('leaderboardOrderToggle');
    const reset = byId('leaderboardOrderReset');
    const panel = byId('leaderboardOrderPanel');
    const primary = byId('leaderboardOrderPrimary');
    const secondary = byId('leaderboardOrderSecondary');
    const primaryDirection = byId('leaderboardOrderDirection');
    const secondaryDirection = byId('leaderboardThenDirection');
    const swap = byId('leaderboardOrderSwap');
    const primaryClear = byId('leaderboardOrderPrimaryClear');
    const secondaryClear = byId('leaderboardOrderSecondaryClear');
    const defaults = () => ({ sort: 'rank', direction: 'asc', then: null, thenDirection: 'asc' });
    const direction = (value, field) => value === 'asc' || value === 'desc' ? value : field.direction;
    function normalize(value) {
        const field = fields.get(value.sort);
        if (!field || field.key === 'rank') return defaults();
        const then = fields.get(value.then);
        const distinct = then && then.key !== 'rank' && then.key !== field.key ? then : null;
        return { sort: field.key, direction: direction(value.direction, field), then: distinct?.key || null,
            thenDirection: distinct ? direction(value.thenDirection, distinct) : 'asc' };
    }
    function readUrl() {
        const params = new URLSearchParams(location.search);
        const first = name => [...params].find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1];
        return normalize({ sort: first('sort'), direction: first('dir'), then: first('then'), thenDirection: first('thenDir') });
    }
    let current = readUrl();
    let onChange = () => {};
    function writeUrl(url, state = current) {
        for (const key of [...url.searchParams.keys()]) if (['sort', 'dir', 'then', 'thendir'].includes(key.toLowerCase())) url.searchParams.delete(key);
        if (state.sort !== 'rank') {
            url.searchParams.set('sort', state.sort);
            if (state.direction !== fields.get(state.sort)?.direction) url.searchParams.set('dir', state.direction);
            if (state.then) {
                url.searchParams.set('then', state.then);
                if (state.thenDirection !== fields.get(state.then)?.direction) url.searchParams.set('thenDir', state.thenDirection);
            }
        }
        return url;
    }
    function explicitDate(value) {
        if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}(?:$|T)/.test(value)) return null;
        const date = value.slice(0, 10);
        const parsed = new Date(date + 'T00:00:00Z');
        return Number.isFinite(parsed.getTime()) && parsed.getUTCFullYear() > 0 && parsed.toISOString().slice(0, 10) === date
            && (value.length === 10 || Number.isFinite(Date.parse(value))) ? date : null;
    }
    const number = value => typeof value === 'number' && Number.isFinite(value) ? value : null;
    function values(source, athlete) {
        const panels = Array.isArray(source.Biomarkers) ? source.Biomarkers.filter(panel => panel && typeof panel === 'object' && !Array.isArray(panel)) : [];
        const dated = panels.map((panel, index) => ({ panel, index, date: explicitDate(panel.Date) }))
            .filter(item => item.date).sort((a, b) => a.date < b.date ? -1 : a.date > b.date ? 1 : a.index - b.index);
        const dob = source.DateOfBirth;
        const age = dob ? (Date.parse(catalog.asOf + 'T00:00:00Z') - Date.UTC(dob.Year, dob.Month - 1, dob.Day)) / 86400000 / 365.2425 : null;
        const hasPheno = athlete.submissionCount > 0;
        const result = {
            age: number(age), bortz: number(athlete.lowestBortzAge), pheno: hasPheno ? number(athlete.lowestPhenoAge) : null,
            crowd: athlete.crowdCount > 0 ? number(athlete.crowdAge) : null,
            bortzReduction: number(athlete.bortzAgeReduction), phenoReduction: hasPheno ? number(athlete.ageReduction) : null,
            crowdReduction: athlete.crowdCount > 0 && number(age) !== null && number(athlete.crowdAge) !== null ? number(athlete.crowdAge - age) : null,
            pace: athlete.chronoAtLowestBortzAge > 0 && number(athlete.lowestBortzAge) !== null ? number(athlete.lowestBortzAge / athlete.chronoAtLowestBortzAge) : null,
            phenoPace: hasPheno && athlete.chronoAtLowestPhenoAge > 0 ? number(athlete.lowestPhenoAge / athlete.chronoAtLowestPhenoAge) : null,
            phenoImprovement: number(athlete.phenoAgeImprovement), bortzImprovement: number(athlete.bortzAgeImprovement),
            phenoPercent: hasPheno ? number(athlete.ageReductionPercent) : null,
            bortzPercent: number(athlete.bortzAgeReductionPercent),
            firstTest: dated[0]?.date || null, lastTest: dated.at(-1)?.date || null,
            tests: panels.length, crowdCount: number(athlete.crowdCount), name: athlete.displayName,
            flag: athlete.canonicalFlag, track: Number.isFinite(athlete.bortzAgeReduction) ? 'Pro' : 'Amateur',
            division: athlete.division, badges: Array.isArray(source.Badges) ? source.Badges.length : 0
        };
        for (const { key, sourceKey } of fields.values()) {
            if (!sourceKey) continue;
            result[key] = null;
            for (const item of dated) if (number(item.panel[sourceKey]) !== null) result[key] = { value: item.panel[sourceKey], date: item.date };
        }
        return result;
    }
    const rawValue = (entry, key) => key === 'rank' ? entry.rank : key === 'metric' ? entry.metricValue : entry.athlete.orderValues?.[key];
    const unwrap = value => value && typeof value === 'object' ? value.value : value;
    const missing = value => value == null || (typeof value === 'number' && !Number.isFinite(value)) || (typeof value === 'string' && !value.trim());
    // Compare invariant, non-contextual lowercase keys as UTF-16, in the same order as C#.
    const textKey = value => Array.from(String(value).trim()).map(char => {
        const lower = char.toLowerCase();
        return lower.length === char.length ? lower : char;
    }).join('');
    function compare(a, b, dir) {
        a = unwrap(a); b = unwrap(b);
        if (missing(a) || missing(b)) return Number(missing(a)) - Number(missing(b));
        if (typeof a !== 'number' || typeof b !== 'number') { a = textKey(a); b = textKey(b); }
        const result = a < b ? -1 : a > b ? 1 : 0;
        return dir === 'desc' ? -result : result;
    }
    function sort(entries, state = current) {
        if (state.sort === 'rank') return entries;
        return [...entries].sort((a, b) => compare(rawValue(a, state.sort), rawValue(b, state.sort), state.direction)
            || (state.then ? compare(rawValue(a, state.then), rawValue(b, state.then), state.thenDirection) : 0)
            || a.rank - b.rank || compare(a.athlete.athleteSlug, b.athlete.athleteSlug, 'asc'));
    }
    const columns = state => (state.sort === 'rank' ? [] : [state.sort, state.then]).filter(key => key && !['rank', 'name', 'metric'].includes(key)).map(key => fields.get(key));
    const labelFor = (key, metricLabel) => key === 'metric' ? metricLabel : fields.get(key)?.label || key;
    const accessibleLabelFor = (key, metricLabel) => {
        const unit = fields.get(key)?.unit;
        return labelFor(key, metricLabel) + (unit ? ` (${unit})` : '');
    };
    function decorateHeader(header, state, metricLabel) {
        const key = header.dataset.orderKey;
        const level = state.sort !== 'rank' && state.sort === key ? 1 : state.then === key ? 2 : 0;
        const dir = level === 2 ? state.thenDirection : state.direction;
        if (header.dataset.orderLevel !== String(level || '')) header.dataset.orderLevel = level || '';
        if (header.dataset.orderDirection !== (level ? dir : '')) header.dataset.orderDirection = level ? dir : '';
        const ariaSort = level === 1 ? (dir === 'asc' ? 'ascending' : 'descending') : null;
        if (ariaSort && header.getAttribute('aria-sort') !== ariaSort) header.setAttribute('aria-sort', ariaSort);
        else if (!ariaSort && header.hasAttribute('aria-sort')) header.removeAttribute('aria-sort');
        const indicator = header.querySelector('.order-indicator');
        if (!!level !== indicator.hasChildNodes()) indicator.innerHTML = level ? '<svg viewBox="0 0 20 20"><path d="M10 4v12m-4-4 4 4 4-4" /></svg>' : '';
        const label = accessibleLabelFor(key, metricLabel);
        const description = key === 'rank' ? 'Restore official ranking' : level
            ? `${label}, ${level === 1 ? 'order by' : 'then by'}, ${dir === 'asc' ? 'ascending' : 'descending'}; reverse order` : `Order by ${label}`;
        const button = header.querySelector('button');
        if (button.getAttribute('aria-label') !== description) button.setAttribute('aria-label', description);
    }
    function clearExitFrame() {
        if (!panel) return;
        delete panel.dataset.orderExit;
        for (const part of ['left', 'top', 'width', 'height']) panel.style.removeProperty(`--order-exit-${part}`);
    }
    function close(restoreFocus = false) {
        if (!panel || panel.hidden) return;
        const inFlow = getComputedStyle(panel).position === 'static';
        if (inFlow) {
            // Keep the exit in its existing frame, but release the strip's flow
            // space in this input task instead of at a delayed display transition.
            for (const [part, value] of Object.entries({ left: panel.offsetLeft, top: panel.offsetTop,
                width: panel.offsetWidth, height: panel.offsetHeight })) panel.style.setProperty(`--order-exit-${part}`, `${value}px`);
            panel.dataset.orderExit = '';
        }
        panel.hidden = true;
        toggle.setAttribute('aria-expanded', 'false');
        if (restoreFocus || panel.contains(document.activeElement)) {
            toggle.focus({ preventScroll: true });
            if (inFlow) {
                // Opening a tall strip can scroll its trigger out of view.
                // Escape must return visible focus, not only DOM focus.
                const top = (parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--site-sticky-height')) || 0) + 12;
                const bounds = toggle.getBoundingClientRect();
                const shift = bounds.top < top ? bounds.top - top : Math.max(0, bounds.bottom - document.documentElement.clientHeight + 12);
                if (shift) window.scrollBy({ top: shift, behavior: 'instant' });
            }
        }
    }
    function revealStrip() {
        if (getComputedStyle(panel).position !== 'static') return;
        const viewport = document.documentElement.clientHeight;
        const stickyHeight = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--site-sticky-height')) || 0;
        const top = stickyHeight + 12;
        const bounds = panel.getBoundingClientRect();
        // A tall strip cannot fit in a short viewport. Keep its first field in
        // view rather than aligning the bottom and scrolling focus off screen.
        const field = primary.getBoundingClientRect();
        const overflow = bounds.bottom - (viewport - 12);
        const shift = Math.min(Math.max(0, overflow), field.top - top);
        if (shift) window.scrollBy({ top: shift, behavior: 'instant' });
    }
    function sync(state = current, byFlag = false, metricLabel = 'Age reduction') {
        if (!toggle || !fields.size) return;
        current = normalize(state);
        toggle.disabled = false;
        toggle.closest('.leaderboard-order-tools').hidden = byFlag;
        if (byFlag) close();
        const active = !byFlag && current.sort !== 'rank';
        toggle.classList.toggle('is-active', active);
        reset.hidden = !active;
        reset.disabled = false;
        if (primary.value !== current.sort) primary.value = current.sort;
        if (secondary.value !== (current.then || '')) secondary.value = current.then || '';
        secondary.disabled = !active;
        swap.disabled = !active || !current.then;
        primaryClear.disabled = !active;
        secondaryClear.disabled = !active || !current.then;
        for (const [select, other] of [[primary, null], [secondary, current.sort]]) {
            for (const option of select.options) {
                const disabled = !!other && option.value === other;
                if (option.disabled !== disabled) option.disabled = disabled;
            }
            const metric = select.querySelector('option[value="metric"]');
            if (metric && metric.textContent !== metricLabel) metric.textContent = metricLabel;
            // Keep the native picker, keyboard interaction and accessible
            // value; let its visible selected text wrap at enlarged sizes.
            let label = select.parentElement.querySelector('.order-selected-value');
            if (!label) {
                label = document.createElement('span');
                label.className = 'order-selected-value';
                label.setAttribute('aria-hidden', 'true');
                select.after(label);
            }
            label.textContent = select.selectedOptions[0]?.textContent || '';
        }
        for (const [button, key, dir, enabled] of [[primaryDirection, current.sort, current.direction, active], [secondaryDirection, current.then, current.thenDirection, active && !!current.then]]) {
            button.disabled = !enabled;
            if (button.dataset.direction !== dir) button.dataset.direction = dir;
            const field = fields.get(key);
            const ascending = field?.kind === 'text' ? 'A to Z' : field?.kind === 'date' ? 'oldest first' : 'lowest first';
            const descending = field?.kind === 'text' ? 'Z to A' : field?.kind === 'date' ? 'newest first' : 'highest first';
            const description = enabled ? `${accessibleLabelFor(key, metricLabel)}: ${dir === 'asc' ? ascending : descending}; switch to ${dir === 'asc' ? descending : ascending}`
                : key === 'rank' ? 'Official ranking' : 'Select a secondary field to set its direction';
            if (button.getAttribute('aria-label') !== description) button.setAttribute('aria-label', description);
            if (button.title !== description) button.title = description;
        }
        const leaderboard = document.querySelector('.leaderboard');
        leaderboard?.classList.toggle('leaderboard--ordered', active);
        const display = byFlag ? defaults() : current;
        const selectedColumns = columns(display);
        if (leaderboard && leaderboard.dataset.orderColumns !== String(selectedColumns.length)) leaderboard.dataset.orderColumns = selectedColumns.length;
        const keys = selectedColumns.map(field => field.key);
        document.querySelectorAll('th.leaderboard-order-column, td.leaderboard-order-value').forEach(cell => { if (!keys.includes(cell.dataset.orderKey)) cell.remove(); });
        const metricHeader = document.querySelector('th[data-order-key="metric"]');
        let previous = metricHeader;
        for (const field of selectedColumns) {
            let header = document.querySelector(`th[data-order-key="${field.key}"]`);
            if (!header) {
                header = document.createElement('th');
                header.scope = 'col'; header.dataset.orderKey = field.key; header.className = 'leaderboard-order-column';
                const button = document.createElement('button'); button.type = 'button'; button.className = 'leaderboard-column-order';
                const heading = document.createElement('span'); heading.className = 'order-heading';
                const label = document.createElement('span'); label.className = 'order-heading-label'; label.textContent = field.label; heading.append(label);
                if (field.unit) { const unit = document.createElement('small'); unit.textContent = field.unit; heading.append(unit); }
                button.append(heading);
                const indicator = document.createElement('span'); indicator.className = 'order-indicator'; indicator.setAttribute('aria-hidden', 'true'); button.append(indicator);
                header.append(button);
            }
            if (previous?.nextElementSibling !== header) previous?.after(header);
            previous = header;
        }
        document.querySelectorAll('th[data-order-key]').forEach(header => decorateHeader(header, display, metricLabel));
    }
    function renderValues(entries, state = current, rowsByName) {
        const selected = columns(state);
        if (!selected.length) return;
        rowsByName ||= new Map(Array.from(document.querySelectorAll('.leaderboard tbody tr[data-athlete-name]'), row => [row.dataset.athleteName, row]));
        for (const entry of entries) {
            const row = rowsByName.get(entry.athlete.name);
            if (!row) continue;
            let previous = row.querySelector('.age-reduction-td');
            for (const field of selected) {
                let cell = row.querySelector(`td[data-order-key="${field.key}"]`);
                if (!cell) { cell = document.createElement('td'); cell.className = 'leaderboard-order-value'; cell.dataset.orderKey = field.key; }
                const label = field.label + (field.unit ? ` (${field.unit})` : '');
                if (cell.dataset.label !== label) cell.dataset.label = label;
                const item = rawValue(entry, field.key);
                const value = unwrap(item);
                const text = missing(value) ? '—' : typeof value === 'number' ? value.toFixed(field.decimals) : String(value);
                if (cell.textContent !== text || !cell.childNodes.length) {
                    cell.replaceChildren();
                    if (missing(value)) { const dash = document.createElement('span'); dash.className = 'order-missing'; dash.setAttribute('role', 'img'); dash.setAttribute('aria-label', 'Not available'); dash.textContent = '—'; cell.append(dash); }
                    else cell.textContent = text;
                }
                if (item?.date) {
                    const title = `${field.label}: ${text} ${field.unit}. Tested ${item.date}`;
                    if (cell.title !== title) { cell.title = title; cell.setAttribute('aria-label', title); }
                }
                else { cell.removeAttribute('title'); cell.removeAttribute('aria-label'); }
                if (previous?.nextElementSibling !== cell) previous?.after(cell);
                previous = cell;
            }
        }
    }
    function commit(next) {
        current = normalize(next);
        const url = writeUrl(new URL(location.href));
        if (url.href !== location.href) history.pushState(history.state || {}, '', url);
        onChange();
        const status = byId('leaderboardOrderStatus');
        if (status) status.textContent = current.sort === 'rank' ? 'Official ranking restored.' : `Ordered by ${accessibleLabelFor(current.sort, byId('leaderboardMetricHeader')?.textContent)}, ${current.direction === 'asc' ? 'ascending' : 'descending'}${current.then ? `, then by ${accessibleLabelFor(current.then, byId('leaderboardMetricHeader')?.textContent)}, ${current.thenDirection === 'asc' ? 'ascending' : 'descending'}` : ''}.`;
    }
    if (toggle && panel && fields.size) {
        // One shared catalog avoids shipping the same choices three times in every page.
        for (const [select, selected, isSecondary] of [[primary, current.sort, false], [secondary, current.then || '', true]]) {
            select.replaceChildren();
            if (isSecondary) { const none = document.createElement('option'); none.value = ''; none.textContent = 'None'; select.append(none); }
            let group;
            for (const field of fields.values()) {
                if (isSecondary && field.key === 'rank') continue;
                if (group?.label !== field.group) { group = document.createElement('optgroup'); group.label = field.group; select.append(group); }
                const option = document.createElement('option'); option.value = field.key;
                option.textContent = labelFor(field.key, byId('leaderboardMetricHeader')?.textContent || 'Age reduction') + (field.unit === '%' ? ' (%)' : '');
                group.append(option);
            }
            select.value = selected;
        }
        toggle.addEventListener('click', () => {
            if (!panel.hidden) close();
            else {
                clearExitFrame();
                panel.hidden = false;
                toggle.setAttribute('aria-expanded', 'true');
                revealStrip();
                primary.focus({ preventScroll: true });
            }
        });
        reset.addEventListener('click', () => {
            toggle.focus({ preventScroll: true });
            commit(defaults());
            close();
        });
        primary.addEventListener('change', () => commit({ ...current, sort: primary.value, direction: fields.get(primary.value)?.direction }));
        secondary.addEventListener('change', () => commit({ ...current, then: secondary.value, thenDirection: fields.get(secondary.value)?.direction }));
        primaryDirection.addEventListener('click', () => commit({ ...current, direction: current.direction === 'asc' ? 'desc' : 'asc' }));
        secondaryDirection.addEventListener('click', () => commit({ ...current, thenDirection: current.thenDirection === 'asc' ? 'desc' : 'asc' }));
        swap.addEventListener('click', () => {
            commit({ sort: current.then, direction: current.thenDirection, then: current.sort, thenDirection: current.direction });
        });
        primaryClear.addEventListener('click', () => {
            primary.focus({ preventScroll: true });
            commit(defaults());
        });
        secondaryClear.addEventListener('click', () => {
            secondary.focus({ preventScroll: true });
            commit({ ...current, then: null });
        });
        document.addEventListener('click', event => {
            const header = event.target.closest('.leaderboard-column-order')?.closest('th[data-order-key]');
            if (!header) return;
            const key = header.dataset.orderKey;
            if (key === 'rank') commit(defaults());
            else if (key === current.sort) commit({ ...current, direction: current.direction === 'asc' ? 'desc' : 'asc' });
            else if (key === current.then) commit({ ...current, thenDirection: current.thenDirection === 'asc' ? 'desc' : 'asc' });
            else commit({ ...current, sort: key, direction: fields.get(key)?.direction });
        });
        // Dismissing the picker must not activate a profile, header or link underneath.
        // Intercept pointerdown before focusout can close it, and consume that gesture's
        // click as well. A drag/scroll has no click and remains ordinary page scrolling.
        let dismissPointer = null;
        document.addEventListener('pointerdown', event => {
            dismissPointer = null;
            if (panel.hidden || toggle.parentElement.contains(event.target)) return;
            dismissPointer = event.pointerId;
            event.preventDefault();
            event.stopImmediatePropagation();
            close(true);
        }, true);
        document.addEventListener('click', event => {
            if (dismissPointer !== null && (event.pointerId === undefined || event.pointerId === dismissPointer)) {
                dismissPointer = null;
                event.preventDefault();
                event.stopImmediatePropagation();
                return;
            }
            if (panel.hidden || toggle.parentElement.contains(event.target)) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            close(true);
        }, true);
        document.addEventListener('pointercancel', () => { dismissPointer = null; }, true);
        document.addEventListener('keydown', event => { if (event.key === 'Escape' && !panel.hidden) { event.preventDefault(); event.stopImmediatePropagation(); close(true); } }, true);
        toggle.parentElement.addEventListener('focusout', event => {
            // Native select menus can temporarily leave activeElement at body.
            // Only a real destination outside the composer is a focus departure.
            if (event.relatedTarget instanceof Node && !toggle.parentElement.contains(event.relatedTarget)) close();
        });
    }
    window.LwcLeaderboardOrder = { bind: callback => { onChange = callback; }, getState: () => ({ ...current }), restore: () => { current = readUrl(); }, writeUrl, values, sort, sync, renderValues };
})();
