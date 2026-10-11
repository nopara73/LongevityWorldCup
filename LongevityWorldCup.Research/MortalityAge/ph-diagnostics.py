"""Exploratory proportional-effects diagnosis in observed panels, after freezing."""
import warnings
from pathlib import Path
import json
import numpy as np
import pandas as pd
from lifelines import CoxPHFitter
from lifelines.statistics import proportional_hazard_test
from model import survey_weights, transform, design

ROOT = Path(__file__).resolve().parents[2] / '.artifacts' / 'mortality-age'
OUT = ROOT / 'results'


def main():
    warnings.filterwarnings('ignore', module='lifelines')
    data = pd.read_csv(ROOT / 'harmonized.csv')
    config = json.loads((OUT/'frozen-config.json').read_text())
    models = json.loads((OUT/'panel-models.json').read_text())
    results = {}
    for label in ['core', 'blood']:
        model = models[label]
        frame = data[data.cycle.isin(config['development'])].dropna(subset=model['features']).reset_index(drop=True)
        marker = 'cystatin' if label == 'blood' else None
        frame = frame[survey_weights(frame, marker) > 0].reset_index(drop=True)
        values = np.column_stack([transform(f, frame[f]) for f in model['features']])[:, None, :]
        x, names, _ = design(frame, values, model['features'], model['curves'], model['smooth'])
        # Sex-specific baseline hazards; remove intercept and within-stratum sex constant.
        analysis = pd.DataFrame(x[:, 0, 2:], columns=names[2:])
        analysis['time'], analysis['event'], analysis['sex'] = frame.time/12, frame.event, frame.male
        weights = survey_weights(frame, marker)
        analysis['weight'] = weights/weights.mean()
        fitted = CoxPHFitter(penalizer=.003).fit(analysis, 'time', 'event', strata=['sex'], weights_col='weight')
        test = proportional_hazard_test(fitted, analysis, time_transform='rank')
        results[label] = dict(approximateCoxDiagnostic=True, originalModelRetuned=False,
            test='Scaled Schoenfeld residual association with ranked event time',
            results={name: dict(testStatistic=float(row.test_statistic), p=float(row.p)) for name,row in test.summary.iterrows()},
            limitation='Exploratory, approximate p values; penalized, survey weighted, no design or multiple testing correction. Not a test of the integrated likelihood.')
        print(label, test.summary.sort_values('p').head(4).to_dict('index'), flush=True)
    (OUT/'ph-diagnostics.json').write_text(json.dumps(results, indent=2, allow_nan=False))


if __name__ == '__main__':
    main()
