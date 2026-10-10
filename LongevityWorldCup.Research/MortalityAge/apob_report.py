"""Render the actual ApoB audit estimates and aggregate comparison tables."""
from pathlib import Path
import argparse
import html
import hashlib
import json
import numpy as np
import pandas as pd
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.ticker import NullLocator
from apob_audit import clean, cohort, YEARS
from model import cubic

COLORS={'0':'#c05c85','1':'#356eaa'}
SEX={'0':'Women','1':'Men'}


def read(root,name):return json.loads((root/name).read_text())


def interval(c):return f"{c['hr']:.2f} ({c['low']:.2f}–{c['high']:.2f})"


def table(headers,rows):
    return '\n'.join(['| '+' | '.join(headers)+' |','| '+' | '.join(['---']*len(headers))+' |']+
                     ['| '+' | '.join(map(str,row))+' |' for row in rows])


def configure(ax,title):
    ax.set_title(title,loc='left',fontsize=12,pad=13)
    ax.set_xlabel('ApoB (mg/dL)')
    ax.axhline(1,color='#a4abb4',lw=.9,zorder=0)
    ax.axvline(108,color='#ccd0d5',lw=.8,ls=':',zorder=0)
    ax.set_xlim(40,200)
    ax.set_yscale('log')
    ax.set_ylim(.25,4)
    ax.set_yticks([.25,.5,1,2,4],labels=['0.25','0.5','1','2','4'])
    ax.yaxis.set_minor_locator(NullLocator())
    ax.spines[['top','right']].set_visible(False)
    ax.grid(axis='y',color='#e7eaee',lw=.6)


def observed_line(ax,result,label,color,band=True,ls='-'):
    grid=np.array(result['grid']);curves=result['curve']
    ax.plot(grid,[c['hr'] for c in curves],color=color,label=label,lw=2,ls=ls)
    if band:ax.fill_between(grid,[c['low'] for c in curves],[c['high'] for c in curves],color=color,alpha=.13,lw=0)


def component(coef,spec,sex,grid):
    z=(np.log(grid)-np.log(108.))/spec['scale']
    nz=(cubic(np.log(grid),spec['knots'])-cubic(np.log(108.),spec['knots']))/spec['nonlinearScale']
    return np.exp(z*(coef['apob_linear']+coef['apob_sex']*(sex-.5))+nz*coef.get('apob_nonlinear',0))


def publish_figure(fig,root,name):
    fig.savefig(root/(name+'.png'),dpi=180,facecolor='white')
    svg=root/(name+'.svg')
    fig.savefig(svg,facecolor='white',metadata={'Date':None})
    svg.write_text('\n'.join(line.rstrip() for line in svg.read_text(encoding='utf-8').splitlines())+'\n',encoding='utf-8',newline='\n')
    plt.close(fig)


