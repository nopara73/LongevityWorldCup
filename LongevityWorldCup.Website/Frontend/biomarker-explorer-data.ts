// Observations remain immutable. Every plot, table and export uses these same
// dated records; missing measurements are never inherited from another panel.
(function () {
    'use strict';

    const markers: readonly BiomarkerExplorerMarker[] = [
        { key: 'AlbGL', label: 'Albumin', unit: 'g/L', domain: 'Liver' },
        { key: 'AlpUL', label: 'ALP', unit: 'U/L', domain: 'Liver' },
        { key: 'AltUL', label: 'ALT', unit: 'U/L', domain: 'Liver' },
        { key: 'GgtUL', label: 'GGT', unit: 'U/L', domain: 'Liver' },
        { key: 'CreatUmolL', label: 'Creatinine', unit: 'µmol/L', domain: 'Kidney' },
        { key: 'CystatinCMgL', label: 'Cystatin C', unit: 'mg/L', domain: 'Kidney' },
        { key: 'UreaMmolL', label: 'Urea', unit: 'mmol/L', domain: 'Kidney' },
        { key: 'GluMmolL', label: 'Glucose', unit: 'mmol/L', domain: 'Metabolism' },
        { key: 'Hba1cMmolMol', label: 'HbA1c', unit: 'mmol/mol', domain: 'Metabolism' },
        { key: 'CholesterolMmolL', label: 'Total cholesterol', unit: 'mmol/L', domain: 'Metabolism' },
        { key: 'ApoA1GL', label: 'ApoA1', unit: 'g/L', domain: 'Metabolism' },
        { key: 'CrpMgL', label: 'CRP', unit: 'mg/L', domain: 'Inflammation' },
        { key: 'Wbc1000cellsuL', label: 'WBC', unit: '10³ cells/µL', domain: 'Blood counts' },
        { key: 'LymPc', label: 'Lymphocytes', unit: '%', domain: 'Blood counts' },
        { key: 'NeutrophilPc', label: 'Neutrophils', unit: '%', domain: 'Blood counts' },
        { key: 'MonocytePc', label: 'Monocytes', unit: '%', domain: 'Blood counts' },
        { key: 'Rbc10e12L', label: 'RBC', unit: '10¹²/L', domain: 'Blood counts' },
        { key: 'McvFL', label: 'MCV', unit: 'fL', domain: 'Blood counts' },
        { key: 'MchPg', label: 'MCH', unit: 'pg', domain: 'Blood counts' },
        { key: 'RdwPc', label: 'RDW', unit: '%', domain: 'Blood counts' },
        { key: 'ShbgNmolL', label: 'SHBG', unit: 'nmol/L', domain: 'Other markers' },
        { key: 'VitaminDNmolL', label: 'Vitamin D', unit: 'nmol/L', domain: 'Other markers' }
    ];
    const phenoKeys = ['AlbGL', 'CreatUmolL', 'GluMmolL', 'CrpMgL', 'Wbc1000cellsuL', 'LymPc', 'McvFL', 'RdwPc', 'AlpUL'] as const;
    const bortzKeys = ['AlbGL', 'AlpUL', 'UreaMmolL', 'CholesterolMmolL', 'CreatUmolL', 'CystatinCMgL', 'Hba1cMmolMol', 'CrpMgL', 'GgtUL', 'Rbc10e12L', 'McvFL', 'RdwPc', 'MonocytePc', 'NeutrophilPc', 'LymPc', 'AltUL', 'ShbgNmolL', 'VitaminDNmolL', 'GluMmolL', 'MchPg', 'ApoA1GL'] as const;
    const dayMilliseconds = 86400000;

    function object(value: unknown): Record<string, unknown> {
        return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : {};
    }

    function string(value: unknown, fallback = ''): string {
        return typeof value === 'string' && value.trim() ? value.trim() : fallback;
    }

    function dateDay(value: unknown): number | null {
        if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
        const timestamp = Date.parse(value + 'T00:00:00Z');
        if (!Number.isFinite(timestamp) || new Date(timestamp).toISOString().slice(0, 10) !== value) return null;
        return timestamp / dayMilliseconds;
    }

    function birthDay(value: unknown): number | null {
        const dob = object(value);
        if (![dob.Year, dob.Month, dob.Day].every(v => typeof v === 'number' && Number.isInteger(v))) return null;
        return dateDay(`${String(dob.Year).padStart(4, '0')}-${String(dob.Month).padStart(2, '0')}-${String(dob.Day).padStart(2, '0')}`);
    }

    function finite(value: unknown): value is number {
        return typeof value === 'number' && Number.isFinite(value);
    }

    // Treat only floating-point conversion noise as equal, never values which
    // merely round to the same displayed decimal place.
    function compare(a: number, b: number): number {
        return Math.abs(a - b) <= 1e-10 * Math.max(1, Math.abs(a), Math.abs(b)) ? 0 : a - b;
    }

    function pheno(values: Readonly<Record<string, number>>, age: number | null): { result: number | null; terms: readonly BiomarkerExplorerTerm[] } {
        const api = window.PhenoAge;
        if (!api || age === null || phenoKeys.some(key => !finite(values[key])) || (values.CrpMgL ?? 0) <= 0) return { result: null, terms: [] };
        const vector = [age, ...phenoKeys.map(key => key === 'CrpMgL' ? Math.log((values[key] ?? NaN) / 10) : values[key] ?? NaN)];
        const result = api.calculatePhenoAge(vector);
        if (!finite(result)) return { result: null, terms: [] };
        const terms: BiomarkerExplorerTerm[] = [];
        for (let index = 0; index < vector.length; index++) {
            const definition = api.biomarkers[index];
            const raw = vector[index];
            if (!definition || raw === undefined) continue;
            const scored = definition.cap === undefined ? raw : definition.coeff < 0 ? Math.min(raw, definition.cap) : Math.max(raw, definition.cap);
            const key = index === 0 ? 'age' : phenoKeys[index - 1] ?? '';
            const marker = markers.find(item => item.key === key);
            terms.push({ key, label: marker?.label ?? 'Chronological age term', value: scored * definition.coeff / 0.090165 - (index === 0 ? age : 0), raw: key === 'CrpMgL' ? values.CrpMgL ?? NaN : raw, scored: key === 'CrpMgL' ? values.CrpMgL ?? NaN : scored, unit: marker?.unit ?? 'years', transformed: key === 'CrpMgL' });
        }
        return { result, terms };
    }

    function bortz(values: Readonly<Record<string, number>>, age: number | null): { result: number | null; terms: readonly BiomarkerExplorerTerm[] } {
        const api = window.BortzAge;
        if (!api || age === null || bortzKeys.some(key => !finite(values[key])) || !finite(values.Wbc1000cellsuL)) return { result: null, terms: [] };
        const vector = [age, ...bortzKeys.map(key => key === 'MonocytePc' || key === 'NeutrophilPc' ? (values.Wbc1000cellsuL ?? NaN) * (values[key] ?? NaN) / 100 : values[key] ?? NaN)];
        const result = api.calculateBortzAge(age, vector);
        if (!finite(result)) return { result: null, terms: [] };
        const terms: BiomarkerExplorerTerm[] = [];
        for (let index = 0; index < vector.length; index++) {
            const definition = api.features[index];
            const raw = vector[index];
            if (!definition || raw === undefined) continue;
            const key = index === 0 ? 'age' : bortzKeys[index - 1] ?? '';
            const marker = markers.find(item => item.key === key);
            const derivedCount = key === 'MonocytePc' || key === 'NeutrophilPc';
            const scored = definition.cap === undefined ? raw : definition.capMode === 'floor' ? Math.max(raw, definition.cap) : Math.min(raw, definition.cap);
            terms.push({ key, label: derivedCount ? `${marker?.label ?? ''} count (derived from WBC)` : marker?.label ?? 'Chronological age term', value: api.calculateFeatureContribution(raw, definition) * 10, raw, scored, unit: derivedCount ? '10⁹/L' : marker?.unit ?? 'years', transformed: definition.isLog });
        }
        return { result, terms };
    }

    function normalize(value: unknown): BiomarkerExplorerAthlete {
        const raw = object(value);
        const name = string(raw.DisplayName, string(raw.Name, 'Athlete'));
        const id = string(raw.AthleteSlug, string(raw.Name, name)).toLocaleLowerCase('en').replace(/[\s-]+/g, '_');
        const division = string(raw.Division);
        const dob = birthDay(raw.DateOfBirth);
        const source = Array.isArray(raw.Biomarkers) ? raw.Biomarkers : [];
        let invalidDates = 0;
        const records: BiomarkerExplorerRecord[] = [];
        source.forEach((entry: unknown, index: number) => {
            const panel = object(entry);
            const day = dateDay(panel.Date);
            if (day === null) { invalidDates++; return; }
            const values: Record<string, number> = {};
            for (const marker of markers) if (finite(panel[marker.key])) values[marker.key] = panel[marker.key] as number;
            if (!Object.keys(values).length) return;
            const age = dob !== null && day >= dob ? (day - dob) / 365.2425 : null;
            const p = pheno(values, age);
            const b = bortz(values, age);
            records.push({ id: `${id}:${String(panel.Date)}:${index}`, athleteId: id, athleteName: name, division, date: String(panel.Date), day, age, values: Object.freeze(values), pheno: p.result, bortz: b.result, phenoTerms: p.terms, bortzTerms: b.terms });
        });
        records.sort((a, b) => a.day - b.day || a.id.localeCompare(b.id));
        return { id, name, division, records, invalidDates };
    }

    function latestCohort(athletes: readonly BiomarkerExplorerAthlete[], excludedId: string): BiomarkerExplorerCohort {
        const records: BiomarkerExplorerRecord[] = [];
        let ambiguous = 0;
        let withoutRecords = 0;
        const seen = new Set<string>();
        for (const athlete of athletes) {
            if (athlete.id === excludedId || seen.has(athlete.id)) continue;
            seen.add(athlete.id);
            const latest = athlete.records.at(-1);
            if (!latest) { withoutRecords++; continue; }
            if (athlete.records.filter(record => record.day === latest.day).length !== 1) { ambiguous++; continue; }
            records.push(latest);
        }
        return { records, ambiguous, withoutRecords };
    }

    function ecdf(input: readonly number[]): readonly { x: number; y: number }[] {
        const values = input.filter(finite).slice().sort((a, b) => a - b);
        const result: { x: number; y: number }[] = [];
        let index = 0;
        while (index < values.length) {
            const x = values[index];
            if (x === undefined) break;
            let end = index + 1;
            while (end < values.length && compare(values[end] ?? NaN, x) === 0) end++;
            result.push({ x, y: index / values.length * 100 }, { x, y: end / values.length * 100 });
            index = end;
        }
        return result;
    }

    function counts(input: readonly number[], value: number): { lower: number; equal: number; higher: number; atOrBelow: number } {
        const values = input.filter(finite);
        let lower = 0;
        let equal = 0;
        for (const observed of values) { const delta = compare(observed, value); if (delta < 0) lower++; else if (delta === 0) equal++; }
        return { lower, equal, higher: values.length - lower - equal, atOrBelow: values.length ? (lower + equal) / values.length * 100 : 0 };
    }

    function standardization(input: readonly number[]): { mean: number; sd: number; n: number } | null {
        const values = input.filter(finite);
        if (values.length < 5) return null;
        const mean = values.reduce((sum, value) => sum + value, 0) / values.length;
        const sd = Math.sqrt(values.reduce((sum, value) => sum + (value - mean) ** 2, 0) / (values.length - 1));
        return sd > 1e-10 ? { mean, sd, n: values.length } : null;
    }

    function ranks(values: readonly number[]): number[] {
        const sorted = values.map((value, index) => ({ value, index })).sort((a, b) => a.value - b.value);
        const result = new Array<number>(values.length).fill(0);
        let start = 0;
        while (start < sorted.length) {
            let end = start + 1;
            while (end < sorted.length && compare(sorted[start]?.value ?? NaN, sorted[end]?.value ?? NaN) === 0) end++;
            for (let index = start; index < end; index++) { const item = sorted[index]; if (item) result[item.index] = (start + 1 + end) / 2; }
            start = end;
        }
        return result;
    }

    function rankAssociation(pairs: readonly { x: number; y: number }[]): number | null {
        const valid = pairs.filter(pair => finite(pair.x) && finite(pair.y));
        if (valid.length < 3) return null;
        const xRanks = ranks(valid.map(pair => pair.x));
        const yRanks = ranks(valid.map(pair => pair.y));
        const mean = (valid.length + 1) / 2;
        let covariance = 0;
        let xVariance = 0;
        let yVariance = 0;
        for (let index = 0; index < valid.length; index++) {
            const x = (xRanks[index] ?? mean) - mean;
            const y = (yRanks[index] ?? mean) - mean;
            covariance += x * y; xVariance += x * x; yVariance += y * y;
        }
        return xVariance > 0 && yVariance > 0 ? Math.max(-1, Math.min(1, covariance / Math.sqrt(xVariance * yVariance))) : null;
    }

    function samePanelPairs(records: readonly BiomarkerExplorerRecord[], xKey: string, yKey: string): readonly { record: BiomarkerExplorerRecord; x: number; y: number }[] {
        return records.flatMap(record => {
            const x = record.values[xKey]; const y = record.values[yKey];
            return finite(x) && finite(y) ? [{ record, x, y }] : [];
        });
    }

    function series(records: readonly BiomarkerExplorerRecord[], value: (record: BiomarkerExplorerRecord) => number | null): readonly { x: number; y: number | null; recordId: string | null }[] {
        const points: { x: number; y: number | null; recordId: string | null }[] = [];
        let previous: BiomarkerExplorerRecord | undefined;
        for (const record of records) {
            // Explicitly interrupt both long intervals and same-day repeated
            // panels. All observations remain at their exact calendar date.
            if (previous && (record.day - previous.day > 180 || record.day === previous.day)) points.push({ x: (previous.day + record.day) / 2, y: null, recordId: null });
            points.push({ x: record.day, y: value(record), recordId: record.id });
            previous = record;
        }
        return points;
    }

    function clockChange(a: BiomarkerExplorerRecord, b: BiomarkerExplorerRecord, clock: 'pheno' | 'bortz'): ReturnType<BiomarkerExplorerDataApi['clockChange']> {
        if (a[clock] === null || b[clock] === null || a.age === null || b.age === null) return null;
        const before = clock === 'pheno' ? a.phenoTerms : a.bortzTerms;
        const after = clock === 'pheno' ? b.phenoTerms : b.bortzTerms;
        const rows = before.flatMap(term => {
            const next = after.find(item => item.key === term.key);
            return next ? [{ key: term.key, label: term.label, change: next.value - term.value, a: term.raw, b: next.raw, unit: term.unit, capped: term.raw !== term.scored || next.raw !== next.scored, transformed: term.transformed }] : [];
        });
        const delta = (b[clock] - b.age) - (a[clock] - a.age);
        const remainder = delta - rows.reduce((sum, row) => sum + row.change, 0);
        if (Math.abs(remainder) > 1e-6) rows.push({ key: 'remainder', label: 'Model floor / numerical adjustment', change: remainder, a: 0, b: 0, unit: '', capped: false, transformed: false });
        return rows;
    }

    window.LwcBiomarkerData = { markers, dateDay, normalize, latestCohort, ecdf, counts, standardization, rankAssociation, samePanelPairs, series, clockChange };
})();
