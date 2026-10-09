"""Export derived research artifacts and the frozen browser model. No raw data."""
from pathlib import Path
import hashlib
import json
import platform
import importlib.metadata
import numpy as np
import pandas as pd
from model import Fit, FEATURES, transform, design, predict_risk, reference_age

REPO = Path(__file__).resolve().parents[2]
ROOT = REPO / '.artifacts' / 'mortality-age'
RESULTS = ROOT / 'results'
PUBLIC = REPO / 'LongevityWorldCup.Website' / 'wwwroot' / 'research'
ARCHIVE = Path(__file__).parent / 'artifacts'


def read(name):
    return json.loads((RESULTS / name).read_text())


def write_json(path, value):
    # Match the repository's LF bytes on every platform so provenance hashes
    # describe the published artifact, including when exporting on Windows.
    path.write_text(json.dumps(value, indent=2, allow_nan=False), encoding='utf-8', newline='\n')


def stripped_model(model):
    keep = ['coefficients', 'gamma', 'features', 'curves', 'smooth', 'ridge', 'ageRange',
            'horizonYears', 'reference', 'status', 'development']
    return {k: model[k] for k in keep}


def numerical_prediction(profile, model, fitted=None):
    frame = pd.DataFrame([profile])
    x, _, _ = design(frame, np.array([[transform(f, [profile[f]])[0] for f in model['features']]])[:, None, :],
                     model['features'], model['curves'], model['smooth'])
    fit = fitted or Fit(np.array(list(model['coefficients'].values())), np.array(model['gamma']), True, '', 0, 0)
    ref = model['reference']
    reference = Fit(np.array(list(ref['coefficients'].values())), np.array(ref['gamma']), True, '', 0, 0)
    risk = float(predict_risk(fit, x, frame.male.to_numpy())[0])
    age = float(reference_age(np.array([risk]), frame.male.to_numpy(), reference)[0])
    return dict(risk5=risk, age=age if np.isfinite(age) else None)


def main():
    run = read('run.json')  # Requires a completed run, never a partially written result.
    full, panels = read('full-model.json'), read('panel-models.json')
    sensitivity, bootstrap = read('sensitivity.json'), read('bootstrap.json')
    public_full = stripped_model(full)
    public_full['bootstrap'] = [dict(coefficients=r['fit']['coefficients'], gamma=r['fit']['gamma'])
        for r in bootstrap['fits'] if r['fit']['converged']]
    public_full['dependence'] = [dict(coefficients=r['fit']['coefficients'], gamma=r['fit']['gamma'],
                                    correlation=r['correlation']) for r in sensitivity['dependence'] if r['fit']['converged']]
    public_panels = {k: stripped_model(v) for k, v in panels.items()}
    # A release decision, not model retuning: four five-year test deaths cannot
    # validate the fitness panel's absolute calibration (observed/expected ~0.19).
    public_panels['fitness']['releaseStatus'] = 'withheld'
    public_panels['fitness']['releaseReason'] = 'The fitness panel has inadequate five-year calibration evidence.'
    for k, v in public_panels.items():
        if k != 'fitness':
            v['releaseStatus'] = 'experimental-observed-panel'
    bundle = dict(schemaVersion=1, name='Mortality age', horizonYears=5, full=public_full,
                  panels=public_panels, fullJointValidation=False,
                  caveat='Research experiment; mortality associations do not establish treatment effects')
    digest = hashlib.sha256(json.dumps(bundle, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    bundle['modelVersion'] = 'mortality-age-0.1-' + digest[:12]
    PUBLIC.mkdir(parents=True, exist_ok=True)
    ARCHIVE.mkdir(exist_ok=True)
    write_json(PUBLIC / 'mortality-age-model.json', bundle)
    # Independent Python expectations for browser parity, including sex functions.
    fixtures = []
    for profile in sensitivity['exampleProfiles']:
        for label, model in [('full', full), *panels.items()]:
            fixtures.append(dict(model=label, inputs=profile['inputs'], expected=numerical_prediction(profile['inputs'], model)))
    write_json(ARCHIVE / 'fixtures.json', dict(modelVersion=bundle['modelVersion'], fixtures=fixtures))
    manifest = json.loads((ROOT / 'downloads.json').read_text())
    write_json(ARCHIVE / 'downloads.json', manifest)
    overlap = json.loads((ROOT / 'overlap.json').read_text())
    aggregate = dict(run=run, fullResults=read('full-results.json'), panelResults=read('panel-results.json'),
                     fullModel=full, panelModels=panels, selection=read('selection.json'),
                     sensitivity=sensitivity, bootstrap=bootstrap, landmark=read('landmark.json'), overlap=overlap,
                     diagnostics=read('diagnostics.json'), proportionalEffects=read('ph-diagnostics.json'),
                     transport=read('transport.json'),
                     environment=dict(python=platform.python_version(), packages={n: importlib.metadata.version(n)
                         for n in ['numpy', 'scipy', 'pandas', 'lifelines', 'matplotlib', 'reportlab']}),
                     implementationCorrection=dict(priorCommit='954d0ad6cfd1393610de3f615be102f425330873',
                         changes=['Correct early four-year weight numerator from 0.5 to 2 relative to two-year weights',
                                  'Use applicable subsample weights for latent residual variances and covariances'],
                         predefinedModelChoicesChanged=False, previousTemporalResultsInspected=True,
                         evaluationStatus='Same predefined temporal split re-evaluated after implementation correction; not a new untouched validation sample'),
                     modelVersion=bundle['modelVersion'], modelSha256=hashlib.sha256((PUBLIC/'mortality-age-model.json').read_bytes()).hexdigest())
    write_json(ARCHIVE / 'results.json', aggregate)
    print(json.dumps(dict(modelVersion=bundle['modelVersion'], exported=str(PUBLIC),
                         bootstrapConverged=len(public_full['bootstrap']), fitnessNumericWithheld=True)), flush=True)


if __name__ == '__main__':
    main()
