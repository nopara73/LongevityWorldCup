"""Evaluation uncertainty and sensitivities; never retunes the frozen primary fit."""
import os
os.environ.setdefault('OPENBLAS_NUM_THREADS', '2')
os.environ.setdefault('OMP_NUM_THREADS', '2')
from pathlib import Path
import hashlib
import json
import warnings
import numpy as np
import pandas as pd
from lifelines import KaplanMeierFitter
from lifelines.exceptions import StatisticalWarning
from lifelines.utils import concordance_index
from model import (Fit, FEATURES, CORE, survey_weights, fit_survival, predict_risk,
                   reference_age, design, transform, integration_values, weighted_km)

ROOT = Path(__file__).resolve().parents[2] / '.artifacts' / 'mortality-age'
OUT = ROOT / 'results'


def read(name):
    return json.loads((OUT / name).read_text())


def as_fit(model):
    return Fit(np.array(list(model['coefficients'].values())), np.array(model['gamma']), True, '', 0, 0)


def dx(frame, model):
    if model['features'] == FEATURES:
        values = integration_values(frame, model['curves'], model['distributions'], 32, 91237)
    else:
        values = np.column_stack([transform(f, frame[f]) for f in model['features']])[:, None, :]
    return design(frame, values, model['features'], model['curves'], model['smooth'])


def cluster_multipliers(frame, rng):
    result = np.zeros(len(frame))
    for stratum in frame.stratum.unique():
        rows = np.flatnonzero(frame.stratum.to_numpy() == stratum)
        units = frame.iloc[rows].psu.unique()
        sampled = rng.choice(units, len(units), replace=True)
        for unit in units:
            result[rows[frame.iloc[rows].psu.to_numpy() == unit]] = np.sum(sampled == unit)
    return result


def bootstrap_calibration(frame, risk, weights, replicates=128):
    rng = np.random.default_rng(91237+21)
    ratios, observed_rates = [], []
    sex_ratios = {'0': [], '1': []}
    t, d = frame.time.to_numpy()/12, frame.event.to_numpy()
    for _ in range(replicates):
        bw = weights*cluster_multipliers(frame, rng)
        ok = bw > 0
        observed = 1-float(weighted_km(t[ok], d[ok], bw[ok], 5))
        expected = float(np.average(risk[ok], weights=bw[ok]))
        ratios.append(observed/expected)
        observed_rates.append(observed)
        for s in [0, 1]:
            so = ok & (frame.male.to_numpy() == s)
            sex_ratios[str(s)].append((1-float(weighted_km(t[so], d[so], bw[so], 5)))/np.average(risk[so], weights=bw[so]))
    return dict(replicates=replicates, intervalPercentiles=[10, 90],
                observedExpected80=np.quantile(ratios, [.1, .9]).tolist(),
                observedRisk80=np.quantile(observed_rates, [.1, .9]).tolist(),
                bySexObservedExpected80={s: np.quantile(v, [.1, .9]).tolist() for s,v in sex_ratios.items()},
                conditionalOn='Frozen primary coefficients and models; survey PSUs resampled within strata')


