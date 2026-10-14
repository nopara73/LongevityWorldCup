import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createContext, runInContext } from 'node:vm';
import { test } from 'node:test';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const context = createContext({ window: {}, console });
for (const filename of ['pheno-age.ts', 'bortz-age.ts', 'biomarker-explorer-data.ts']) {
    const source = readFileSync(join(root, 'Frontend', filename), 'utf8').replace(/export \{\};\s*$/, '');
    runInContext(`(function () {\n${stripTypeScriptTypes(source, { mode: 'strip' })}\n})();`, context, { filename });
}
const data = context.window.LwcBiomarkerData;
const plain = value => JSON.parse(JSON.stringify(value));
const close = (actual, expected, tolerance = 1e-8) => assert.ok(Math.abs(actual - expected) < tolerance, `${actual} != ${expected}`);
// Freeze representative public panels so a legitimate future submission does
// not silently change a regression scenario or invalidate expected counts.
const publicPanels = JSON.parse(readFileSync(join(root, '..', 'LongevityWorldCup.Tests', 'Fixtures', 'BiomarkerExplorerPanels.json'), 'utf8'));
const fixture = slug => ({ Name: slug, DateOfBirth: { Year: 1980, Month: 1, Day: 1 }, Biomarkers: structuredClone(publicPanels[slug]) });
const panel = { Date: '2026-01-01', AlbGL: 45, CreatUmolL: 80, GluMmolL: 5, CrpMgL: 1, Wbc1000cellsuL: 5, LymPc: 30, McvFL: 90, RdwPc: 13, AlpUL: 70 };
const athlete = (records, name = 'Test athlete') => ({ Name: name, DateOfBirth: { Year: 1980, Month: 1, Day: 1 }, Biomarkers: records });
const deepFreeze = value => { if (value && typeof value === 'object') { Object.values(value).forEach(deepFreeze); Object.freeze(value); } return value; };

test('date-only records use UTC calendar days and reject impossible dates', () => {
    assert.equal(data.dateDay('2025-02-30'), null);
    assert.equal(data.dateDay('2025-03-01T00:00:00Z'), null);
    assert.equal(data.dateDay(''), null);
    assert.equal(data.dateDay('2025-03-18') - data.dateDay('2025-02-04'), 42);
    assert.equal(data.dateDay('2025-05-27') - data.dateDay('2025-03-18'), 70);
    assert.equal(data.dateDay('2024-03-01') - data.dateDay('2024-02-28'), 2);
});

test('real partial historical panels remain observed and are never completed by inheritance', () => {
    const source = deepFreeze(fixture('nopara73'));
    const before = JSON.stringify(source);
    const normalized = data.normalize(source);
    assert.equal(JSON.stringify(source), before);
    const old = normalized.records.find(record => record.date === '2005-09-15');
    assert.equal(old.values.AlbGL, undefined);
    assert.equal(old.values.CrpMgL, 21.2);
    assert.equal(old.pheno, null);
    assert.equal(old.bortz, null);
    const withoutRdw = normalized.records.find(record => record.date === '2024-05-08');
    assert.equal(withoutRdw.values.RdwPc, undefined);
    assert.equal(withoutRdw.pheno, null);
});

test('Benjamin Garden has two pheno observations but only one eligible bortz observation', () => {
    const records = data.normalize(fixture('benjamin_garden')).records;
    assert.equal(records.length, 2);
    assert.ok(Number.isFinite(records[0].pheno));
    assert.ok(Number.isFinite(records[1].pheno));
    assert.equal(records[0].bortz, null);
    assert.ok(Number.isFinite(records[1].bortz));
});

