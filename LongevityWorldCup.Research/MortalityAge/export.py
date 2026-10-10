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
    keep = ['coefficients', 'gamma', 'features', 'curves', 'smooth', 'ridge', 'trainingAgeRange',
            'riskInputs', 'requiresChronologicalAge',
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
    config = run['config']
    plan_hash = hashlib.sha256((Path(__file__).parent/config['planFile']).read_bytes()).hexdigest()
    if (run.get('status') != 'complete' or config['planSha256'] != plan_hash or
        config.get('chronologicalAgeInRisk') is not False or
        config.get('chronologicalAgeInMissingInputDistribution') is not False):
        raise ValueError('A completed, consistent age-free refit is required')
    for model in [full, *panels.values()]:
        if (model.get('requiresChronologicalAge') is not False or
            any('age' in key for key in model['coefficients']) or
            model['riskInputs'] != ['male', *model['features']]):
            raise ValueError('Chronological age remains in a risk model')
    if any(len(coef) != 10 for coef in full['distributions']['regressions'].values()):
        raise ValueError('Unexpected missing-input predictors')
    for name in ['transport.json', 'uncertainty.json']:
        if read(name)['planSha256'] != plan_hash:
            raise ValueError(f'Stale diagnostic artifact: {name}')
    risk_refits = ([r['fit'] for r in bootstrap['fits']] +
        [r['fit'] for r in sensitivity['dependence']] +
        [sensitivity['moreDraws']['fit'], read('landmark.json')['fit'],
         read('diagnostics.json')['timeForm']['fit']] +
        [r['fit'] for r in read('diagnostics.json')['landmark'].values()])
    if any(any('age' in key for key in fitted['coefficients']) for fitted in risk_refits):
        raise ValueError('Chronological age remains in a risk sensitivity refit')
    public_full = stripped_model(full)
    public_full['bootstrap'] = [dict(coefficients=r['fit']['coefficients'], gamma=r['fit']['gamma'])
        for r in bootstrap['fits'] if r['fit']['converged']]
    public_full['dependence'] = [dict(coefficients=r['fit']['coefficients'], gamma=r['fit']['gamma'],
                                    correlation=r['correlation']) for r in sensitivity['dependence'] if r['fit']['converged']]
    public_panels = {k: stripped_model(v) for k, v in panels.items()}
    # A release decision, not model retuning: four five-year test deaths cannot
    # validate the fitness panel's absolute calibration.
    public_panels['fitness']['releaseStatus'] = 'withheld'
    public_panels['fitness']['releaseReason'] = 'The fitness panel has inadequate five-year calibration evidence.'
    for k, v in public_panels.items():
        if k != 'fitness':
            v['releaseStatus'] = 'experimental-observed-panel'
    bundle = dict(schemaVersion=2, name='Mortality age', horizonYears=5,
                  requiresChronologicalAge=False, full=public_full,
                  panels=public_panels, fullJointValidation=False,
                  caveat='Research experiment; mortality associations do not establish treatment effects')
    digest = hashlib.sha256(json.dumps(bundle, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    bundle['modelVersion'] = 'mortality-age-0.2-' + digest[:12]
    PUBLIC.mkdir(parents=True, exist_ok=True)
    ARCHIVE.mkdir(exist_ok=True)
    write_json(PUBLIC / 'mortality-age-model.json', bundle)
    # Independent Python expectations for browser parity, including sex functions.
    fixtures = []
    for profile in sensitivity['exampleProfiles']:
        if set(profile['inputs']) != {'male', *FEATURES}:
            raise ValueError('Unexpected fixture inputs')
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
                     validationUncertainty=read('uncertainty.json'),
                     environment=dict(python=platform.python_version(), packages={n: importlib.metadata.version(n)
                         for n in ['numpy', 'scipy', 'pandas', 'lifelines', 'matplotlib', 'reportlab']}),
                     implementationCorrection=dict(priorCommit='954d0ad6cfd1393610de3f615be102f425330873',
                         changes=['Correct early four-year weight numerator from 0.5 to 2 relative to two-year weights',
                                  'Use applicable subsample weights for latent residual variances and covariances'],
                         predefinedModelChoicesChanged=False, previousTemporalResultsInspected=True,
                         evaluationStatus='Same predefined temporal split re-evaluated after implementation correction; not a new untouched validation sample'),
                     ageFreeRevision=dict(priorModelVersion='mortality-age-0.1-19e880e4f33f',
                         chronologicalAgeInRisk=False, chronologicalAgeInMissingInputDistribution=False,
                         referencePurpose='Fixed historical age/sex risk-to-years ruler; never uses the user\'s age',
                         evaluationStatus=config['evaluationReuse']),
                     modelVersion=bundle['modelVersion'], modelSha256=hashlib.sha256((PUBLIC/'mortality-age-model.json').read_bytes()).hexdigest())
    write_json(ARCHIVE / 'results.json', aggregate)
    print(json.dumps(dict(modelVersion=bundle['modelVersion'], exported=str(PUBLIC),
                         bootstrapConverged=len(public_full['bootstrap']), fitnessNumericWithheld=True)), flush=True)


if __name__ == '__main__':
    main()