def render(output):
    cox=read(output,'cox-results.json');lwc=read(output,'lwc-results.json')
    controlled=read(output,'controlled-results.json');bundle=read(output,'data/public-model.json')
    reference=json.loads(Path(__file__).with_name('apob-reference.json').read_text())
    plt.rcParams.update({'font.family':'DejaVu Sans','font.size':10,'axes.labelcolor':'#343c45',
        'text.color':'#202a36','axes.edgecolor':'#aab1b9','xtick.color':'#53606f','ytick.color':'#53606f',
        'svg.hashsalt':'apob-audit-20261010'})
    fig,axes=plt.subplots(1,3,figsize=(14.7,4.9))
    configure(axes[0],'Study reconstruction\n9,380 complete cases · 455 deaths')
    observed_line(axes[0],cox['reconstructed-paper3'],'Unweighted Cox · 95% interval','#263d50')
    observed_line(axes[0],cox['same-participants-weight-WTSAF2YR'],'Fasting weights · 95% interval','#bd7b28')
    axes[0].legend(frameon=False,loc='upper left',fontsize=8.5)
    for ax,label,title,m in [(axes[1],'full','LWC full\n12,645 participants · 475 deaths',bundle['full']),
                             (axes[2],'lipid','LWC observed lipid\n4,281 participants · 554 deaths',bundle['panels']['lipid'])]:
        configure(ax,title)
        result=lwc[label+'-public'];grid=np.array(result['grid'])
        for s in ['0','1']:
            curve=np.array(result['curves'][s]);lo,hi=m['curves']['apob']['support'][s]
            support=(grid>=lo)&(grid<=hi)
            ax.plot(grid,curve,color=COLORS[s],ls='--',lw=1.2,alpha=.6)
            ax.plot(grid[support],curve[support],color=COLORS[s],lw=2,label=SEX[s])
            if label=='full':
                samples=np.array([component(b['fit']['coefficients'],m['curves']['apob'],int(s),grid)
                    for b in reference['bootstrap']['fits'] if b['fit']['converged']])
                low,high=np.quantile(samples,[.1,.9],axis=0)
                ax.fill_between(grid[support],low[support],high[support],color=COLORS[s],alpha=.13,lw=0)
        if label=='full':
            ax.text(.03,.05,'Ages 18–49; ApoB observed in 3,718\n93 deaths with observed ApoB; band = 80%',transform=ax.transAxes,fontsize=8.5,color='#53606f')
        else:ax.text(.03,.05,'Ages 18–79; all ApoB observed\n2005–2008 cycles; point estimates only',transform=ax.transAxes,fontsize=8.5,color='#53606f')
        ax.legend(frameon=False,loc='upper right',fontsize=9)
    axes[0].set_ylabel('Relative hazard contribution (108 mg/dL = 1)')
    fig.suptitle('The U-shaped reconstruction and the current model use different populations and designs',x=.04,ha='left',fontsize=15,y=.99)
    fig.text(.04,.02,'Shading: fixed-model uncertainty, as labeled. Dashed LWC tails lie outside the sex-specific 1st–99th percentile support. No causal interpretation.',fontsize=9,color='#53606f')
    fig.subplots_adjust(top=.79,bottom=.17,left=.075,right=.985,wspace=.26)
    publish_figure(fig,output,'apob-comparison')

    fig,axes=plt.subplots(2,2,figsize=(11.8,8.5))
    specs=[('Weights: identical participants',[
        ('same-participants-weight-none','Unweighted','#263d50'),
        ('same-participants-weight-WTSAF2YR','Fasting weights','#bd7b28')]),
        ('Age range: fasting weights and same covariates',[
        ('same-participants-weight-WTSAF2YR','Ages 20–85','#263d50'),
        ('age18-49-paper3','Ages 20–49','#b75d65')]),
        ('Spline specification: identical participants',[
        ('reconstructed-paper3','3 knots, raw ApoB','#263d50'),
        ('reconstructed-paper3-5knots','5 knots, raw ApoB','#bd7b28'),
        ('reconstructed-paper3-log','3 knots, log ApoB','#438e85')]),
        ('Survivor landmarks: time begins at the landmark',[
        ('same-participants-weight-WTSAF2YR','Baseline','#263d50'),
        ('landmark2015-2','Survived >2 years','#bd7b28'),
        ('landmark2015-5','Survived >5 years','#438e85')])]
    for ax,(title,series) in zip(axes.flat,specs):
        configure(ax,title)
        for key,label,color in series:observed_line(ax,cox[key],label,color,band=len(series)==2)
        ax.legend(frameon=False,fontsize=8.5,loc='upper left')
        ax.set_ylabel('Relative hazard (108 mg/dL = 1)')
    fig.suptitle('Observed-data sensitivities: every curve is fitted to actual linked outcomes',x=.07,ha='left',fontsize=14)
    fig.text(.07,.015,'95% intervals shown in the top panels; fixed knots. Age-range comparison changes participants as well as age. All mortality here is through 2015.',fontsize=9,color='#53606f')
    fig.tight_layout(rect=(0,.035,1,.95),h_pad=2.3)
    publish_figure(fig,output,'apob-sensitivities')

    fig,axes=plt.subplots(1,2,figsize=(11.2,4.8))
    for ax,s in zip(axes,['0','1']):
        configure(ax,SEX[s]+': observed cohort with fasting weights')
        observed_line(ax,cox['sex-'+s+'-paper3'],'Sex-specific Cox · 95% interval',COLORS[s])
        ax.legend(frameon=False,fontsize=9)
        ax.set_ylabel('Relative hazard (108 mg/dL = 1)')
    fig.suptitle('Sex-specific high-ApoB estimates remain imprecise',x=.07,ha='left',fontsize=14)
    fig.tight_layout(rect=(0,.02,1,.93))
    publish_figure(fig,output,'apob-sex')

    fig,axes=plt.subplots(1,2,figsize=(11.5,4.9))
    for ax,title,first,second in [
        (axes[0],'Raw ApoB: only the biomarker adjustment changes',
         'lwc-lipid-same-cohort-single-marker','lwc-lipid-same-cohort-conditional-raw'),
        (axes[1],'Log ApoB: only the biomarker adjustment changes',
         'lwc-lipid-same-cohort-single-marker-log','lwc-lipid-same-cohort-conditional-log-unpenalized')]:
        configure(ax,title)
        observed_line(ax,cox[first],'ApoB + sex','#263d50')
        observed_line(ax,cox[second],'Add BP, waist/height, HbA1c, CRP','#438e85')
        ax.legend(frameon=False,fontsize=8.5,loc='upper left')
        ax.set_ylabel('Relative hazard (108 mg/dL = 1)')
    fig.suptitle('Joint biomarker adjustment changes the ApoB residual association',x=.07,ha='left',fontsize=14)
    fig.text(.07,.02,'Identical 4,281 lipid participants / 554 deaths, 2019 linkage, fasting weights, no age input, no penalty. Bands = survey 95% intervals; fixed knots.',fontsize=8.5,color='#53606f')
    fig.tight_layout(rect=(0,.035,1,.93))
    publish_figure(fig,output,'apob-conditioning')

    rows=[]
    for key,r in cox.items():
        if 'error' in r:rows.append([key,r['cohort']['n'],r['cohort']['deaths'],'ERROR',r['error'],'',''])
        else:rows.append([key,r['cohort']['n'],r['outcomeEvents'],r['weight'],r['minimumInGrid'],
                          interval(r['contrasts']['125vs85']),interval(r['contrasts']['160vs108'])])
    all_cox=table(['Specification','n','Events','Weights','Grid minimum','HR 125 vs 85 (95%)','HR 160 vs 108 (95%)'],rows)
    lwc_rows=[[key,r['cohort']['n'],r['cohort']['deaths'],f"{r['hr125vs85']['0']:.3f}",f"{r['hr125vs85']['1']:.3f}",r['converged']]
              for key,r in lwc.items()]
    all_lwc=table(['LWC specification','n','Deaths','Women HR 125 vs 85','Men HR 125 vs 85','Converged'],lwc_rows)
    control_rows=[]
    for key,r in controlled.items():
        vals=[interval(r['contrasts'][s]) if key.startswith('cox') else f"{r['contrasts'][s]:.3f}" for s in ['0','1']]
        control_rows.append([key,*vals])
    all_controls=table(['Same 4,281 lipid participants','Women HR 125 vs 85','Men HR 125 vs 85'],control_rows)
    integration_rows=[]
    integration_specs=[('32 draws, fitted/zero unknown covariance',{'coefficients':bundle['full']['coefficients'],'converged':True})]
    integration_specs.extend([(f"Requested unknown correlation {r['correlation']:+.1f}",r['fit']) for r in reference['sensitivity']['dependence']])
    integration_specs.append(('64 draws, fitted/zero unknown covariance',reference['sensitivity']['moreDraws']['fit']))
    for label,fit in integration_specs:
        ratios=[]
        for s in [0,1]:
            curve=component(fit['coefficients'],bundle['full']['curves']['apob'],s,np.arange(35.,211.))
            ratios.append(f'{curve[90]/curve[50]:.3f}')
        integration_rows.append([label,*ratios,fit['converged']])
    integration_table=table(['Saved full-model integration sensitivity','Women HR 125 vs 85','Men HR 125 vs 85','Converged'],integration_rows)
    flow=read(output,'cohort-flow.json')
    study=[r for r in flow if r['stage']=='paperExclusions' and r['cycles']==YEARS][0]
    quartile_table=table(['Quartile (mg/dL)','Published n / deaths','Reconstructed n / deaths'],
        [[label,f'{n} / {d}',f"{study['quartiles']['n'][i]} / {int(study['quartiles']['deaths'][i])}"]
         for i,(label,n,d) in enumerate([('≤76',2727,132),('>76–92',2581,136),('>92–110',2586,127),('>110',2481,138)])])
    raw=pd.read_csv(output/'observed.csv');primary=cohort(raw)
    support_rows=[]
    for s in [0,1]:
        g=primary[(primary.male==s)&(primary.apob>=160)]
        support_rows.append([SEX[str(s)],'Reconstructed 2015',len(g),int(g.event.sum()),int(((g.event==1)&g.cause.isin([1,5])).sum())])
    # Cached participant support is supplied as aggregates by the audit runner.
    if (output/'lwc-tail-support.json').exists():
        support_rows.extend([[SEX[str(r['sex'])],r['cohort'],r['n'],r['deaths'],r.get('cardiovascularDeaths','—')] for r in read(output,'lwc-tail-support.json')])
    tail_table=table(['Sex','Cohort','n with ApoB ≥160','Deaths','CVD deaths'],support_rows)
    summary=dict(cohortFlow=flow,publicModelVersion=bundle['modelVersion'],
        cox={k:{field:r[field] for field in ['cohort','outcome','outcomeEvents','covariates','weight','knots','logApoB','ridge','sex','ageSpline','quartile','contrasts','minimumInGrid','uncertainty','error'] if field in r} for k,r in cox.items()},
        lwc={k:{field:r[field] for field in ['cohort','coefficients','hr125vs85','ageAdjustedDiagnostic','integrated','converged','message']} for k,r in lwc.items()},
        controlled={k:{field:r[field] for field in ['cohort','features','smooth','ridge','contrasts','uncertainty','converged'] if field in r} for k,r in controlled.items()},
        inputManifest=read(output,'input-downloads.json'),cacheReadback=read(output,'cache-readback.json'),
        provenance=read(output,'provenance.json'),
        publishedSourceSha256={n:hashlib.sha256(Path(__file__).with_name(n).read_bytes()).hexdigest()
            for n in ['prepare.py','assays.py','apob_audit.py','apob_cox.py','apob_inputs.py','apob_report.py','test_apob_audit.py','apob-reference.json']},
        fullObservedApoB=read(output,'full-observed-apob.json'),
        fullBootstrap=read(output,'full-fixed-nuisance-bootstrap.json'),integration=read(output,'integration-assumptions.json'))
    summary['savedIntegrationContrasts']=integration_rows
    (output/'apob-audit-results.json').write_text(json.dumps(clean(summary),indent=2,allow_nan=False),encoding='utf-8',newline='\n')
    report=f'''# ApoB mortality audit — 10 October 2026

The paper's cohort and U-shaped all-cause association can be reconstructed. Age range and adjustment for correlated biomarkers are leading differences from the LWC model; sample weights and spline scale also change the high-ApoB estimate. Age adjustment alone, early-death removal, the Gompertz baseline, and ridge penalties do not explain away the LWC downward component. These associations do not establish a healthy ApoB target or a causal benefit of raising ApoB.

![Actual fitted comparisons](apob-comparison.png)

## Cohort reconstruction and published source

The [2023 journal abstract](https://doi.org/10.1016/j.amjms.2023.07.012) states NHANES 2007–2014, n=10,375, 533 all-cause deaths, 91 cardiovascular deaths, and a 108 mg/dL turning point. The [authors' 2021 preprint, full methods, flowchart and supplements](https://www.researchsquare.com/article/rs-1039792/v1) contain the same totals. The journal full text remained inaccessible, so the detailed reconstruction is of that preprint; final-version methodological identity is unverified.

The flowchart instead reconstructs **2005–2014**: 30,295 people aged ≥18; 16,967 missing ApoB; 13 missing 2015 linkage; 2,039 cardiovascular exclusions; 901 cancer exclusions; **10,375 retained, 533 deaths, 91 cardiovascular deaths**. The retained participants are aged ≥20 because of the adult medical questionnaire. The reconstructed sex counts (4,968 men / 5,407 women), age 46.30±16.87, ApoB 93.76±25.82, smoking and education counts, and all quartile counts/deaths also match. The implied CVD mask covers coronary heart disease, angina, heart attack and stroke; it drops missing questionnaire rows but retains refused/unknown codes. Stricter masks are separate sensitivities. These many matches are strong reconstruction evidence; the authors' participant IDs were unavailable.

{quartile_table}

The unweighted age/sex/race-adjusted third-vs-first-quartile HR is **0.711 (0.556–0.907)**, matching the abstract's **0.71** point estimate and closely reproducing its **0.55–0.91** interval. The preprint fully adjusted estimate is 0.73 (0.55–0.96); our complete-case implementation gives 0.743 (0.562–0.982), using 9,380 people and 455 deaths. Undocumented missing-data handling, exact eGFR/education coding, tie method and spline knots prevent claiming exact reproduction of every final coefficient. Three/four/five-knot raw-ApoB fits give minima of 103/110/115 mg/dL; no nadir was imposed.

The original figure below is Yan and colleagues' Figure 2 from [U-shaped Association Between Apolipoprotein B and All-Cause Mortality](https://www.researchsquare.com/article/rs-1039792/v1), 2021, licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), reproduced unchanged. It is not a digitized or reconstructed numerical curve.

![Yan and colleagues' original Figure 2, CC BY 4.0](data/study-curves.jpg)

## Controlled differences

On identical 9,380 complete cases, changing unweighted Cox to CDC fasting weights moves HR(160 vs 108) from **1.23 (0.86–1.77)** to **1.06 (0.61–1.85)**. MEC weights are a diagnostic only; [CDC specifies the fasting-subsample weights for ApoB](https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2013/DataFiles/APOB_H.htm). The correct weights retain a shallow U-shaped point estimate with weak high-tail evidence.

With the same covariates, fasting weights and 2015 mortality, restricting to ages 18–49 leaves **5,529 complete cases / 78 deaths** and HR(160 vs 108) **0.55 (0.23–1.31)**. Including ages 18–79 gives 9,047 / 319 and **1.22 (0.65–2.27)**. This changes the participant population as well as age; it is strong evidence that the young cohort can produce the reversal, not proof of a causal age interaction.

Changing raw ApoB to log ApoB on the same complete cases reduces the unweighted high-tail HR(200 vs 108) from **1.47 (0.77–2.82)** to **1.08 (0.67–1.76)**. Additional knots strengthen the raw-scale upturn but widen its interval. Calendar period, actual 2015 vs 2019 linkage, interview vs examination time origin, medication adjustment, exclusion of lipid treatment, CVD/cancer inclusion and correlated core biomarkers are all reported below, including weak/null estimates.

![Observed-data design sensitivities](apob-sensitivities.png)

Holding the exact 4,281 lipid-panel participants, fasting weights, 2019 linkage, no age input and no penalty fixed, the **log-ApoB + sex** fit gives HR(125 vs 85) **1.033 (0.859–1.242)** and HR(160 vs 108) **1.199 (0.890–1.615)**. Adding only SBP, DBP, waist/height, HbA1c and CRP changes these to **0.823 (0.682–0.994)** and **0.910 (0.682–1.215)**. With raw ApoB, the same adjustment weakens the high tail from **1.301 (0.977–1.732)** to **1.033 (0.755–1.413)**. Transformation, adjustment and penalty are separate rows below. A joint model's ApoB component is a residual association conditional on the other biomarkers; it does not represent the total biological effect of ApoB.

![Identical-participant biomarker adjustment comparisons](apob-conditioning.png)

The frozen public model is **{bundle['modelVersion']}**, SHA256 `{reference['publicModelSha256']}`. The full risk model uses no chronological age, ages 18–49, survey weights, a sex-specific Gompertz baseline, log ApoB, restricted cubic biomarker terms and penalized sex-specific slopes. Its 12,645 participants have 475 deaths, but only **3,718 participants / 93 deaths** have measured ApoB. It integrates missing measurements over fitted distributions. ApoB/cystatin and ApoB/VO2 have zero jointly measured participants; unknown residual correlations are assumptions. Its observed lipid panel has **4,281 / 554**, ages 18–79, ApoB+CRP from 2005–2008 development cycles, and does not integrate missing ApoB. The fact that this observed panel also slopes down means integration alone cannot explain the result.

On the exact lipid participants and original biomarker/sex spline basis, replacing Gompertz with weighted Cox still slopes down. With smooth terms and no ridge, HR(125 vs 85) is **0.840 (0.616–1.144)** in women and **0.758 (0.595–0.965)** in men. The original Gompertz smooth fit is 0.814 / 0.763; adding sex-specific age slopes gives **0.852 / 0.822**. Penalties from 0 to 0.003 and linear/smooth forms do not restore a high-ApoB upturn. Cox intervals are conditional on fixed knots and penalty selection; penalized intervals do not remove shrinkage bias. Gompertz sensitivities are point estimates only.

The original saved full-model fits also vary the assumed unobserved residual correlations and double integration draws. The table reuses those converged reference fits; it does not treat integration draws as independent people. Correlation scenarios may be shrunk to a positive-definite covariance matrix. None of these tested scenarios restores the upturn, but unknown biomarker relationships and absent joint validation remain material limitations.

{integration_table}

## Survivor landmarks and sex support

Every landmark keeps `follow-up > L`, starts time at `follow-up − L`, and counts only subsequent deaths. A participant censored at or before L is excluded. LWC full HR(125 vs 85), women/men: baseline 0.732/0.630; one year 0.714/0.623; two years 0.678/0.581; five years 0.767/0.667. The 2015 reconstructed weighted cohort retains shallow U-shaped point estimates after all three landmarks, but high-tail intervals widen. The five-year landmark also changes represented NHANES cycles because recent participants have insufficient follow-up. Landmark selection and baseline biomarker aging remain limitations.

{tail_table}

In the 2015 weighted complete-case fits, HR(160 vs 108) is **1.37 (0.62–3.05)** for women and **0.70 (0.40–1.23)** for men. Opposite point estimates with these intervals do not establish a sex interaction. The full model's existing 32-replicate fixed-nuisance bootstrap gives **80%**, not 95%, intervals for HR(125 vs 85): women **0.664–0.858**, men **0.574–0.721**. It holds model selection, spline/distribution fitting and the risk-age ruler fixed, so it understates total model-development uncertainty.

![Sex-specific fitted estimates and uncertainty](apob-sex.png)

## Demonstrated preparation bug

The source preparation code pooled raw 2005–2006 BN100 ApoB with later BN ProSpec results. [CDC's paired-assay Deming equation is `ProSpec = 0.923 × BN100`](https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2007/DataFiles/APOB_E.htm). `assays.py` now applies that conversion once to raw mg/dL measurements; `prepare.py` preserves `apob_raw`, uses the calibrated value, and writes the assay provenance. The correction keeps the full and observed lipid components downward; lipid HR(125 vs 85) changes from 0.814/0.763 to **0.864/0.808**, with its curves refitted. It is a real input-preparation fix, not a way to force a U.

The public calculator and exported model have not been replaced. A defensible age-free replacement requires a new frozen development plan, correct assay inputs, survey-aware uncertainty and independent validation; this age-adjusted reproduction is a diagnostic.

## Reproduce

Install `requirements.txt` in a virtual environment. From the repository root:

```powershell
python LongevityWorldCup.Research/MortalityAge/prepare.py
python LongevityWorldCup.Research/MortalityAge/apob_inputs.py --cache .artifacts/mortality-age --output .artifacts/apob-audit
python LongevityWorldCup.Research/MortalityAge/apob_audit.py --cache .artifacts/mortality-age --output .artifacts/apob-audit --phase all
python LongevityWorldCup.Research/MortalityAge/apob_report.py --output .artifacts/apob-audit
python -m unittest discover -s LongevityWorldCup.Research/MortalityAge -p "test_*.py"
```

`apob-reference.json` preserves aggregate frozen parameters/distributions/configuration so a fresh preparation run can replay the historical public fit. The audit explicitly uses raw ApoB for that replay, then separately applies the calibration. Inputs use CDC transport files and actual archived CDC 2015 mortality files; file-type checks, respondent-key joins and SHA256 manifests verify completion. Raw participant rows and participant-key manifests remain under ignored `.artifacts`; committed results contain aggregates only. The custom Breslow implementation is checked against explicit weighted risk sets, finite-difference gradient/Hessian, score sums and an independent lifelines fit; landmark and assay tests also pass. This audit is exploratory reuse of NHANES, not new external validation.

## All observed-data specifications

The displayed minimum is within 35–210 mg/dL; an endpoint minimum indicates a downward/upward boundary, not a discovered physiological optimum. Exposure grids beyond measured support are extrapolations. The 2015 and 2019 outcomes are separate linkage versions; no artificial truncation of 2019 follow-up stands in for the 2015 files. Ordinary Cox intervals are model-based; weighted intervals use strata/PSU sandwich variance, fixed knots and survey t degrees of freedom. Cardiovascular fits censor other-cause deaths and are cause-specific hazards.

{all_cox}

## All frozen-model sensitivities

{all_lwc}

## Identical-cohort baseline and penalty comparisons

{all_controls}
'''
    (output/'apob-audit.md').write_text(report,encoding='utf-8',newline='\n')
    # Simple readable local report; no external resources or participant records.
    import re
    parts=[];lines=report.splitlines();i=0
    while i<len(lines):
        line=lines[i]
        if line.startswith('|'):
            block=[]
            while i<len(lines) and lines[i].startswith('|'):block.append(lines[i]);i+=1
            parts.append('<div class="table"><table>'+''.join('<tr>'+''.join(f'<{"th" if j==0 else "td"}>{html.escape(c.strip())}</{"th" if j==0 else "td"}>' for c in row.strip('|').split('|'))+'</tr>' for j,row in enumerate(block) if j!=1)+'</table></div>');continue
        if line.startswith('```'):
            i+=1;code=[]
            while i<len(lines) and not lines[i].startswith('```'):code.append(lines[i]);i+=1
            parts.append('<pre>'+html.escape('\n'.join(code))+'</pre>')
        elif line.startswith('#'):
            n=len(line)-len(line.lstrip('#'));parts.append(f'<h{n}>{html.escape(line[n:].strip())}</h{n}>')
        elif line.startswith('!['):
            match=re.match(r'!\[(.*?)\]\((.*?)\)',line)
            parts.append(f'<img alt="{html.escape(match[1])}" src="{html.escape(match[2])}">')
        elif line.strip():
            escaped=html.escape(line)
            escaped=re.sub(r'\*\*(.*?)\*\*',r'<strong>\1</strong>',escaped)
            escaped=re.sub(r'`(.*?)`',r'<code>\1</code>',escaped)
            escaped=re.sub(r'\[(.*?)\]\((https://.*?)\)',r'<a href="\2">\1</a>',escaped)
            parts.append('<p>'+escaped+'</p>')
        i+=1
    doc='<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>ApoB mortality audit</title><style>body{max-width:1200px;margin:40px auto;padding:0 24px;font:17px/1.65 system-ui;color:#202a36}h1{font-size:32px}h2{margin-top:45px;font-size:24px}img{display:block;max-width:100%;margin:28px auto}a{color:#356eaa}.table{overflow:auto}table{border-collapse:collapse;font-size:13px;width:100%;margin:18px 0}td,th{padding:9px 12px;text-align:left;border-bottom:1px solid #e3e7ed}th{background:#f4f6f9}pre{overflow:auto;background:#f4f6f9;padding:18px;font-size:13px}code{font-size:.9em}</style>'+''.join(parts)+'</html>'
    (output/'apob-audit.html').write_text(doc,encoding='utf-8',newline='\n')
    print('Report, fitted figures and aggregate results written to',output)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    render(args.output)
