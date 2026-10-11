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
const planHash = createHash('sha256').update(readFileSync(new URL(research.run.config.planFile, import.meta.url))).digest('hex');
assert.equal(planHash, research.run.config.planSha256, 'Age-free plan bytes must match the research checksum');
assert.equal(planHash, research.transport.planSha256, 'Additional diagnostics must follow the age-free plan');
assert.equal(research.transport.primaryRetuned, false);
assert.equal(research.transport.fiveYearEvaluatorParity, true);
assert.equal(planHash, research.validationUncertainty.planSha256, 'Validation uncertainty must follow the age-free plan');
assert.equal(research.validationUncertainty.primaryRetuned, false);
for (const validation of Object.values(research.validationUncertainty.models)) assert.equal(validation.evaluatorParity, true);
assert.equal(bundle.modelVersion, research.modelVersion);
assert.equal(bundle.modelVersion, fixtureSet.modelVersion);
assert.equal(bundle.schemaVersion, 2);
assert.equal(bundle.requiresChronologicalAge, false);
assert.equal(research.run.config.chronologicalAgeInRisk, false);
assert.equal(research.run.config.chronologicalAgeInMissingInputDistribution, false);
function near(actual, expected, tolerance = 1e-10) { assert.ok(Math.abs(actual - expected) < tolerance, `${actual} != ${expected}`); }
for (const fixture of fixtureSet.fixtures) {
    const model = fixture.model === 'full' ? bundle.full : bundle.panels[fixture.model];
    assert.equal(model.requiresChronologicalAge, false);
    assert.equal(Object.keys(model.coefficients).some(key => key.includes('age')), false);
    assert.deepEqual(model.riskInputs, ['male', ...model.features]);
    assert.equal('age' in fixture.inputs, false);
    const hazard = api.cumulativeHazard(fixture.inputs, model);
    for (const age of [18, 40, 79, NaN]) {
        const alternative = { ...fixture.inputs, age };
        assert.equal(api.cumulativeHazard(alternative, model), hazard);
        assert.deepEqual(api.calculate(alternative, model), api.calculate(fixture.inputs, model));
        for (const refit of [...(model.bootstrap ?? []), ...(model.dependence ?? [])]) {
            assert.equal(Object.keys(refit.coefficients).some(key => key.includes('age')), false);
            assert.equal(api.cumulativeHazard(alternative, model, refit.coefficients, refit.gamma), api.cumulativeHazard(fixture.inputs, model, refit.coefficients, refit.gamma));
        }
    }
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
// Boundary labels preserve the mortality score and never expose an exact extrapolated age.
for (const male of [0, 1]) {
    const sexProfile = fixtureSet.fixtures.find(f => f.model === 'full' && f.inputs.male === male).inputs;
    const original = api.calculate(sexProfile, bundle.full);
    for (const [target, boundary] of [[17.999, '<18'], [40, undefined], [79.001, '>79']]) {
        const reference = structuredClone(bundle.full.reference);
        const coefficients = reference.coefficients;
        const hazard = api.cumulativeHazard(sexProfile, bundle.full);
        const gamma = reference.gamma[male];
        const baseline = Math.expm1(gamma * 5) / gamma;
        coefficients.intercept = Math.log(hazard / baseline) - coefficients.male * male
            - coefficients[male === 1 ? 'age_male' : 'age_female'] * (target - 45) / 10;
        const result = api.calculate(sexProfile, { ...bundle.full, reference });
        assert.equal(result.supported, true);
        assert.equal(result.risk5, original.risk5);
        assert.equal(result.ageBoundary, boundary);
        if (boundary) assert.equal(result.age, null);
        else near(result.age, target);
    }
}
const simpleReference = { coefficients: { intercept: 0, male: 0, age_male: 1, age_female: 1 }, gamma: [0, 0] };
for (const age of [18, 79]) near(api.ageFromHazard(5 * Math.exp((age - 45) / 10), 1, simpleReference), age);
assert.equal(api.calculate(profile, { ...bundle.full, coefficients: { ...bundle.full.coefficients, intercept: Infinity } }).supported, false);
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
assert.equal(api.calculate({ ...profile, age: 60 }, bundle.full).supported, true);
assert.equal(api.calculate(profile, { ...bundle.full, requiresChronologicalAge: true }).supported, false);
assert.equal(api.calculate(profile, { ...bundle.full, coefficients: { ...bundle.full.coefficients, age_male: .5 } }).supported, false);
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
console.log(`Verified ${fixtureSet.fixtures.length} Python/browser fixtures, age invariance in every panel/refit, provenance checksums, units, sex-dependent functions, reference boundaries and unsupported results (${bundle.modelVersion}).`);
