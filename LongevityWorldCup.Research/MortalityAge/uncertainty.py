"""Conditional validation metric intervals and paired errors; no predictor refitting."""
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
from lifelines.exceptions import StatisticalWarning
from lifelines.utils import concordance_index
from model import CORE, reference_design, survey_weights, predict_risk, weighted_km
from diagnostics import as_fit, dx, cluster_multipliers
from transport import evaluate_horizon

ROOT = Path(__file__).resolve().parents[2] / '.artifacts' / 'mortality-age'
OUT = ROOT / 'results'


def ipcw_errors(frame, risk, weights, horizon=5):
    t, d = frame.time.to_numpy()/12, frame.event.to_numpy()
    died, survived = (d == 1) & (t <= horizon), t > horizon
    gt = weighted_km(t, 1-d, weights, np.maximum(t-1e-8, 0))
    gh = float(weighted_km(t, 1-d, weights, horizon))
    if gh <= 0 or np.any(gt[died] <= 0):
        return None
    errors = np.zeros(len(frame))
    errors[died] = (1-risk[died])**2/gt[died]
    errors[survived] = risk[survived]**2/gh
    return errors


def paired_brier_improvement(frame, risk, benchmark, weights, horizon=5):
    main = ipcw_errors(frame, np.asarray(risk), weights, horizon)
    base = ipcw_errors(frame, np.asarray(benchmark), weights, horizon)
    if main is None or base is None:
        return None
    return float(np.average(base-main, weights=weights))


def interval(values, total):
    finite = [float(x) for x in values if x is not None and np.isfinite(x)]
    return dict(estimableReplicates=len(finite), totalReplicates=total,
        range80=np.quantile(finite, [.1, .9]).tolist() if len(finite) >= .8*total else None)


def main():
    warnings.filterwarnings('ignore', category=StatisticalWarning)
    from train import evaluate
    full = json.loads((OUT/'full-model.json').read_text())
    panels = json.loads((OUT/'panel-models.json').read_text())
    config = json.loads((OUT/'frozen-config.json').read_text())
    source = Path(__file__).parent
    result = dict(planFile=config['planFile'],
        planSha256=hashlib.sha256((source/config['planFile']).read_bytes()).hexdigest(),
        evaluationReuse=config['evaluationReuse'],
        primaryRetuned=False, replicates=128, seed=91258, intervalPercentiles=[10, 90], models={},
        conditionalOn='Frozen models, curves, measurement distributions, selection and reference; survey PSUs resampled within strata')
    data = pd.read_csv(ROOT/'harmonized.csv').dropna(subset=CORE)
    holdout = data[data.cycle.isin(config['evaluation'])]
    for label, model in [('full', full), *panels.items()]:
        frame = holdout if label == 'full' else holdout.dropna(subset=model['features'])
        frame = frame[frame.age <= model['trainingAgeRange'][1]].reset_index(drop=True)
        marker = 'cystatin' if label in ['blood', 'fitness'] else 'apob' if label in ['lipid', 'strength'] else None
        frame = frame[survey_weights(frame, marker) > 0].reset_index(drop=True)
        weights = survey_weights(frame, marker)
        x, _, _ = dx(frame, model)
        risk = predict_risk(as_fit(model), x, frame.male.to_numpy())
        point = evaluate_horizon(frame, risk, weights, 5)
        previous = evaluate(frame, risk, weights)
        for a,b in [('weightedObserved5y','weightedObservedRisk'), ('ipcwBrier5y','ipcwBrier'), ('unweightedHarrellC','unweightedHarrellC')]:
            np.testing.assert_allclose(previous[a], point[b], atol=1e-11, rtol=1e-10)
        metrics = dict(brier=[], concordance=[], slope=[])
        benchmarks, improvements = {}, {}
        if label == 'full':
            xr, _, _ = reference_design(frame)
            benchmarks['ageSex'] = predict_risk(as_fit(model['reference']), xr, frame.male.to_numpy())
            cx, _, _ = dx(frame, panels['core'])
            benchmarks['commonCore'] = predict_risk(as_fit(panels['core']), cx, frame.male.to_numpy())
            improvements = {name: [] for name in benchmarks}
        rng = np.random.default_rng(result['seed'])
        for _ in range(result['replicates']):
            multiplicities = cluster_multipliers(frame, rng).astype(int)
            bw = weights*multiplicities
            ok = bw > 0
            sampled = frame.loc[ok].reset_index(drop=True)
            evaluation = evaluate_horizon(sampled, risk[ok], bw[ok], 5)
            metrics['brier'].append(evaluation['ipcwBrier'])
            metrics['slope'].append(evaluation['calibrationSlope'] if point['calibrationSlope'] is not None else None)
            rows = np.repeat(np.arange(len(frame)), multiplicities)
            try:
                value = float(concordance_index(frame.time.to_numpy()[rows]/12, -risk[rows], frame.event.to_numpy()[rows]))
            except ZeroDivisionError:
                value = None
            metrics['concordance'].append(value)
            for name, predictions in benchmarks.items():
                improvements[name].append(paired_brier_improvement(sampled, risk[ok], predictions[ok], bw[ok]))
        entry = dict(n=len(frame), horizonDeaths=point['horizonDeaths'], point=dict(
            brier=point['ipcwBrier'], concordance=point['unweightedHarrellC'], slope=point['calibrationSlope']),
            ranges={name: interval(values, result['replicates']) for name,values in metrics.items()},
            evaluatorParity=True)
        if benchmarks:
            entry['pairedBrierImprovement'] = {name: dict(
                point=paired_brier_improvement(frame, risk, predictions, weights),
                **interval(improvements[name], result['replicates'])) for name,predictions in benchmarks.items()}
        result['models'][label] = entry
        (OUT/'uncertainty.json').write_text(json.dumps(result, indent=2, allow_nan=False), encoding='utf-8', newline='\n')
        print(label,entry['ranges'],entry.get('pairedBrierImprovement',''),flush=True)
    result['completedUtc'] = datetime.now(timezone.utc).isoformat()
    (OUT/'uncertainty.json').write_text(json.dumps(result, indent=2, allow_nan=False), encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
