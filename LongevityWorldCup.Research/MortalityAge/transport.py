"""Frozen-model cycle/assay and longer-horizon diagnostics; no model retuning."""
import os
os.environ.setdefault('OPENBLAS_NUM_THREADS', '2')
os.environ.setdefault('OMP_NUM_THREADS', '2')
from pathlib import Path
import hashlib
import json
from datetime import datetime, timezone
import warnings
import numpy as np
import pandas as pd
from lifelines import CoxPHFitter
from lifelines.exceptions import StatisticalWarning
from lifelines.utils import concordance_index
from model import CORE, reference_design, survey_weights, predict_risk, weighted_km
from diagnostics import as_fit, dx, cluster_multipliers

ROOT = Path(__file__).resolve().parents[2] / '.artifacts' / 'mortality-age'
OUT = ROOT / 'results'


def evaluate_horizon(frame, risk, weights, horizon):
    t, d = frame.time.to_numpy()/12, frame.event.to_numpy()
    risk = np.asarray(risk)
    died, survived = (d == 1) & (t <= horizon), t > horizon
    observed = 1-float(weighted_km(t, d, weights, horizon))
    expected = float(np.average(risk, weights=weights))
    gt = weighted_km(t, 1-d, weights, np.maximum(t-1e-8, 0))
    gh = float(weighted_km(t, 1-d, weights, horizon))
    brier = None
    if gh > 0 and np.all(gt[died] > 0):
        errors = np.zeros(len(frame))
        errors[died] = (1-risk[died])**2/gt[died]
        errors[survived] = risk[survived]**2/gh
        brier = float(np.average(errors, weights=weights))
    try:
        cindex = float(concordance_index(t, -risk, d))
    except ZeroDivisionError:
        cindex = None
    slope = None
    if died.sum() >= 30:
        calibration = pd.DataFrame(dict(t=np.minimum(t, horizon), d=died.astype(int),
            logCumulative=np.log(-np.log1p(-risk)), weight=weights/weights.mean()))
        try:
            fit = CoxPHFitter().fit(calibration, 't', 'd', weights_col='weight')
            value = float(fit.params_.iloc[0])
            slope = value if np.isfinite(value) else None
        except Exception:
            slope = None
    administrative = t[d == 0]
    result = dict(n=len(frame), deaths=int(d.sum()), horizonYears=horizon,
        horizonDeaths=int(died.sum()), medianFollowupYears=float(np.median(t)),
        minimumCensoredFollowupYears=float(administrative.min()) if len(administrative) else None,
        weightedObservedRisk=observed, weightedExpectedRisk=expected,
        observedExpected=observed/expected, ipcwBrier=brier, unweightedHarrellC=cindex,
        calibrationSlope=slope, effectiveWeightN=float(weights.sum()**2/np.sum(weights**2)), bySex={})
    for sex in [0, 1]:
        ok = frame.male.to_numpy() == sex
        if ok.any():
            obs = 1-float(weighted_km(t[ok], d[ok], weights[ok], horizon))
            exp = float(np.average(risk[ok], weights=weights[ok]))
            result['bySex'][str(sex)] = dict(n=int(ok.sum()), deaths=int(d[ok].sum()),
                horizonDeaths=int(died[ok].sum()), weightedObservedRisk=obs,
                weightedExpectedRisk=exp, observedExpected=obs/exp)
    return result


def calibration_ranges(frame, risk, weights, horizon, replicates=128):
    rng = np.random.default_rng(91258)
    t, d = frame.time.to_numpy()/12, frame.event.to_numpy()
    ratios, by_sex = [], {'0': [], '1': []}
    for _ in range(replicates):
        bw = weights*cluster_multipliers(frame, rng)
        ok = bw > 0
        observed = 1-float(weighted_km(t[ok], d[ok], bw[ok], horizon))
        ratios.append(observed/np.average(risk[ok], weights=bw[ok]))
        for sex in [0, 1]:
            so = ok & (frame.male.to_numpy() == sex)
            if so.any():
                obs = 1-float(weighted_km(t[so], d[so], bw[so], horizon))
                by_sex[str(sex)].append(obs/np.average(risk[so], weights=bw[so]))
    return dict(replicates=replicates, intervalPercentiles=[10, 90],
        observedExpected80=np.quantile(ratios, [.1, .9]).tolist(),
        bySexObservedExpected80={s: np.quantile(v, [.1, .9]).tolist() if v else None for s,v in by_sex.items()},
        conditionalOn='Frozen models and measurement distributions; survey PSUs resampled within strata')


