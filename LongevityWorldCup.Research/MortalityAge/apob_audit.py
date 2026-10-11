"""Reproducible ApoB observed-data audit and frozen LWC sensitivity fits.

Raw participant rows remain in --output (which must be under .artifacts).
No result from this script is a validated age-free calculator or causal effect.
See apob-audit.md for source limitations and a runnable command.
"""
import os
os.environ.setdefault('OPENBLAS_NUM_THREADS', '2')
os.environ.setdefault('OMP_NUM_THREADS', '2')
from pathlib import Path
import argparse, hashlib, json, subprocess, warnings
import numpy as np
import pandas as pd
from apob_cox import Cox, rcs, contrast
from assays import harmonize_apob
from model import (CORE, FEATURES, survey_weights, transform, design, fit_survival,
                   learn_curves, learn_distributions, integration_values, cubic)

YEARS = [2005, 2007, 2009, 2011, 2013]
PAPER_COVARIATES = ['age','male','white','smoking','education','bmi','sbp','egfr',
                    'tg','hdl','energy','diabetes','hypertension','bpmed','dmmed','lipidmed']
GRID = np.arange(35., 211.)


def clean(value):
    if isinstance(value, dict): return {k: clean(v) for k,v in value.items()}
    if isinstance(value, (list,tuple)): return [clean(v) for v in value]
    if isinstance(value, np.ndarray): return clean(value.tolist())
    if isinstance(value, (np.floating,float)): return float(value) if np.isfinite(value) else None
    if isinstance(value, (np.integer,)): return int(value)
    if isinstance(value, (np.bool_,)): return bool(value)
    return value


def save(path, value):
    path.write_text(json.dumps(clean(value), indent=2, allow_nan=False), encoding='utf-8')


def saved_reference(cache,filename,key):
    path=cache/'results'/filename
    if path.exists():return json.loads(path.read_text())
    reference=Path(__file__).with_name('apob-reference.json')
    if not reference.exists():raise FileNotFoundError(f'Missing {path} and {reference}')
    return json.loads(reference.read_text())[key]


def fingerprint(frame):
    keys = frame[['cycle','SEQN']].sort_values(['cycle','SEQN']).astype(int)
    # Explicit canonical CRLF preserves the audited Windows hashes on any OS.
    return hashlib.sha256(keys.to_csv(index=False,lineterminator='\r\n').encode()).hexdigest()


def counts(frame):
    return dict(n=len(frame), deaths=int(frame.event.sum()),
                cardiovascularDeaths=int(((frame.event==1)&frame.cause.isin([1,5])).sum()) if 'cause' in frame else None,
                meanAge=float(frame.age.mean()), meanApoB=float(frame.apob.mean()) if 'apob' in frame else None,
                meanFollowupMonths=float(frame.time.mean()), participantSha256=fingerprint(frame),
                bySex={str(s): dict(n=int((frame.male==s).sum()), deaths=int(frame.loc[frame.male==s,'event'].sum())) for s in [0,1]},
                byCycle={str(y): dict(n=len(g), deaths=int(g.event.sum())) for y,g in frame.groupby('cycle')})


def landmark(frame, years):
    """Keep only survivors still observed at L; begin subsequent follow-up at L."""
    result = frame[frame.time > years*12].copy().reset_index(drop=True)
    result['time'] -= years*12
    assert (result.time > 0).all()
    return result


def mortality(path):
    f = pd.read_fwf(path, colspecs=[(0,6),(14,15),(15,16),(16,19),(42,45),(45,48)],
        names=['SEQN','eligible','event','cause','inttime','examtime'], na_values=['.']).set_index('SEQN')
    if f.index.duplicated().any(): raise ValueError('Duplicate mortality linkage IDs')
    if not f.eligible.dropna().isin([1,2,3]).all(): raise ValueError('Invalid linkage eligibility')
    return f