test('raw albumin above the scoring cap remains displayed while clock results honor the cap', () => {
    const capped = data.normalize(athlete([{ ...panel, AlbGL: 54 }])).records[0];
    const raw = data.normalize(athlete([{ ...panel, AlbGL: 66 }])).records[0];
    assert.equal(raw.values.AlbGL, 66);
    close(raw.pheno, capped.pheno);
    assert.equal(raw.phenoTerms.find(term => term.key === 'AlbGL').scored, 54);
    const full = fixture('benjamin_garden').Biomarkers.at(-1);
    const b54 = data.normalize(athlete([{ ...full, AlbGL: 54 }])).records[0];
    const b66 = data.normalize(athlete([{ ...full, AlbGL: 66 }])).records[0];
    assert.equal(b66.values.AlbGL, 66);
    close(b54.bortz, b66.bortz);
});

test('numeric strings, censoring text and zero CRP do not produce an invented clock', () => {
    for (const value of ['<0.2', '0.2', null, NaN, Infinity]) {
        const record = data.normalize(athlete([{ ...panel, CrpMgL: value }])).records[0];
        assert.equal(record.pheno, null);
        assert.equal(record.values.CrpMgL, undefined);
    }
    const zero = data.normalize(athlete([{ ...panel, CrpMgL: 0 }])).records[0];
    assert.equal(zero.values.CrpMgL, 0);
    assert.equal(zero.pheno, null);
});

test('latest reference panels exclude self, duplicates and ambiguous same-date panels without backfilling', () => {
    const own = data.normalize(athlete([panel], 'Self'));
    const missingLatest = data.normalize(athlete([panel, { Date: '2026-02-01', CrpMgL: 2 }], 'Partial latest'));
    const duplicate = data.normalize(athlete([{ ...panel }, { ...panel, AlbGL: 46 }], 'Ambiguous'));
    assert.equal(duplicate.records.length, 2);
    assert.notEqual(duplicate.records[0].id, duplicate.records[1].id);
    const cohort = data.latestCohort([own, missingLatest, duplicate, missingLatest], own.id);
    assert.equal(cohort.records.length, 1);
    assert.equal(cohort.ambiguous, 1);
    assert.equal(cohort.records[0].date, '2026-02-01');
    assert.equal(cohort.records[0].values.AlbGL, undefined);
});

test('pairing requires the same record, even for separate panels sharing a calendar date', () => {
    const records = data.normalize(athlete([
        { Date: '2026-01-01', AlbGL: 45 },
        { Date: '2026-01-01', RdwPc: 13 },
        { Date: '2026-01-02', AlbGL: 46, RdwPc: 12 }
    ])).records;
    const pairs = data.samePanelPairs(records, 'AlbGL', 'RdwPc');
    assert.equal(pairs.length, 1);
    assert.equal(pairs[0].record.date, '2026-01-02');
    assert.equal(data.samePanelPairs(records.slice(0, 2), 'AlbGL', 'RdwPc').length, 0);
});

test('timeline breaks missing, long-gap and duplicate-date observations without dropping points', () => {
    const records = data.normalize(athlete([
        { Date: '2024-01-01', AlbGL: 45 },
        { Date: '2024-02-01', RdwPc: 13 },
        { Date: '2024-03-01', AlbGL: 46 },
        { Date: '2026-01-01', AlbGL: 47 },
        { Date: '2026-01-01', AlbGL: 48 }
    ])).records;
    const series = data.series(records, record => record.values.AlbGL ?? null);
    assert.equal(series.filter(point => point.recordId).length, 5);
    assert.equal(series.find(point => point.recordId === records[1].id).y, null);
    assert.equal(series.filter(point => point.recordId === null).length, 2);
    assert.equal(series.filter(point => point.y !== null).length, 4);
});

test('ECDF uses ties and the complete reference denominator, not a fitted normal distribution', () => {
    const curve = plain(data.ecdf([2, 2, 4]));
    assert.deepEqual(curve.map(point => point.x), [2, 2, 4, 4]);
    close(curve[1].y, 200 / 3);
    assert.equal(curve.at(-1).y, 100);
    const count = plain(data.counts([2, 2, 4], 2));
    assert.deepEqual({ lower: count.lower, equal: count.equal, higher: count.higher }, { lower: 0, equal: 2, higher: 1 });
    close(count.atOrBelow, 200 / 3);
    assert.equal(data.counts([45.99999999999999, 46, 46.001], 46).equal, 2);
    assert.equal(data.counts([45.99999999999999, 46, 46.001], 46).higher, 1);
    assert.deepEqual(plain(data.ecdf([])), []);
});