def main():
    warnings.filterwarnings('ignore', category=StatisticalWarning)
    from train import evaluate
    full = json.loads((OUT/'full-model.json').read_text())
    panels = json.loads((OUT/'panel-models.json').read_text())
    config = json.loads((OUT/'frozen-config.json').read_text())
    source = Path(__file__).parent
    results = dict(planFile=config['planFile'],
        planSha256=hashlib.sha256((source/config['planFile']).read_bytes()).hexdigest(),
        evaluationReuse=config['evaluationReuse'],
        modelInputsSha256=hashlib.sha256(json.dumps(dict(full=full, panels=panels), sort_keys=True, separators=(',', ':')).encode()).hexdigest(),
        primaryRetuned=False, diagnostics={})
    data = pd.read_csv(ROOT/'harmonized.csv').dropna(subset=CORE)
    for cycle, horizon in [(2015, 2), (2003, 10)]:
        comparison = {}
        for label, model in [('full', full), *panels.items()]:
            frame = data[data.cycle == cycle]
            if label != 'full':
                frame = frame.dropna(subset=model['features'])
            frame = frame[frame.age <= model['trainingAgeRange'][1]].reset_index(drop=True)
            marker = 'cystatin' if label in ['blood', 'fitness'] else 'apob' if label in ['lipid', 'strength'] else None
            frame = frame[survey_weights(frame, marker) > 0].reset_index(drop=True)
            if frame.empty:
                comparison[label] = dict(status='unavailable', n=0)
                continue
            weights = survey_weights(frame, marker)
            x, _, _ = dx(frame, model)
            risk = predict_risk(as_fit(model), x, frame.male.to_numpy(), horizon)
            result = evaluate_horizon(frame, risk, weights, horizon)
            result['status'] = 'diagnostic-only'
            result['calibrationRange'] = calibration_ranges(frame, risk, weights, horizon)
            # Compare both benchmarks on exactly the same observed rows/weights.
            xr, _, _ = reference_design(frame)
            reference_risk = predict_risk(as_fit(model['reference']), xr, frame.male.to_numpy(), horizon)
            cx, _, _ = dx(frame, panels['core'])
            core_risk = predict_risk(as_fit(panels['core']), cx, frame.male.to_numpy(), horizon)
            result['ageSexBenchmark'] = evaluate_horizon(frame, reference_risk, weights, horizon)
            result['commonCoreBenchmark'] = evaluate_horizon(frame, core_risk, weights, horizon)
            if cycle == 2003 and label == 'core':
                risk5 = predict_risk(as_fit(model), x, frame.male.to_numpy(), 5)
                old, new = evaluate(frame, risk5, weights), evaluate_horizon(frame, risk5, weights, 5)
                for a,b in [('weightedObserved5y','weightedObservedRisk'), ('weightedExpected5y','weightedExpectedRisk'), ('ipcwBrier5y','ipcwBrier')]:
                    np.testing.assert_allclose(old[a], new[b], atol=1e-11, rtol=1e-10)
                results['fiveYearEvaluatorParity'] = True
            comparison[label] = result
            print(cycle, label, 'horizon', horizon, 'n', result['n'], 'deaths', result['horizonDeaths'],
                  'O/E', round(result['observedExpected'], 3), flush=True)
        results['diagnostics'][str(cycle)] = comparison
    results['completedUtc'] = datetime.now(timezone.utc).isoformat()
    (OUT/'transport.json').write_text(json.dumps(results, indent=2, allow_nan=False), encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
