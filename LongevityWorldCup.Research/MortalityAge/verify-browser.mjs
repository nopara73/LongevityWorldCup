// Independent Python fixtures verify the exact browser equation and unit definitions.
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const root = new URL('../../', import.meta.url);
const context = { window: {} };
vm.createContext(context);
vm.runInContext(readFileSync(new URL('LongevityWorldCup.Website/Frontend/mortality-age-model.js', root), 'utf8'), context);
const api = context.window.LwcMortalityAgeModel;
const modelBytes = readFileSync(new URL('LongevityWorldCup.Website/wwwroot/research/mortality-age-model.json', root));
const bundle = JSON.parse(modelBytes);
const fixtureSet = JSON.parse(readFileSync(new URL('artifacts/fixtures.json', import.meta.url)));
const research = JSON.parse(readFileSync(new URL('artifacts/results.json', import.meta.url)));
assert.equal(createHash('sha256').update(modelBytes).digest('hex'), research.modelSha256, 'Published model bytes must match the research checksum');
assert.equal(createHash('sha256').update(readFileSync(new URL('analysis-plan.md', import.meta.url))).digest('hex'), research.run.config.planSha256, 'Frozen plan bytes must match the research checksum');
assert.equal(createHash('sha256').update(readFileSync(new URL('transport-plan.md', import.meta.url))).digest('hex'), research.transport.planSha256, 'Additional diagnostic plan must match the research checksum');
assert.equal(research.transport.primaryRetuned, false);
assert.equal(research.transport.fiveYearEvaluatorParity, true);
assert.equal(bundle.modelVersion, research.modelVersion);
assert.equal(bundle.modelVersion, fixtureSet.modelVersion);
function near(actual, expected, tolerance = 1e-10) { assert.ok(Math.abs(actual - expected) < tolerance, `${actual} != ${expected}`); }
for (const fixture of fixtureSet.fixtures) {
    const model = fixture.model === 'full' ? bundle.full : bundle.panels[fixture.model];
    const hazard = api.cumulativeHazard(fixture.inputs, model);
    const age = api.ageFromHazard(hazard, fixture.inputs.male, model.reference);
    near(-Math.expm1(-hazard), fixture.expected.risk5);
    if (fixture.expected.age === null) assert.equal(age, null);
    else near(age, fixture.expected.age);
}
near(api.convert('apob', .9, 'g/L'), 90);
near(api.convert('crp', .1, 'mg/dL'), 1);
near(api.convert('cystatin', .08, 'mg/dL'), .8);
near(api.convert('hba1c', 53, 'mmol/mol'), .09148 * 53 + 2.152);
near(api.convert('whr', 50, 'percent'), .5);
near(api.convert('grip', 100, 'lb'), 45.359237);
near(api.convert('sbp', 16, 'kPa'), 120.009869232);
near(api.convert('crp', .2, 'mg/L', true), .2 / Math.sqrt(2));
assert.ok(Number.isNaN(api.convert('vo2', 3, 'unsupported')));
const profile = fixtureSet.fixtures.find(f => f.model === 'full').inputs;
// An out-of-reference refit cannot be dropped to make a narrower range.
const outside = { coefficients: { ...bundle.full.coefficients, intercept: 0 }, gamma: bundle.full.gamma };
const boundaryRanges = api.calculate({ ...profile }, { ...bundle.full,
    bootstrap: [outside, ...bundle.full.bootstrap], dependence: [outside, ...bundle.full.dependence] });
assert.equal(boundaryRanges.supported, true);
assert.equal(boundaryRanges.samplingRange80, undefined);
assert.equal(boundaryRanges.samplingRangeUnsupported, true);
assert.equal(boundaryRanges.dependenceRange, undefined);
assert.equal(boundaryRanges.dependenceRangeUnsupported, true);
assert.equal(api.calculate({ ...profile, male: undefined }, bundle.full).supported, false);
assert.equal(api.calculate({ ...profile, age: 60 }, bundle.full).supported, false);
assert.equal(api.calculate({ ...profile, crp: 0 }, bundle.full).supported, false);
assert.equal(api.calculate({ ...profile, apob: 1000 }, bundle.full).supported, false);
assert.equal(api.calculate(profile, bundle.panels.fitness).supported, false);
for (const f of bundle.full.features) {
    const value = profile[f];
    const female = api.contribution(value, 0, f, bundle.full);
    const male = api.contribution(value, 1, f, bundle.full);
    const c = bundle.full.curves[f];
    near(male-female, bundle.full.coefficients[`${f}_sex`] * ((c.log ? Math.log(value) : value) - c.center) / c.scale);
}
console.log(`Verified ${fixtureSet.fixtures.length} Python/browser fixtures, provenance checksums, units, sex-dependent functions and unsupported results (${bundle.modelVersion}).`);