test('personal standardization rejects sparse or constant histories and uses sample SD', () => {
    assert.equal(data.standardization([1, 2]), null);
    assert.equal(data.standardization([2, 2, 2, 2, 2]), null);
    const basis = data.standardization([1, 2, 3, 4, 5, NaN]);
    assert.equal(basis.n, 5);
    assert.equal(basis.mean, 3);
    close(basis.sd, Math.sqrt(2.5));
});

test('rank association respects ties, direction and zero variation', () => {
    const pairs = Array.from({ length: 25 }, (_, index) => ({ x: Math.floor(index / 5), y: 2 * Math.floor(index / 5) }));
    close(data.rankAssociation(pairs), 1);
    close(data.rankAssociation(pairs.map(pair => ({ x: pair.x, y: -pair.y }))), -1);
    assert.equal(data.rankAssociation(pairs.map(pair => ({ x: pair.x, y: 2 }))), null);
    assert.equal(data.rankAssociation([{ x: 1, y: 2 }, { x: 2, y: 4 }]), null);
});

test('every complete clock-change explanation reconciles to the actual difference including age', () => {
    const michael = data.normalize(fixture('michael_lustgarten')).records;
    const full = fixture('benjamin_garden').Biomarkers.at(-1);
    const bortz = data.normalize(athlete([full, { ...full, Date: '2026-09-03', AlbGL: 66, Wbc1000cellsuL: 5.8, MonocytePc: 12, VitaminDNmolL: 200 }])).records;
    for (const [clock, records] of [['pheno', michael], ['bortz', bortz]]) {
        const before = records[0]; const after = records.at(-1);
        const rows = data.clockChange(before, after, clock);
        close(rows.reduce((sum, row) => sum + row.change, 0), (after[clock] - after.age) - (before[clock] - before.age));
        assert.ok(rows.some(row => row.key === 'age'));
    }
    const rows = data.clockChange(bortz[0], bortz[1], 'bortz');
    assert.ok(rows.some(row => row.key === 'CholesterolMmolL'));
    assert.ok(rows.some(row => row.key === 'CreatUmolL'));
    assert.ok(rows.some(row => row.key === 'ShbgNmolL'));
    assert.ok(rows.some(row => row.key === 'MonocytePc' && row.label.includes('derived from WBC')));
});

test('fixed biomarkers still change clock differences as chronological age advances', () => {
    const records = data.normalize(athlete([panel, { ...panel, Date: '2027-01-01' }])).records;
    const ageDelta = records[1].age - records[0].age;
    const diffDelta = (records[1].pheno - records[1].age) - (records[0].pheno - records[0].age);
    close(diffDelta, (0.0804 / 0.090165 - 1) * ageDelta);
    const rows = data.clockChange(records[0], records[1], 'pheno');
    assert.ok(rows.filter(row => row.key !== 'age' && row.key !== 'remainder').every(row => row.change === 0));
});

test('dense histories preserve every observation and scale to a thousand paired tests', () => {
    const records = Array.from({ length: 1000 }, (_, index) => ({ ...panel, Date: new Date(Date.UTC(2020, 0, 1 + index)).toISOString().slice(0, 10), AlbGL: 40 + index % 15 }));
    const normalized = data.normalize(athlete(records));
    assert.equal(normalized.records.length, 1000);
    assert.equal(data.samePanelPairs(normalized.records, 'AlbGL', 'RdwPc').length, 1000);
    assert.equal(data.series(normalized.records, record => record.values.AlbGL).length, 1000);
    assert.equal(data.ecdf(normalized.records.map(record => record.values.AlbGL)).at(-1).y, 100);
});