def main():
    warnings.filterwarnings('ignore', category=StatisticalWarning)
    from train import evaluate
    full, panels, config = read('full-model.json'), read('panel-models.json'), read('frozen-config.json')
    # Completed sections are reusable only for the same data, fits and code.
    # A corrected training run must never inherit stale calibration results.
    source = Path(__file__).parent
    provenance = dict(full=full, panels=panels, config=config,
        code={name: hashlib.sha256((source/name).read_bytes()).hexdigest()
              for name in ['model.py', 'train.py', 'diagnostics.py']},
        dataSha256=hashlib.sha256((ROOT/'harmonized.csv').read_bytes()).hexdigest())
    input_hash = hashlib.sha256(json.dumps(provenance, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    data = pd.read_csv(ROOT / 'harmonized.csv').dropna(subset=CORE)
    data = data[survey_weights(data) > 0].reset_index(drop=True)
    dev = data[data.cycle.isin(config['development'])].reset_index(drop=True)
    hold = data[data.cycle.isin(config['evaluation'])].reset_index(drop=True)
    saved = read('diagnostics.json') if (OUT/'diagnostics.json').exists() else None
    diagnostics = (saved if saved and saved.get('inputSha256') == input_hash else
        dict(inputSha256=input_hash, postFreezeDiagnosticOnly=True, modelRetuned=False, calibration={}, landmark={}))
    for label, model in [('full', full), *panels.items()]:
        if label in diagnostics['calibration'] and (label == 'full' or label in diagnostics['landmark']):
            print(f'Using completed diagnostic: {label}', flush=True)
            continue
        frame = hold if label == 'full' else hold.dropna(subset=model['features'])
        frame = frame[frame.age <= model['ageRange'][1]].reset_index(drop=True)
        marker = 'cystatin' if label in ['blood', 'fitness'] else 'apob' if label in ['lipid', 'strength'] else None
        frame = frame[survey_weights(frame, marker) > 0].reset_index(drop=True)
        x, names, penalties = dx(frame, model)
        risk = predict_risk(as_fit(model), x, frame.male.to_numpy())
        diagnostics['calibration'][label] = bootstrap_calibration(frame, risk, survey_weights(frame, marker))
        if label == 'full':
            cx, _, _ = dx(frame, panels['core'])
            core_risk = predict_risk(as_fit(panels['core']), cx, frame.male.to_numpy())
            diagnostics['commonCoreBenchmarkUnder50'] = evaluate(frame, core_risk, survey_weights(frame))
        if label != 'full':
            train = dev.dropna(subset=model['features'])
            train = train[(train.age <= model['ageRange'][1]) & (train.time > 24)].copy().reset_index(drop=True)
            # Observed-panel two-year landmark: correct time origin and attained age.
            train['time'] -= 24
            train['age'] += 2
            tx, tn, tp = dx(train, model)
            landmark = fit_survival(tx, train, survey_weights(train, marker), tp, model['ridge'])
            diagnostics['landmark'][label] = dict(n=len(train), events=int(train.event.sum()),
                fit=landmark.json(tn), caveat='Baseline biomarkers are two years old; survivor selection remains')
        print(f'Diagnostic saved: {label}', flush=True)
        (OUT / 'diagnostics.json').write_text(json.dumps(diagnostics, indent=2, allow_nan=False))

    young = dev[dev.age <= 49].reset_index(drop=True)
    x, names, penalties = dx(young, full)
    exponential = fit_survival(x, young, survey_weights(young), penalties, full['ridge'], fixed_gamma=0)
    xr, nr, pr = design(dev, np.empty((len(dev), 1, 0)), [], {}, False)
    exponential_ref = fit_survival(xr, dev, survey_weights(dev), pr, 0, fixed_gamma=0)
    examples = pd.DataFrame([p['inputs'] for p in read('sensitivity.json')['exampleProfiles']])
    ev = np.column_stack([transform(f, examples[f]) for f in FEATURES])[:, None, :]
    ex, _, _ = design(examples, ev, FEATURES, full['curves'], full['smooth'])
    risk = predict_risk(exponential, ex, examples.male.to_numpy())
    ages = reference_age(risk, examples.male.to_numpy(), exponential_ref)
    diagnostics['timeForm'] = dict(alternative='Exponential baseline, sex-specific constant hazards',
        fit=exponential.json(names), reference=exponential_ref.json(nr),
        exampleAge=[float(a) if np.isfinite(a) else None for a in ages],
        limitation='Sensitivity only; a time-form comparison does not establish proportional hazards')

    # Independent component-profile sweeps across age/sex and supported input values.
    sweep = []
    scenarios = read('sensitivity.json')['dependence']
    for sex in [0, 1]:
        median = read('sensitivity.json')['exampleProfiles'][sex]['inputs']
        for age in [20, 30, 40, 49]:
            for feature in FEATURES:
                lo, hi = full['curves'][feature]['support'][str(sex)]
                for value in np.linspace(lo, hi, 9):
                    row = {**median, 'age': age, feature: float(value)}
                    if row['sbp'] <= row['dbp']:
                        continue
                    frame = pd.DataFrame([row])
                    values = np.column_stack([transform(f, frame[f]) for f in FEATURES])[:, None, :]
                    fx, _, _ = design(frame, values, FEATURES, full['curves'], full['smooth'])
                    risks = [float(predict_risk(as_fit(full), fx, frame.male.to_numpy())[0])]
                    for scenario in scenarios:
                        risks.append(float(predict_risk(as_fit(scenario['fit']), fx, frame.male.to_numpy())[0]))
                    ref = as_fit(full['reference'])
                    eq = [reference_age(np.array([r]), np.array([sex]), ref)[0] for r in risks]
                    finite = [float(a) for a in eq if np.isfinite(a)]
                    sweep.append(dict(male=sex, age=age, feature=feature, value=float(value),
                        primaryAge=float(eq[0]) if np.isfinite(eq[0]) else None,
                        dependenceAgeSpan=max(finite)-min(finite) if finite else None))
    spans = [r['dependenceAgeSpan'] for r in sweep if r['dependenceAgeSpan'] is not None]
    diagnostics['profileSweep'] = dict(n=len(sweep), profiles=sweep,
        unsupportedPrimary=int(sum(r['primaryAge'] is None for r in sweep)),
        maxDependenceAgeSpan=float(max(spans)), medianDependenceAgeSpan=float(np.median(spans)),
        caveat='Artificial one-at-a-time profiles; not observed full cases or a simultaneous joint-tail test')
    (OUT / 'diagnostics.json').write_text(json.dumps(diagnostics, indent=2, allow_nan=False))
    print('Diagnostics completed', flush=True)


if __name__ == '__main__':
    main()