def prepare(cache, output):
    new = output/'data'
    rows, units = [], []
    audit = []
    def read(name):
        p = new/name if (new/name).exists() else cache/'data'/name
        raw = p.read_bytes()
        audit.append(dict(file=name, sha256=hashlib.sha256(raw).hexdigest()))
        f = pd.read_sas(p, format='xport').set_index('SEQN')
        if f.index.duplicated().any() and not name.startswith('RXQ_RX'): raise ValueError(name)
        return f
    for y in YEARS:
        s = chr(ord('D')+(y-2005)//2)
        d = read(f'DEMO_{s}.xpt')
        units.extend(d[['SDMVSTRA','SDMVPSU']].drop_duplicates().to_numpy().tolist())
        components = ['TRIGLY' if y==2005 else 'APOB','BPX','BMX','GHB',
                      'MCQ','SMQ','BPQ','DIQ','TCHOL','HDL','TRIGLY','BIOPRO','DR1TOT','GLU']
        if y<=2009:components.append('CRP')
        for base in components:
            a = read(f'{base}_{s}.xpt')
            d = d.join(a.drop(columns=d.columns.intersection(a.columns)), validate='one_to_one')
        rx = read(f'RXQ_RX_{s}.xpt')
        drugs = rx.RXDDRUG.map(lambda v: v.decode() if isinstance(v,bytes) else str(v)).str.upper()
        statin = drugs.str.contains('ATORVASTATIN|SIMVASTATIN|ROSUVASTATIN|PRAVASTATIN|LOVASTATIN|FLUVASTATIN|PITAVASTATIN')
        other = drugs.str.contains('EZETIMIBE|FENOFIBRATE|GEMFIBROZIL|COLESEVELAM|CHOLESTYRAMINE|COLESTIPOL')
        d['statin'] = statin.groupby(level=0).max().astype(float).reindex(d.index)
        d['otherlipid'] = other.groupby(level=0).max().astype(float).reindex(d.index)
        d['rxunknown'] = ~rx.RXDUSE.isin([1,2]).groupby(level=0).all().reindex(d.index)
        out = pd.DataFrame(index=d.index)
        out['cycle'], out['age'], out['male'] = y, d.RIDAGEYR, d.RIAGENDR.eq(1).astype(float)
        out['stratum'], out['psu'] = d.SDMVSTRA, d.SDMVPSU
        for name in ['WTSAF2YR','WTMEC2YR']: out[name] = d[name]
        out['apob'], out['apob_corrected'] = d.LBXAPB, harmonize_apob(d.LBXAPB,y)
        assert np.allclose(d.LBXAPB, d.LBDAPBSI*100, equal_nan=True, atol=1e-8)
        out['sbp'] = d[[f'BPXSY{i}' for i in [1,2,3]]].replace(0,np.nan).mean(axis=1)
        out['dbp'] = d[[f'BPXDI{i}' for i in [1,2,3]]].replace(0,np.nan).mean(axis=1)
        out['whr'], out['bmi'], out['hba1c'] = d.BMXWAIST/d.BMXHT, d.BMXBMI, d.LBXGH
        out['crp'] = d.LBXCRP*10 if 'LBXCRP' in d else np.nan
        out['white'] = d.RIDRETH1.eq(3).astype(float)
        out['smoking'] = d.SMQ020.map({1:1.,2:0.})
        out['education'] = d.DMDEDUC2.where(d.DMDEDUC2.isin([1,2,3,4,5])).map({1:0.,2:0.,3:1.,4:1.,5:1.})
        out['egfr'] = 175*d.LBXSCR**(-1.154)*d.RIDAGEYR**(-.203)*np.where(d.RIAGENDR==2,.742,1)*np.where(d.RIDRETH1==4,1.212,1)
        out['tg'], out['hdl'], out['ldl'], out['energy'] = d.LBXTR, d.LBDHDD, d.LBDLDL, d.DR1TKCAL
        # Questionnaire skips in non-diagnosed adults mean no reported medication.
        # Unknown/refused answers (7/9) remain missing for complete-case fits.
        for name, col in [('bpmed','BPQ050A'),('lipidmed','BPQ100D')]:
            out[name] = d[col].eq(1).astype(float).mask(d[col].isin([7,9]))
        out['dmmed'] = (d.DIQ050.eq(1)|d.DIQ080.eq(1)).astype(float).mask(d.DIQ050.isin([7,9])|d.DIQ080.isin([7,9]))
        out['hypertension'] = ((out.sbp>=140)|(out.dbp>=90)|d.BPQ020.eq(1)|(out.bpmed==1)).astype(float)
        out['diabetes'] = ((d.LBXGLU>=126)|(d.LBXGH>=6.5)|d.DIQ010.eq(1)|(out.dmmed==1)).astype(float)
        for name in ['statin','otherlipid','rxunknown']:out[name]=d[name]
        out['cvd'] = d[['MCQ160B','MCQ160C','MCQ160D','MCQ160E','MCQ160F']].eq(1).any(axis=1).astype(float)
        out['cancer'] = d.MCQ220.eq(1).astype(float)
        out['illness_unknown'] = ~d[['MCQ160B','MCQ160C','MCQ160D','MCQ160E','MCQ160F','MCQ220']].isin([1,2]).all(axis=1)
        paper_cvd = d[['MCQ160C','MCQ160D','MCQ160E','MCQ160F']]
        # This definition reconstructs the preprint flow and every quartet of
        # published counts. Codes 7/9 are retained here to reproduce its cohort,
        # not treated as verified absence of disease in sensitivity analyses.
        out['paper_no'] = paper_cvd.notna().all(axis=1)&~paper_cvd.eq(1).any(axis=1)
        out['paper_cvd_unknown'] = paper_cvd.isin([7,9]).any(axis=1)
        for cols, name in [(['MCQ160C','MCQ160E','MCQ160F'],'ascvd'),
                           (['MCQ160B','MCQ160C','MCQ160D','MCQ160E','MCQ160F'],'allcvd')]:
            out[name+'_no'] = d[cols].eq(2).all(axis=1)
            out[name+'_yes'] = d[cols].eq(1).any(axis=1)
        out['cancer_no'] = d.MCQ220.eq(2)
        out['pregnant'] = d.RIDEXPRG
        for v in [2015,2019]:
            path = new/f'NHANES_{y}_{y+1}_MORT_{v}_PUBLIC.dat'
            if not path.exists(): path=cache/'data'/path.name
            audit.append(dict(file=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
            m = mortality(path)
            for col in m:out[col+str(v)] = m[col].reindex(out.index)
        rows.append(out.reset_index())
    frame = pd.concat(rows, ignore_index=True)
    frame.to_csv(output/'observed.csv',index=False)
    save(output/'input-files.json',audit)
    save(output/'design-units.json',np.unique(units,axis=0))
    flow=[]
    for years in [YEARS,YEARS[1:]]:
        f=frame[(frame.age>=18)&frame.cycle.isin(years)]
        flow.append(dict(cycles=years,stage='age18',n=len(f)))
        f=f[f.apob.notna()];flow.append(dict(cycles=years,stage='measuredApoB',n=len(f)))
        f=f[(f.eligible2015==1)&f.event2015.isin([0,1])&f.examtime2015.notna()]
        flow.append(dict(cycles=years,stage='linked2015',n=len(f)))
        for defn in ['paper','ascvd','allcvd']:
            g=f[f[defn+'_no']&f.cancer_no].copy()
            g['event'],g['time'],g['cause']=g.event2015,g.inttime2015,g.cause2015
            flow.append(dict(cycles=years,stage=defn+'Exclusions',**counts(g),
                cardiovascularExclusions=int((~f[defn+'_no']).sum()),
                cancerExclusions=int((f[defn+'_no']&~f.cancer_no).sum()),
                men=int(g.male.sum()), ageSD=float(g.age.std()), apoBSD=float(g.apob.std()),
                quartiles=g.groupby(pd.cut(g.apob,[0,76,92,110,1000]),observed=True).agg(
                    n=('event','size'),deaths=('event','sum')).to_dict('list')))
            g[['cycle','SEQN']].to_csv(output/f'participants-{years[0]}-{defn}.csv',index=False)
    save(output/'cohort-flow.json',flow)
    print('COHORT FLOW',json.dumps(clean(flow)),flush=True)
    if (cache/'harmonized.csv').exists():
        legacy=pd.read_csv(cache/'harmonized.csv')
        if 'apob_raw' in legacy:legacy['apob']=legacy.apob_raw
        legacy=legacy[legacy.cycle.isin(YEARS)].set_index(['cycle','SEQN'])
        independent=frame.set_index(['cycle','SEQN']).reindex(legacy.index)
        checks={}
        for col in ['age','male','stratum','psu','apob','sbp','dbp','whr','hba1c','crp','WTMEC2YR','WTSAF2YR','event','time']:
            other={'event':'event2019','time':'examtime2019'}.get(col,col)
            checks[col]=bool(np.allclose(legacy[col],independent[other],equal_nan=True,rtol=1e-10,atol=1e-9))
        save(output/'cache-readback.json',dict(n=len(legacy),checks=checks))
        if not all(checks.values()):raise ValueError('Independent source join differs from frozen cache: '+str(checks))
        tail=[]
        for label,features,age in [('Full, measured ApoB',CORE+['apob'],(18,49)),
                                   ('Observed lipid',CORE+['apob','crp'],(18,79))]:
            g=legacy[legacy.index.get_level_values('cycle').isin([2005,2007,2011])&legacy.age.between(*age)].dropna(subset=features)
            g=g[g.WTSAF2YR>0]
            for sex in [0,1]:
                h=g[(g.male==sex)&(g.apob>=160)]
                cause=independent.cause2019.reindex(h.index)
                tail.append(dict(cohort=label,sex=sex,n=len(h),deaths=int(h.event.sum()),
                    cardiovascularDeaths=int(((h.event==1)&cause.isin([1,5])).sum())))
        save(output/'lwc-tail-support.json',tail)


def cohort(raw, years=YEARS, vintage=2015, age=(18,85), healthy=True, time='inttime', apob='apob'):
    f=raw[raw.cycle.isin(years)&raw.age.between(*age)&(raw['eligible'+str(vintage)]==1)&
          raw['event'+str(vintage)].isin([0,1])&(raw[time+str(vintage)]>0)&raw[apob].notna()].copy()
    if healthy:f=f[f.paper_no&f.cancer_no]
    f['event'],f['time'],f['cause']=f['event'+str(vintage)],f[time+str(vintage)],f['cause'+str(vintage)]
    f['apob']=f[apob]
    return f.reset_index(drop=True)


def cox_fit(frame, covariates, units, weight='none', knots=(.1,.5,.9), log=False,
            ridge=0, sex=None, age_spline=False, outcome='all', quartile=False):
    # Use the survey frames for the included cycles, retaining both original
    # PSUs even when one contributes no complete cases in a sex/domain fit.
    units=np.asarray(units)
    units=units[np.isin(units[:,0],frame.stratum.unique())]
    if sex is not None:frame=frame[frame.male==sex].copy();covariates=[c for c in covariates if c!='male']
    f=frame.dropna(subset=covariates+['apob']).copy()
    if weight!='none':f=f[f[weight]>0]
    f=f.reset_index(drop=True)
    x = np.log(f.apob.to_numpy()) if log else f.apob.to_numpy()
    kk=np.quantile(x,knots)
    apo=rcs(x,kk) if not quartile else np.column_stack([((f.apob>lo)&(f.apob<=hi)).astype(float) for lo,hi in [(76,92),(92,110),(110,1000)]])
    cols=[apo]
    for c in covariates:
        z=f[c].to_numpy(dtype=float)
        cols.append(rcs(z,np.quantile(z,[.05,.35,.65,.95])) if c=='age' and age_spline else z[:,None])
    design_x=np.column_stack(cols)
    # Remove constant nuisance columns in sex/landmark subgroups.
    keep=design_x.std(axis=0)>1e-8
    if not keep[:apo.shape[1]].all():raise ValueError('Unidentified ApoB basis')
    design_x=design_x[:,keep]
    scale=design_x.std(axis=0);center=design_x.mean(axis=0)
    xx=(design_x-center)/scale
    weights=np.ones(len(f)) if weight=='none' else f[weight].to_numpy()
    event=f.event.to_numpy() if outcome=='all' else ((f.event==1)&f.cause.isin([1,5])).astype(float).to_numpy()
    penalties=np.zeros(xx.shape[1]);penalties[:apo.shape[1]]=ridge*len(f)*np.r_[1.,np.full(apo.shape[1]-1,4.)]
    fitter=Cox(xx,f.time.to_numpy(),event,weights,penalties)
    fit=fitter.fit(f,design_units=units)
    if not fit['converged']:raise RuntimeError(fit['message']+' '+str(fit['maxGradient']))
    def at(v):
        a=rcs(np.log(v) if log else v,kk) if not quartile else np.column_stack([((np.asarray(v)>lo)&(np.asarray(v)<=hi)).astype(float) for lo,hi in [(76,92),(92,110),(110,1000)]])
        rows=np.zeros((len(a),len(scale)));rows[:,:a.shape[1]]=a/scale[:a.shape[1]]
        return rows
    deltas=at(GRID)-at([108.])
    curves=[contrast(fit,row,survey=(weight!='none')) for row in deltas]
    contrasts={f'{hi}vs{lo}':contrast(fit,(at([hi])-at([lo]))[0],survey=(weight!='none')) for lo,hi in [(85,125),(108,160),(108,200),(50,108)]}
    if quartile:
        contrasts.update({f'Q{i+2}vsQ1':contrast(fit,(at([v])-at([60.]))[0],survey=(weight!='none')) for i,v in enumerate([84.,100.,125.])})
    return dict(cohort=counts(f), outcome=outcome, outcomeEvents=int(event.sum()),
                covariates=covariates, weight=weight, knotProbabilities=knots, knots=kk,
                logApoB=log, ridge=ridge, sex=sex, ageSpline=age_spline, quartile=quartile,
                fit=fit, contrasts=contrasts, grid=GRID, curve=curves,
                minimumInGrid=float(GRID[np.argmin([c['hr'] for c in curves])]),
                uncertainty='NHANES PSU sandwich centered within strata; fixed knots; t interval' if weight!='none' else 'Model-based Cox Wald interval; fixed knots')


def observed(output):
    raw=pd.read_csv(output/'observed.csv')
    units=json.loads((output/'design-units.json').read_text())
    primary=cohort(raw)
    literal=cohort(raw,years=YEARS[1:])
    results={}
    def run(name,frame,covs=PAPER_COVARIATES,**kwargs):
        try:
            r=cox_fit(frame,covs,units,**kwargs)
            results[name]=r
            print(name,r['cohort']['n'],r['outcomeEvents'],r['minimumInGrid'],
                  clean(r['contrasts']),flush=True)
        except Exception as e:
            results[name]=dict(error=repr(e),cohort=counts(frame))
            print(name,'FAILED',repr(e),flush=True)
        save(output/'cox-results.json',results)
    # All specifications are reported; none is selected by the shape it produces.
    run('reconstructed-unadjusted',primary,[])
    run('reconstructed-demographic',primary,['age','male','white'])
    run('reconstructed-paper3',primary)
    run('literal-2007-2014-paper3',literal)
    run('reconstructed-paper3-4knots',primary,knots=(.05,.35,.65,.95))
    run('reconstructed-paper3-5knots',primary,knots=(.05,.275,.5,.725,.95))
    run('reconstructed-paper3-log',primary,log=True)
    run('reconstructed-paper3-age-spline',primary,age_spline=True)
    for name,covs in [('none',[]),('demographic',['age','male','white']),('full',PAPER_COVARIATES)]:
        run('quartiles-'+name,primary,covs,quartile=True)
    run('reconstructed-cardiovascular',primary,outcome='cvd')
    fixed=primary.dropna(subset=PAPER_COVARIATES)
    fixed=fixed[fixed.WTSAF2YR>0]
    for w in ['none','WTMEC2YR','WTSAF2YR']:
        run('same-participants-weight-'+w,fixed,weight=w)
    run('same-participants-no-age',fixed,[c for c in PAPER_COVARIATES if c!='age'],weight='WTSAF2YR')
    run('age18-49-paper3',cohort(raw,age=(18,49)),weight='WTSAF2YR')
    run('age18-79-paper3',cohort(raw,age=(18,79)),weight='WTSAF2YR')
    run('age20-85-paper3',cohort(raw,age=(20,85)),weight='WTSAF2YR')
    run('exam-origin-paper3',cohort(raw,time='examtime'))
    run('all-illness-included-paper3',cohort(raw,healthy=False),weight='WTSAF2YR')
    common=cohort(raw,healthy=False).dropna(subset=PAPER_COVARIATES+['cvd','cancer','illness_unknown','statin','otherlipid'])
    run('same-illness-cohort-unadjusted-status',common,weight='WTSAF2YR')
    run('same-illness-cohort-adjusted-status',common,PAPER_COVARIATES+['cvd','cancer','illness_unknown'],weight='WTSAF2YR')
    run('same-illness-cohort-adjusted-statin',common,PAPER_COVARIATES+['cvd','cancer','illness_unknown','statin','otherlipid'],weight='WTSAF2YR')
    run('no-lipid-treatment',primary[(primary.lipidmed==0)&(primary.statin==0)&(primary.otherlipid==0)&~primary.rxunknown],weight='WTSAF2YR')
    run('strict-cvd-codes',primary[~primary.paper_cvd_unknown],weight='WTSAF2YR')
    run('strict-five-cvd-conditions',primary[primary.allcvd_no],weight='WTSAF2YR')
    run('paper3-no-med-adjustment-same-cases',fixed,[c for c in PAPER_COVARIATES if c not in ['bpmed','dmmed','lipidmed']],weight='WTSAF2YR')
    run('corrected-assay-paper3',cohort(raw,apob='apob_corrected'))
    # Freeze 2015 membership when substituting the actual 2019 linkage.
    later=cohort(raw,vintage=2019)
    keys=primary[['cycle','SEQN']]
    later=later.merge(keys,on=['cycle','SEQN'],validate='one_to_one')
    run('same-cohort-linkage2019',later)
    run('same-cohort-linkage2019-weighted',later,weight='WTSAF2YR')
    for s in [0,1]:run('sex-'+str(s)+'-paper3',primary,weight='WTSAF2YR',sex=s)
    for l in [1,2,5]:
        run('landmark2015-'+str(l),landmark(primary,l),weight='WTSAF2YR')
        run('landmark2019-'+str(l),landmark(later,l),weight='WTSAF2YR')
    correlated=cohort(raw,vintage=2019).dropna(subset=PAPER_COVARIATES+CORE+['crp'])
    run('correlated-common-paper3',correlated,weight='WTSAF2YR')
    run('correlated-common-plus-core',correlated,PAPER_COVARIATES+['dbp','whr','hba1c','crp'],weight='WTSAF2YR')
    run('correlated-common-plus-core-no-age',correlated,[c for c in PAPER_COVARIATES if c!='age']+['dbp','whr','hba1c','crp'],weight='WTSAF2YR')
    for label,frame in [('reconstructed2015',primary),('literal2015',literal),('reconstructed2019',later)]:
        table=[]
        for s in ['all',0,1]:
            f=frame if s=='all' else frame[frame.male==s]
            for lo,hi in [(0,60),(60,85),(85,108),(108,130),(130,160),(160,200),(200,1000)]:
                g=f[(f.apob>lo)&(f.apob<=hi)]
                table.append(dict(sex=s,lower=lo,upper=hi,n=len(g),deaths=int(g.event.sum()),
                    cardiovascularDeaths=int(((g.event==1)&g.cause.isin([1,5])).sum()),personYears=float(g.time.sum()/12),
                    meanAge=float(g.age.mean()),lipidTreatment=float(g.lipidmed.mean()),
                    statin=float(g.statin.mean()),diabetes=float(g.diabetes.mean())))
        save(output/(label+'-events.json'),table)
    return results


def lwc(cache, output):
    bundle=json.loads((output/'data/public-model.json').read_text())
    full=bundle['full'];panels=bundle['panels']
    config=saved_reference(cache,'frozen-config.json','config')
    data=pd.read_csv(cache/'harmonized.csv')
    # The public snapshot predates assay harmonization. Replay its raw scale
    # explicitly if the cache has subsequently been regenerated by prepare.py.
    if 'apob_raw' in data:data['apob']=data.apob_raw
    provenance=dict(modelVersion=bundle['modelVersion'], harmonizedSha256=hashlib.sha256((cache/'harmonized.csv').read_bytes()).hexdigest(),
        publicModelSha256=hashlib.sha256((output/'data/public-model.json').read_bytes()).hexdigest(),
        config=config, sourceSha256={n:hashlib.sha256((Path(__file__).parent/n).read_bytes()).hexdigest()
            for n in ['model.py','train.py','prepare.py','apob_audit.py','apob_cox.py']})
    for name,m in [('full',full),('lipid',panels['lipid'])]:
        saved=saved_reference(cache,'full-model.json' if name=='full' else 'panel-models.json','full' if name=='full' else 'panels')
        saved=saved if name=='full' else saved[name]
        assert saved['coefficients']==m['coefficients'] and saved['curves']==m['curves']
        if name=='full':m['distributions']=saved['distributions']
    save(output/'provenance.json',provenance)
    dev=data[data.cycle.isin(config['development'])].dropna(subset=CORE).copy()
    dev=dev[survey_weights(dev)>0]
    result={}
    def component(coef,model):
        spec=model['curves']['apob']
        x=np.log(GRID);ref=np.log(108.)
        z=(x-ref)/spec['scale'];nz=(cubic(x,spec['knots'])-cubic(ref,spec['knots']))/spec['nonlinearScale']
        return {str(s):np.exp(z*(coef['apob_linear']+coef['apob_sex']*(s-.5))+
            (nz*coef.get('apob_nonlinear',0) if model['smooth'] else 0)) for s in [0,1]}
    def record(name,frame,m,fit=None,names=None,age=False):
        coef=m['coefficients'] if fit is None else dict(zip(names,fit.coefficients))
        curves=component(coef,m)
        result[name]=dict(cohort=counts(frame), coefficients=coef, curves=curves, grid=GRID,
            ageAdjustedDiagnostic=age, integrated=(len(m['features'])==len(FEATURES)),
            converged=True if fit is None else fit.converged,
            message='Frozen public fit' if fit is None else fit.message,
            hr125vs85={str(s):float(curves[str(s)][np.where(GRID==125)[0][0]]/curves[str(s)][np.where(GRID==85)[0][0]]) for s in [0,1]})
        save(output/'lwc-results.json',result)
        print('LWC',name,counts(frame)['n'],counts(frame)['deaths'],result[name]['hr125vs85'],flush=True)
    def fit_one(name,frame,m,marker=None,age=False,corrected=False,cutoff=0,censor=None):
        f=frame.copy();f=f[survey_weights(f,marker)>0].reset_index(drop=True)
        if cutoff:f=landmark(f,cutoff)
        if censor:
            f['event']=f.event*((f.time<=censor*12).astype(int));f['time']=np.minimum(f.time,censor*12)
        mm=json.loads(json.dumps(m))
        if corrected:
            f['apob']=harmonize_apob(f.apob,f.cycle)
            mm['curves']=learn_curves(f,m['features'])
            if len(m['features'])==len(FEATURES):mm['distributions']=learn_distributions(f,mm['curves'])
        v=(integration_values(f,mm['curves'],mm['distributions'],32,config['seed']) if len(m['features'])==len(FEATURES)
           else np.column_stack([transform(k,f[k]) for k in m['features']])[:,None,:])
        x,names,pen=design(f,v,m['features'],mm['curves'],m['smooth'])
        initial=np.r_[[m['coefficients'][n] for n in names],m['gamma']]
        if age:
            z=(f.age.to_numpy()-45)/10;sex=f.male.to_numpy()
            x=np.concatenate([x,(z*(1-sex))[:,None,None],(z*sex)[:,None,None]],axis=2)
            names+=['research_age_female','research_age_male'];pen=np.r_[pen,0,0]
            initial=np.r_[initial[:-2],.8,.8,initial[-2:]]
        fit=fit_survival(x,f,survey_weights(f,marker),pen,m['ridge'],initial=initial,maxiter=500)
        record(name,f,mm,fit,names,age)
    for label,m in [('full',full),('lipid',panels['lipid']),('strength',panels['strength'])]:
        f=dev if label=='full' else dev.dropna(subset=m['features'])
        f=f[f.age.between(*m['trainingAgeRange'])]
        marker=None if label=='full' else 'apob'
        f=f[survey_weights(f,marker)>0].reset_index(drop=True)
        assert len(f)==m['development']['n'] and int(f.event.sum())==m['development']['deaths']
        record(label+'-public',f,m)
        for l in [1,2,5]:fit_one(label+'-landmark-'+str(l),f,m,marker,cutoff=l)
        if label in ['full','lipid']:
            fit_one(label+'-corrected-assay',f,m,marker,corrected=True)
            fit_one(label+'-five-year-censored',f,m,marker,censor=5)
        if label=='lipid':
            fit_one('lipid-age-adjusted',f,m,marker,age=True)
            fit_one('lipid-age18-49',f[f.age<=49],m,marker)
            units=pd.read_csv(output/'observed.csv')[['stratum','psu']].drop_duplicates().to_numpy()
            # Exact observed lipid participant set: compare single-marker and
            # correlated-core fits using Cox before changing cohorts or horizons.
            simple=cox_fit(f,['male'],units,weight='WTSAF2YR')
            core=cox_fit(f,CORE+['male','crp'],units,weight='WTSAF2YR',log=True,ridge=.001)
            results=json.loads((output/'cox-results.json').read_text())
            results['lwc-lipid-same-cohort-single-marker']=simple
            results['lwc-lipid-same-cohort-conditional']=core
            save(output/'cox-results.json',results)
        if label=='full':
            f['apoObserved']=f.apob.notna()
            groups=[]
            for observed in [False,True]:
                g=f[f.apoObserved==observed]
                groups.append(dict(apobObserved=observed,n=len(g),deaths=int(g.event.sum())))
            save(output/'full-observed-apob.json',groups)
            bootstrap=saved_reference(cache,'bootstrap.json','bootstrap')
            ratios=[]
            for b in bootstrap['fits']:
                if b['fit']['converged']:
                    curves=component(b['fit']['coefficients'],m)
                    ratios.append([curves[str(s)][90]/curves[str(s)][50] for s in [0,1]])
            save(output/'full-fixed-nuisance-bootstrap.json',dict(n=len(ratios),
                hr125vs85Interval80=np.quantile(ratios,[.1,.9],axis=0),
                caveat='Existing 32 replicate survey cluster bootstrap; fixed spline/distribution/selection; 80 percent interval'))
            sensitivity=saved_reference(cache,'sensitivity.json','sensitivity')
            save(output/'integration-assumptions.json',dict(observedPairs=m['distributions']['observedPairs'],
                shrinkage=m['distributions']['shrinkage'],
                dependence=[dict(correlation=z['correlation'],apobCoefficients={k:v for k,v in z['fit']['coefficients'].items() if 'apob' in k},converged=z['fit']['converged']) for z in sensitivity['dependence']]))
    return result


def controlled(cache,output):
    """Change the risk model while freezing the observed lipid participants."""
    bundle=json.loads((output/'data/public-model.json').read_text())
    m=bundle['panels']['lipid']
    data=pd.read_csv(cache/'harmonized.csv')
    if 'apob_raw' in data:data['apob']=data.apob_raw
    config=saved_reference(cache,'frozen-config.json','config')
    f=data[data.cycle.isin(config['development'])&data.age.between(*m['trainingAgeRange'])].dropna(subset=m['features'])
    f=f[survey_weights(f,'apob')>0].reset_index(drop=True)
    units=json.loads((output/'design-units.json').read_text())
    units=np.asarray(units);units=units[np.isin(units[:,0],f.stratum.unique())]
    results={}
    for smooth in [False,True]:
        mm=dict(m,smooth=smooth)
        values=np.column_stack([transform(k,f[k]) for k in m['features']])[:,None,:]
        xx,names,pen=design(f,values,m['features'],mm['curves'],smooth)
        for ridge in [0.,.0003,.001,.003]:
            name=f'cox-original-basis-smooth{smooth}-ridge{ridge}'
            fit=Cox(xx[:,0,1:],f.time,f.event,survey_weights(f,'apob'),pen[1:]*ridge*len(f)).fit(f,design_units=units)
            if not fit['converged']:raise RuntimeError(f'{name}: {fit["message"]}')
            spec=m['curves']['apob']
            def delta(values,sex):
                logratio=(np.log(values)-np.log(108.))/spec['scale']
                rows=np.zeros((len(values),len(names)-1))
                rows[:,names[1:].index('apob_linear')]=logratio
                rows[:,names[1:].index('apob_sex')]=logratio*(sex-.5)
                if smooth:rows[:,names[1:].index('apob_nonlinear')]=(cubic(np.log(values),spec['knots'])-cubic(np.log(108.),spec['knots']))/spec['nonlinearScale']
                return rows
            curves={str(s):[contrast(fit,row) for row in delta(GRID,s)] for s in [0,1]}
            contrasts={str(s):contrast(fit,(delta([125.],s)-delta([85.],s))[0]) for s in [0,1]}
            results[name]=dict(cohort=counts(f),features=m['features'],smooth=smooth,ridge=ridge,
                uncertainty='Survey PSU sandwich; fixed original spline basis; t interval',
                grid=GRID,curves=curves,contrasts=contrasts,fit=fit)
            print('CONTROLLED',name,clean(contrasts),flush=True)
        for ridge in [0.,.0003,.001,.003]:
            name=f'gompertz-original-basis-smooth{smooth}-ridge{ridge}'
            initial=np.r_[[m['coefficients'][n] for n in names],m['gamma']]
            fit=fit_survival(xx,f,survey_weights(f,'apob'),pen,ridge,initial=initial,maxiter=500)
            coef=dict(zip(names,fit.coefficients))
            spec=m['curves']['apob']
            z=(np.log(GRID)-np.log(108.))/spec['scale']
            nz=(cubic(np.log(GRID),spec['knots'])-cubic(np.log(108.),spec['knots']))/spec['nonlinearScale']
            curves={str(s):np.exp(z*(coef['apob_linear']+coef['apob_sex']*(s-.5))+
                nz*coef.get('apob_nonlinear',0)) for s in [0,1]}
            contrasts={str(s):float(curves[str(s)][90]/curves[str(s)][50]) for s in [0,1]}
            results[name]=dict(cohort=counts(f),features=m['features'],smooth=smooth,ridge=ridge,
                grid=GRID,curves=curves,contrasts=contrasts,coefficients=coef,
                converged=fit.converged,message=fit.message,uncertainty='Point estimate only')
            print('CONTROLLED',name,contrasts,'converged',fit.converged,flush=True)
    # Measured ApoB subset of the full model: no integration is needed here.
    young=data[data.cycle.isin(config['development'])&data.age.between(18,49)].dropna(subset=CORE+['apob'])
    young=young[survey_weights(young,'apob')>0].reset_index(drop=True)
    cox_results=json.loads((output/'cox-results.json').read_text())
    for label,covs in [('single-marker',['male']),('core',CORE+['male']),('core-age',CORE+['male','age'])]:
        cox_results['full-measured-apob-'+label]=cox_fit(young,covs,units=json.loads((output/'design-units.json').read_text()),weight='WTSAF2YR',log=True)
    save(output/'cox-results.json',cox_results)
    save(output/'controlled-results.json',results)
    conditional(cache,output)


def conditional(cache,output):
    """Separate transformation, biomarker adjustment and ApoB-only penalties."""
    bundle=json.loads((output/'data/public-model.json').read_text())
    m=bundle['panels']['lipid']
    data=pd.read_csv(cache/'harmonized.csv')
    if 'apob_raw' in data:data['apob']=data.apob_raw
    config=saved_reference(cache,'frozen-config.json','config')
    f=data[data.cycle.isin(config['development'])&data.age.between(*m['trainingAgeRange'])].dropna(subset=m['features'])
    f=f[survey_weights(f,'apob')>0].reset_index(drop=True)
    units=json.loads((output/'design-units.json').read_text())
    results=json.loads((output/'cox-results.json').read_text())
    for label,covs,log in [('single-marker-log',['male'],True),
                           ('conditional-raw',CORE+['male','crp'],False),
                           ('conditional-log-unpenalized',CORE+['male','crp'],True)]:
        name='lwc-lipid-same-cohort-'+label
        r=cox_fit(f,covs,units,weight='WTSAF2YR',log=log)
        results[name]=r
        print('CONDITIONAL',name,clean(r['contrasts']),flush=True)
    save(output/'cox-results.json',results)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--cache',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--phase',choices=['prepare','observed','lwc','controlled','conditional','all'],default='all')
    args=p.parse_args();args.output=args.output.resolve()
    if '.artifacts' not in args.output.parts:raise ValueError('Participant output must be in ignored .artifacts')
    args.output.mkdir(parents=True,exist_ok=True)
    warnings.filterwarnings('ignore',category=RuntimeWarning)
    if args.phase in ['prepare','all']:prepare(args.cache,args.output)
    if args.phase in ['observed','all']:observed(args.output)
    if args.phase in ['lwc','all']:lwc(args.cache,args.output)
    if args.phase in ['controlled','all']:controlled(args.cache,args.output)
    if args.phase=='conditional':conditional(args.cache,args.output)


if __name__=='__main__':main()
