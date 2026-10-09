"""Generate the substantive report from saved results, with no hand-entered results."""
from pathlib import Path
import json
from html import escape
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, Image, PageBreak, KeepTogether
from reportlab.lib import colors
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from model import FEATURES, transform, cubic

REPO = Path(__file__).resolve().parents[2]
ARCHIVE = Path(__file__).parent / 'artifacts'
TEMP = REPO / '.artifacts' / 'mortality-age' / 'report'
PUBLIC = REPO / 'LongevityWorldCup.Website' / 'wwwroot' / 'research'
TEMP.mkdir(parents=True, exist_ok=True)
R = json.loads((ARCHIVE / 'results.json').read_text())
FULL = R['fullModel']
NAMES = dict(sbp='Systolic BP', dbp='Diastolic BP', whr='Waist / height', hba1c='HbA1c', apob='ApoB',
             crp='hs-CRP', cystatin='Cystatin C', grip='Single-hand grip', vo2='Estimated VO2max')
UNITS = dict(sbp='mmHg', dbp='mmHg', whr='ratio', hba1c='%', apob='mg/dL', crp='mg/L', cystatin='mg/L', grip='kg', vo2='mL/kg/min')
LABELS = dict(core='Common core', blood='Core + CRP + cystatin', lipid='Core + ApoB + CRP',
              strength='Core + ApoB + grip', fitness='Core + CRP + cystatin + fitness', full='Full integration (marginal)')
TEAL, INK, MUTED = colors.HexColor('#087685'), colors.HexColor('#243447'), colors.HexColor('#526373')
font_root = Path('C:/Windows/Fonts')
if (font_root/'arial.ttf').exists():
    for name, file in [('Research', 'arial.ttf'), ('ResearchBold', 'arialbd.ttf'), ('ResearchItalic', 'ariali.ttf')]:
        pdfmetrics.registerFont(TTFont(name, str(font_root/file)))
    pdfmetrics.registerFontFamily('Research', normal='Research', bold='ResearchBold', italic='ResearchItalic', boldItalic='ResearchBold')
    FONT, BOLD = 'Research', 'ResearchBold'
else:
    FONT, BOLD = 'Helvetica', 'Helvetica-Bold'
styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name='Body', fontName=FONT, fontSize=9.5, leading=14, textColor=INK, spaceAfter=9))
styles.add(ParagraphStyle(name='Small', parent=styles['Body'], fontSize=8, leading=11, textColor=MUTED, spaceAfter=5))
styles.add(ParagraphStyle(name='TitleResearch', fontName=BOLD, fontSize=32, leading=38, textColor=INK, spaceAfter=16))
styles.add(ParagraphStyle(name='H', fontName=BOLD, fontSize=17, leading=23, textColor=TEAL, spaceAfter=12))
styles.add(ParagraphStyle(name='Sub', fontName=BOLD, fontSize=11, leading=15, textColor=INK, spaceBefore=8, spaceAfter=8))
styles.add(ParagraphStyle(name='Equation', fontName=FONT, fontSize=9, leading=14, textColor=INK, leftIndent=12, spaceAfter=10))
story = []


def p(text, style='Body'):
    story.append(Paragraph(text, styles[style]))


def page(title):
    if story:
        story.append(PageBreak())
    p(title, 'H')


def table(rows, widths, small=False, compact=False):
    formatted = [[Paragraph(escape(str(cell)), styles['Small' if small or i else 'Body']) for cell in row] for i, row in enumerate(rows)]
    item = Table(formatted, colWidths=widths, repeatRows=1, hAlign='LEFT')
    item.setStyle(TableStyle([('BACKGROUND', (0,0), (-1,0), colors.HexColor('#e8f3f4')),
        ('TEXTCOLOR', (0,0), (-1,0), INK), ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('LINEBELOW', (0,0), (-1,0), .6, TEAL), ('LINEBELOW', (0,1), (-1,-1), .3, colors.HexColor('#d7dfe5')),
        ('LEFTPADDING', (0,0), (-1,-1), 6), ('RIGHTPADDING', (0,0), (-1,-1), 6),
        ('TOPPADDING', (0,0), (-1,-1), 3 if compact else 6), ('BOTTOMPADDING', (0,0), (-1,-1), 3 if compact else 6)]))
    story.extend([item, Spacer(1, 10)])


def fmt(value, digits=3):
    return 'Not estimable' if value is None else f'{value:.{digits}f}'


def interval(values, digits=2):
    return ' to '.join(fmt(v, digits) for v in values)


def component(x, sex, feature, coef):
    spec = FULL['curves'][feature]
    tx = transform(feature, x)
    z = (tx-spec['center'])/spec['scale']
    result = coef[feature+'_linear']*z + coef[feature+'_sex']*z*(sex-.5)
    if FULL['smooth']:
        result += coef[feature+'_nonlinear']*(cubic(tx, spec['knots'])-spec['nonlinearCenter'])/spec['nonlinearScale']
    return result


def make_curves(features, filename):
    fig, axes = plt.subplots(3, 1, figsize=(7.4, 8.1), constrained_layout=True)
    boot = [r['fit']['coefficients'] for r in R['bootstrap']['fits'] if r['fit']['converged']]
    for ax, feature in zip(axes, features):
        for sex, color, label in [(0, '#087685', 'Female'), (1, '#9259a4', 'Male')]:
            lo, hi = FULL['curves'][feature]['support'][str(sex)]
            grid = np.linspace(lo, hi, 161)
            reference = R['sensitivity']['exampleProfiles'][sex]['inputs'][feature]
            ys = component(grid, sex, feature, FULL['coefficients'])-component(reference, sex, feature, FULL['coefficients'])
            draws = np.array([component(grid, sex, feature, b)-component(reference, sex, feature, b) for b in boot])
            lower, upper = np.quantile(draws, [.1, .9], axis=0)
            ax.plot(grid, ys, color=color, label=label, linewidth=1.6)
            ax.fill_between(grid, lower, upper, color=color, alpha=.13, linewidth=0)
        ax.axhline(0, color='#9ba9b5', linewidth=.6)
        ax.set_title(NAMES[feature], fontsize=10, fontweight='bold', loc='left')
        ax.set_xlabel(UNITS[feature], fontsize=8)
        ax.set_ylabel('Log hazard contribution', fontsize=8)
        ax.tick_params(labelsize=8)
        ax.spines[['top','right']].set_visible(False)
        ax.legend(frameon=False, fontsize=8, loc='best')
    path = TEMP / filename
    fig.savefig(path, dpi=220, facecolor='white')
    plt.close(fig)
    return path


def footer(canvas, doc):
    canvas.saveState()
    width, _ = A4
    canvas.setStrokeColor(colors.HexColor('#d7dfe5')); canvas.line(48, 39, width-48, 39)
    canvas.setFont(FONT, 7.5); canvas.setFillColor(MUTED)
    canvas.drawString(48, 26, 'Mortality age | Research experiment | 9 October 2026')
    canvas.drawRightString(width-48, 26, str(doc.page))
    canvas.restoreState()


def main():
    full_eval = R['fullResults']['temporalEvaluation']
    blood = R['panelResults']['blood']['temporalEvaluation']
    fitness = R['panelResults']['fitness']['temporalEvaluation']
    p('Mortality age', 'TitleResearch')
    p('A reproducible, sex-dependent, multi-cohort mortality-equivalent age experiment', 'Sub')
    p('Version 0.1 · 9 October 2026 · Longevity World Cup', 'Small')
    p('<b>Finding:</b> NHANES can support several jointly measured mortality models, but these public cohorts do not establish a validated clock using all eight requested measurement domains. No respondent has the complete panel. The fitness panel failed a sparse five-year temporal calibration test. The complete-panel equation is published as an unvalidated integration experiment, with its assumptions exposed.')
    p(f'The development integration includes {R["fullResults"]["development"]["n"]:,} people aged 18-49 and {R["fullResults"]["development"]["deaths"]} deaths over available follow-up. Marginal temporal evaluation includes {full_eval["n"]:,} people, {full_eval["deaths"]} total deaths, and {full_eval["fiveYearDeaths"]} deaths within five years. Missing measurements are integrated out during this evaluation; this is <b>not validation of a jointly observed eight-domain prediction</b>.')
    p(f'In marginal temporal evaluation, the full experiment has observed/expected five-year mortality {full_eval["observedExpected"]:.2f} and unweighted Harrell C {full_eval["unweightedHarrellC"]:.3f}. The observed inflammation/kidney panel has observed/expected {blood["observedExpected"]:.2f}, slope {blood["fiveYearCalibrationSlope"]:.2f} and C {blood["unweightedHarrellC"]:.3f}. Its broader age range prevents interpreting this as a head-to-head comparison.')
    p(f'The fitness panel has only {fitness["fiveYearDeaths"]} five-year deaths in its temporal test, with observed/expected {fitness["observedExpected"]:.2f}. Its numeric panel age is withheld in the calculator. Other panel estimates and any full integration result remain experimental, not clinical validation.')
    p('The displayed age matches modeled five-year all-cause mortality to a historical sex-specific age reference. It measures an association in selected survey populations. It does not estimate pace of aging, life expectancy, treatment benefit, or years gained.')
    p('Calculator: <link href="https://longevityworldcup.com/mortality-age" color="#087685">longevityworldcup.com/mortality-age</link><br/>Frozen model: <link href="https://longevityworldcup.com/research/mortality-age-model.json" color="#087685">mortality-age-model.json</link>', 'Small')

    page('1. Measurements and populations')
    rows = [['Domain', 'Definition and canonical unit']]
    definitions = [
        ('Circulation', 'Mean nonzero readings 1-3 of resting clinic systolic and diastolic BP, mmHg. Both enter the joint equation. Not 24-hour blood pressure. [4]'),
        ('Fitness', 'CVDESVO2, exercise-based treadmill estimate, mL/kg/min. Do not substitute CVDVOMAX, the non-exercise protocol-assignment estimate. Eligibility excluded many people with health conditions; ages 12-49 were tested. Adult calculator range: 18-49. [5-7]'),
        ('Strength', 'Highest MGX trial from either hand with effort code 1 (maximal). Unit kg. Not MGDCGSZ, which adds best readings from both hands. [8-9]'),
        ('Body composition', 'Measured waist cm / standing height cm. The same unit is required in numerator and denominator. [10]'),
        ('Lipids', 'LBXAPB ApoB, mg/dL, from the morning fasting subsample. [11-12]'),
        ('Metabolism', 'LBXGH HbA1c, NGSP percent. IFCC conversion: % = 0.09148 × mmol/mol + 2.152. [13]'),
        ('Kidney', 'SSCYST cystatin C, mg/L, revised SSCARD_A stored-sample release spanning 1999-2004. Laboratory testing was in 2018-2020. Dedicated updated sample weights are used for its distributions and observed panels. [3]'),
        ('Inflammation', 'CRP: early LBXCRP mg/dL × 10; 2015 LBXHSCRP is already mg/L. Released below-detection fill values are retained. The 2015 assay is separate sensitivity data. [14-15]')]
    rows += definitions
    table(rows, [100, 397], small=True)
    p('Recorded RIAGENDR categories (female/male) are required. Every component includes a regularized sex-dependent slope; sex is also included in the baseline and age reference. These categories do not encode gender identity or hormone treatment. Differences that shrink toward zero are uncertain, not proof that sexes have identical effects.')
    p('Analytic rows are mortality-eligible, nonpregnant where identified, aged 18-79, with event status 0/1 and positive examination follow-up. Ages 80+ are excluded because of top-coding. Unknown pregnancy is not equivalent to a measured negative pregnancy test. The common core requires BP, waist/height and HbA1c. Complete-core selection and measurement exclusions may affect transport.')

    page('2. Data, overlap and frozen split')
    p('Public NHANES 1999-2016 is linked by SEQN to the 2019 public-use mortality files, using PERMTH_EXM and MORTSTAT. All causes are included. Public follow-up can be perturbed for confidentiality; mortality status is preserved. Restricted newer linkage was not accessed. [1-2]')
    p('Development cycles: 1999, 2001, 2005, 2007, 2011. Temporal evaluation: 2003, 2009, 2013. The 2015 cycle is reserved for assay/short-follow-up sensitivity. The plan was saved after availability counts and before fitting or prediction performance. A deterministic respondent hash separates internal 80% development fitting from 20% model selection.')
    rows = [['Cycle', 'Core N / D', 'CRP+cys N / D', 'ApoB+CRP N / D', 'ApoB+grip N / D', 'CRP+cys+VO2 N / D']]
    for r in R['overlap']:
        rows.append([f'{r["cycle"]}-{str(r["cycle"]+1)[-2:]}']+[f'{r[k+"_n"]:,} / {r[k+"_deaths"]}' for k in ['core','blood','lipid_inflammation','strength','fitness']])
    table(rows, [51, 86, 90, 90, 88, 92], small=True)
    p('N = observed complete rows for that panel; D = deaths over all available follow-up, not five years. Panels include the common core. These availability counts precede positive-weight exclusions. <b>All-eight complete cases: zero in every cycle.</b> Exercise fitness and grip were never collected together in these cycles.')
    rows = [['Model', 'Development N / D', 'Temporal N / D / D≤5y']]
    for label in ['core','blood','lipid','strength','fitness']:
        dev, ev = R['panelResults'][label]['development'], R['panelResults'][label]['temporalEvaluation']
        rows.append([LABELS[label], f'{dev["n"]:,} / {dev["deaths"]}', f'{ev["n"]:,} / {ev["deaths"]} / {ev["fiveYearDeaths"]}'])
    table(rows, [244, 116, 137], small=True)
    p('The age/sex reference uses the complete-core development population aged 18-79, not every eligible NHANES respondent. Modelled five-year mortality is therefore a historical complete-core reference, not a forecast of current national mortality.', 'Small')

    page('3. Survival model and sex-dependent components')
    p('Follow-up t is in years; d is death status. Sex s is 0 for female and 1 for male. For observed inputs, the sex-specific Gompertz model is:')
    p('η(x,a,s) = α + δs + b<sub>age,s</sub>(a − 45)/10 + Σ<sub>j</sub> f<sub>j</sub>(x<sub>j</sub>,s)<br/>h(t|x,a,s) = exp(η) exp(γ<sub>s</sub>t)<br/>H(t|x,a,s) = exp(η) A(γ<sub>s</sub>,t), where A(γ,t) = (exp(γt) − 1)/γ<br/>P(death by 5 years) = 1 − exp[−H(5)]', 'Equation')
    p('Log transformations are used for ApoB, CRP, cystatin C, grip and fitness to model positive measurements. Other measurements use their raw scale. Development-weighted 10th/50th/90th percentiles are the three restricted-cubic-spline knots. z is centered/scaled transformed input; q is centered/scaled spline basis. The component is:')
    p('f<sub>j</sub>(x,s) = β<sub>j</sub> z<sub>j</sub>(x) + θ<sub>j</sub> q<sub>j</sub>(x) + ψ<sub>j</sub> z<sub>j</sub>(x)(s − 0.5)', 'Equation')
    p('This is f(VO2max, sex), f(grip, sex), and equivalent sex-dependent functions for the other markers. Shared nonlinear curvature plus sex-specific slope deviations borrow information. Separate unrestricted male/female nonlinear fits would consume more information than the young-event counts support. No biomarker is capped to a preferred clinical value, and U/J shapes are not forced.')
    p('Three knots k1 &lt; k2 &lt; k3 define q before centering/scaling: [(u−k1)<sub>+</sub>³ − (u−k2)<sub>+</sub>³(k3−k1)/(k3−k2) + (u−k3)<sub>+</sub>³(k2−k1)/(k3−k2)]/(k3−k1)². Its tails are linear mathematically; the calculator refuses values beyond declared empirical support instead of extrapolating those tails.', 'Small')
    p('The weighted negative mean log-likelihood uses ridge shrinkage. Relative penalties are 1 for main linear terms, 4 for nonlinear terms, 8 for sex deviations, 0.05 for sex, 0.02 for age slopes and zero for intercept. The internal grid is linear/log λ = 0.0003, 0.001, 0.003 and smooth λ = 0.001, 0.003. The smooth λ = 0.003 candidate wins by internal marginal survival loss and is refit on development only.')
    table([['Candidate', 'Internal loss', 'Converged']] + [[('Smooth' if r['smooth'] else 'Linear/log')+f', λ={r["ridge"]}', fmt(r['validationLoss'],6), r['fit']['converged']] for r in R['selection']], [265, 116, 116], True)
    p('Numerical age slopes are constrained positive for reference inversion; follow-up slopes are bounded −0.1 to 0.2 per year. These are optimizer constraints, not biomarker clipping. Survey weights are normalized by sum in the likelihood. The fit includes full right-censored follow-up; its five-year output depends on the time form and proportional-effects assumption.')

    page('4. What combining cohorts assumes')
    p('Adding adjusted marginal hazard ratios would double count shared information and would not recover a joint model. Here each original participant contributes a survival likelihood marginalized over their unobserved measurements:')
    p('L<sub>i</sub> = E<sub>Xmissing | Xobserved, age, sex, core</sub> { h(t<sub>i</sub>|X)<sup>d<sub>i</sub></sup> exp[−H(t<sub>i</sub>|X)] }', 'Equation')
    p('Conditional measurement means are ridge regressions on age, age², sex, age×sex, the four common-core measurements and their sex interactions. Residuals for the five occasionally measured inputs are approximated by sex-specific multivariate Gaussian distributions on standardized transformed scales. Observed values remain fixed during integration. The draws are quadrature, not fabricated observed participants.')
    p('Genuinely observed residual pair covariance is retained. A pair with fewer than 50 jointly measured records in a sex is assigned zero residual covariance in the primary model. Unobserved relationships cannot be inferred merely because datasets share the common core. The zero-residual-dependence choice and the transport of conditional distributions/outcome relationships across cycles and fitness exclusions are assumptions. [16-17]')
    pairs = FULL['distributions']['observedPairs']
    rows = [['Pair', 'Female N', 'Male N']]
    for pair, n in pairs['0'].items():
        if pair.split('|')[0] != pair.split('|')[1]:
            rows.append([pair.replace('|',' + '), n, pairs['1'][pair]])
    table(rows, [257,120,120], True)
    p('Conditional means use dedicated ApoB fasting weights or revised cystatin weights where available, otherwise MEC weights. Observed-panel survival uses the panel-specific weights. Early four-year weights are halved before pooling with two-year cycles. The full outcome model and residual covariance use MEC weights; no single published joint subsample weight covers the eight domains. Fitness noncompletion is not corrected by an official fitness response weight. These departures limit population calibration. [3,18]')
    p('The primary integral uses 32 deterministic antithetic Gaussian draws; 64-draw refitting checks numerical sensitivity. Residual covariance matrices are shrunk toward their diagonal only if needed for positive definiteness. Unknown pair correlations are changed to −0.3, +0.3 and +0.6, then refit. PSD shrinkage changes the attained correlations; all matrices and shrinkage are saved.')

    page('5. Temporal evaluation and its limits')
    rows = [['Model', 'O/E (80% PSU range)', 'C', 'Slope', 'Brier 5y']]
    for label in ['full','core','blood','lipid','strength','fitness']:
        ev = full_eval if label == 'full' else R['panelResults'][label]['temporalEvaluation']
        ci = R['diagnostics']['calibration'][label]['observedExpected80']
        rows.append([LABELS[label], f'{ev["observedExpected"]:.2f} ({interval(ci)})', fmt(ev['unweightedHarrellC']), fmt(ev['fiveYearCalibrationSlope'],2), fmt(ev['ipcwBrier5y'],5)])
    table(rows, [168,143,49,57,80], True)
    p('O/E = weighted Kaplan-Meier observed five-year death probability / weighted mean predicted probability; 1 is the target. Its 80% interval uses 128 survey-PSU bootstraps within strata with the model fixed. C is unweighted Harrell concordance over available censored follow-up, not five-year AUC. Slopes use a five-year truncated Cox recalibration on log cumulative risk when at least 30 five-year events are available. A missing slope is insufficient event information. Brier error uses inverse-censoring-probability weights with the censoring distribution estimated in the evaluation cohort. C/slope/Brier point estimates do not have design intervals in this initial report.')
    core_young = R['diagnostics']['commonCoreBenchmarkUnder50']
    base = full_eval['ageSexBenchmark']
    table([['Same under-50 holdout', 'O/E', 'C', 'Brier 5y'],
        ['Age/sex', fmt(base['observedExpected'],2), fmt(base['unweightedHarrellC']), fmt(base['ipcwBrier5y'],6)],
        ['Observed common core', fmt(core_young['observedExpected'],2), fmt(core_young['unweightedHarrellC']), fmt(core_young['ipcwBrier5y'],6)],
        ['Full marginal integration', fmt(full_eval['observedExpected'],2), fmt(full_eval['unweightedHarrellC']), fmt(full_eval['ipcwBrier5y'],6)]], [228,75,75,119], True)
    p(f'The full marginal Brier improvement over age/sex is {full_eval["brierImprovement"]:.6f}, small in absolute terms. These predictions integrate missing measurements. Even good marginal calibration would not prove that the unseen joint eight-domain equation is correct. There is no external cohort validation.')
    rows = [['Full marginal test', 'N', 'Deaths ≤5y', 'O/E (80% PSU range)']]
    for s, label in [('0','Female'),('1','Male')]:
        ev = full_eval['bySex'][s]
        ci = R['diagnostics']['calibration']['full']['bySexObservedExpected80'][s]
        rows.append([label, ev['n'], ev['fiveYearDeaths'], f'{ev["observedExpected"]:.2f} ({interval(ci)})'])
    table(rows, [160,75,92,170], True)
    p('The fitness panel substantially overpredicts the sparse test outcomes. With four five-year deaths it cannot supply reliable calibration or elaborate sex-specific fitness curves. The release decision to withhold that panel is a post-evaluation usability decision; the frozen model and all failure metrics remain available. The full model is not promoted to a validated clock by partial-panel results.')

    for i, fs in enumerate([FEATURES[:3], FEATURES[3:6], FEATURES[6:]], 1):
        page(f'6.{i} Sex-dependent curves and conditional uncertainty')
        path = make_curves(fs, f'curves-{i}.png')
        story.append(Image(str(path), width=497, height=544))
        p('Each curve is the log-hazard contribution relative to that sex’s development median profile, holding other inputs fixed. Shading is the 10th-90th percentile of 32 survey-cluster refits with fixed knots, conditional distributions, selected specification and age reference. It omits nuisance-estimation and unobserved-dependence uncertainty. Limits are development-weighted 1st-99th input percentiles by sex, not clinical normal ranges.', 'Small')
        if i == 2:
            p('ApoB has an inverse adjusted association in this experiment. This is not evidence that raising ApoB improves health. Selection, illness, confounding and the integration assumptions can affect this association.', 'Small')
        if i == 3:
            p('The fitness contribution is weak and does not show the expected protective ordering in this fit. Sparse outcomes and selective exercise eligibility make a biological interpretation unjustified. Shrunk sex deviations do not establish equivalence between sexes.', 'Small')

    page('7. Sensitivity, uncertainty and failed hypotheses')
    profiles = R['sensitivity']['exampleProfiles']
    rows = [['Refit / assumption', 'Female example age', 'Male example age']]
    rows.append(['Primary 32-draw integration']+[fmt(r['age'],2) for r in profiles])
    for scenario in R['sensitivity']['dependence']:
        rows.append([f'Unknown residual correlation {scenario["correlation"]:+.1f}']+[fmt(a,2) for a in scenario['exampleAge']])
    rows.append(['64-draw integration']+[fmt(a,2) for a in R['sensitivity']['moreDraws']['exampleAge']])
    rows.append(['Exponential baseline + matched reference']+[fmt(a,2) for a in R['diagnostics']['timeForm']['exampleAge']])
    table(rows, [257,120,120], True)
    p('Examples have chronological age 40 and development medians for each sex. They are artificial profiles, not two jointly observed people. The exponential sensitivity refits both outcome model and age reference with a constant baseline hazard. It demonstrates time-form dependence; it does not establish the proportional-effects assumption.')
    ph = R['proportionalEffects']
    smallest = {label: min(result['results'].items(), key=lambda r: r[1]['p']) for label,result in ph.items()}
    p('Exploratory sex-stratified Cox diagnostics using the observed core/blood spline bases found ranked-time associations in scaled Schoenfeld residuals. The smallest approximate p values are ' + '; '.join(f'{label}: {name}, p={result["p"]:.2g}' for label,(name,result) in smallest.items()) + '. This raises concern about constant covariate effects over follow-up. These penalized, survey-weighted diagnostics have no survey-design or multiple-testing correction and are not a formal test of the integrated likelihood.', 'Small')
    sweep = R['diagnostics']['profileSweep']
    p(f'One-at-a-time sweeps across sex, ages 20/30/40/49 and nine values per input produced {sweep["n"]} artificial profiles. The median tested dependence age span is {sweep["medianDependenceAgeSpan"]:.2f} years; the maximum is {sweep["maxDependenceAgeSpan"]:.2f} years. {sweep["unsupportedPrimary"]} primary profiles lie outside the reference-age support. This is not a simultaneous joint-tail stress test and does not exhaust unidentified relationships.')
    p('Observed-panel two-year landmark fits restart follow-up among people surviving past two years, subtract 24 months and advance age by two years. Baseline measurements remain two years old. This handles the time origin correctly but does not remove survivor selection or reverse causality. The separate integrated landmark artifact is exploratory and also changes conditional missing-input assumptions; it is not treated as a validation result.')
    p('The 32 training bootstraps are conditional on fixed nuisance distributions, knots, model selection and reference; their 80% age/curve ranges are not comprehensive prediction intervals. Structural sensitivity spans are separate, finite scenario comparisons, not confidence intervals. Regularization may conceal weakly identified components. One must not infer that each domain is independently measured biological aging.')
    p('The proposed all-domain clock hypothesis remains unestablished. Additional joint measurements, mortality events, sex-specific evaluation, measurement-method calibration and independent cohort validation are required before clinical or intervention interpretation. Accessible public NHANES overlaps cannot supply the absent exercise/grip joint information. HRS health access and UK Biobank would require separately authorized access; neither dataset was used. [19-20]')

    page('8. From risk to age, and supported inputs')
    ref = FULL['reference']
    p('Reference risk is fit in the complete-core development population aged 18-79. The same five-year endpoint and sex are used on both sides. Matching cumulative hazard rather than rounded probabilities gives a stable closed-form age:')
    p('Age<sub>eq</sub> = 45 + 10 { log H<sub>model</sub>(5) − log A(γ<sub>ref,s</sub>,5) − α<sub>ref</sub> − δ<sub>ref</sub>s } / b<sub>ref,age,s</sub>', 'Equation')
    table([['Reference parameter', 'Value']] + [[k, f'{v:.12g}'] for k,v in ref['coefficients'].items()]
          + [[f'gamma_{s}', f'{g:.12g}'] for s,g in enumerate(ref['gamma'])], [257,240], True)
    p('The reference is monotone because its age coefficients are positive. Inversion is allowed only over ages 18-79. The full fitness integration accepts chronological ages 18-49; nonfitness panels accept 18-79. Inputs outside sex-specific weighted 1st-99th development percentiles return unsupported, not a capped measurement or clipped age. These marginal ranges do not certify support for every joint combination.')
    rows = [['Input', 'Female support', 'Male support', 'Unit']]
    for f in FEATURES:
        c = FULL['curves'][f]
        rows.append([NAMES[f], interval(c['support']['0'],3), interval(c['support']['1'],3), UNITS[f]])
    table(rows, [142,136,136,83], True)
    p('The calculator accepts an exercise-based estimate; wearable/non-exercise fitness methods return unsupported for the fitness/full calculation because cross-method calibration was not established. If a laboratory reports a value below a known reporting limit, the entered limit is divided by √2 for CRP/cystatin, matching the public-data fill convention. Unknown limits are not assigned invented values. Inputs and drafts are local to the browser; no health values are sent to a server.')

    page('9. Frozen equation coefficients')
    table([['Component', 'Linear β', 'Spline θ', 'Sex slope ψ']] + [[NAMES[f], f'{FULL["coefficients"][f+"_linear"]:.9g}', f'{FULL["coefficients"][f+"_nonlinear"]:.9g}', f'{FULL["coefficients"][f+"_sex"]:.9g}'] for f in FEATURES], [185,104,104,104], True, compact=True)
    p('The component sex slope multiplies z(x)(s−0.5), not raw x. Coefficients require the exact transformations, knots, centers/scales and baseline. A component taken from another panel cannot be swapped into this full equation while retaining its interpretation: each panel is fitted jointly and calibrated against the reference.')
    table([['Full-model baseline', 'Value']] + [[k, f'{FULL["coefficients"][k]:.12g}'] for k in ['intercept','male','age_female','age_male']]
          + [[f'gamma_{s}', f'{g:.12g}'] for s,g in enumerate(FULL['gamma'])], [257,240], True, compact=True)
    rows = [['Input', 'Transformed knots', 'Center / scale']]
    for f in FEATURES:
        c = FULL['curves'][f]
        rows.append([NAMES[f]+(' (log)' if c['log'] else ''), ' / '.join(f'{k:.6g}' for k in c['knots']), f'{c["center"]:.6g} / {c["scale"]:.6g}'])
    table(rows, [145,205,147], True, compact=True)
    p('All digits, nonlinear centers/scales, panel coefficients, conditional covariance matrices, failed specifications and numerical fixtures are saved in the repository artifacts. The downloadable browser JSON contains the exact evaluated equation. The report’s rounded tables are descriptive; calculations use the full stored precision.', 'Small')

    page('10. Reproduction and provenance')
    p('Source: <link href="https://github.com/nopara73/LongevityWorldCup/tree/master/LongevityWorldCup.Research/MortalityAge" color="#087685">LongevityWorldCup.Research/MortalityAge</link>. Run prepare.py, train.py, diagnostics.py, ph-diagnostics.py, export.py, report.py in that order in the pinned environment. Run test_model.py and verify-browser.mjs for independent numerical and unit parity. Raw downloads, respondent data and intermediate outputs remain in ignored .artifacts/mortality-age.')
    p(f'Model version: <b>{escape(R["modelVersion"])}</b><br/>Frozen plan SHA-256: {R["run"]["config"]["planSha256"]}<br/>Browser model SHA-256: {R["modelSha256"]}', 'Small')
    manifest = json.loads((ARCHIVE/'downloads.json').read_text())
    p(f'{len(manifest)} original public files are recorded with URL, retrieval UTC, byte size, SHA-256, row count and columns in artifacts/downloads.json. Joins are one-to-one on SEQN with duplicate checks. SAS file signatures and mortality-file content are verified. Analysis completion UTC: {R["run"]["completedUtc"]}. Monte Carlo seed: {R["run"]["config"]["seed"]}.')
    table([['Runtime / package', 'Version'], ['Python', R['environment']['python']]] + list(R['environment']['packages'].items()), [257,240], True)
    p('Verification includes analytic likelihood gradients against independent finite differences, exponential-limit calculations, observed-row/quadrature equivalence, right-censored risk sets, sex-specific age roundtrips, spline tails and Python/browser fixtures. Website checks cover unit conversions, sex-dependent functions, unsupported inputs, loading recovery, editing and restoration, mobile/desktop behavior, PDF bytes and exclusion from discovery. Code verification does not constitute scientific validation.')
    p('This report distinguishes actual temporal performance from scientific identification. The model has no complete-panel external validation, no causal adjustment or competing-risk interpretation, and no calibrated smartwatch conversion. Medication, smoking, disease, ethnicity and socioeconomic conditions are not separately included in the requested panel. Repeated measurements and longitudinal biological change were not modelled. Those limitations are material, not resolved by producing an age number.')

    page('11. Primary sources')
    refs = [
        ('CDC/NCHS public-use linked mortality description', 'https://www.cdc.gov/nchs/data/datalinkage/public-use-linked-mortality-file-description.pdf'),
        ('CDC/NCHS mortality data dictionary', 'https://www.cdc.gov/nchs/data/datalinkage/public-use-linked-mortality-files-data-dictionary.pdf'),
        ('NHANES SSCARD_A: revised cystatin/stored samples', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/1999/DataFiles/SSCARD_A.htm'),
        ('NHANES BPX_G blood pressure', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2011/DataFiles/BPX_G.htm'),
        ('NHANES CVX exercise fitness 1999-2000', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/1999/DataFiles/CVX.htm'),
        ('NHANES CVX_B exercise fitness 2001-2002', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2001/DataFiles/CVX_B.htm'),
        ('NHANES CVX_C exercise fitness 2003-2004', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2003/DataFiles/CVX_C.htm'),
        ('NHANES MGX_G grip', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2011/DataFiles/MGX_G.htm'),
        ('NHANES MGX_H grip', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2013/DataFiles/MGX_H.htm'),
        ('NHANES BMX_G anthropometry', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2011/DataFiles/BMX_G.htm'),
        ('NHANES TRIGLY_D ApoB and morning weights', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2005/DataFiles/TRIGLY_D.htm'),
        ('NHANES APOB_G', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2011/DataFiles/APOB_G.htm'),
        ('NGSP/IFCC master equation', 'https://ngsp.org/ifcc.asp'),
        ('NHANES CRP_F', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2009/DataFiles/CRP_F.htm'),
        ('NHANES HSCRP_I', 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2015/DataFiles/HSCRP_I.htm'),
        ('Raessler: Data Fusion, Identification Problems, Validity and Multiple Imputation', 'https://www.ajs.or.at/index.php/ajs/article/view/vol33,%20no1&2%20-%209'),
        ('White and Royston: missing covariates in Cox models', 'https://pmc.ncbi.nlm.nih.gov/articles/PMC2998703/'),
        ('NHANES analytic guidelines', 'https://wwwn.cdc.gov/Nchs/Nhanes/analyticguidelines.aspx'),
        ('HRS sensitive health data access', 'https://hrsdata.isr.umich.edu/data-products/sensitive-health'),
        ('UK Biobank access procedures', 'https://www.ukbiobank.ac.uk/access-procedures/')]
    for i, (title, url) in enumerate(refs, 1):
        p(f'[{i}] {escape(title)}<br/><link href="{escape(url, quote=True)}" color="#087685">{escape(url)}</link>', 'Small')
    PUBLIC.mkdir(parents=True, exist_ok=True)
    output = PUBLIC/'mortality-age.pdf'
    doc = SimpleDocTemplate(str(output), pagesize=A4, leftMargin=48, rightMargin=48, topMargin=48, bottomMargin=54,
        title='Mortality age: multi-cohort research experiment', author='Longevity World Cup', subject='Sex-dependent all-cause mortality models and identification limits')
    doc.build(story, onFirstPage=footer, onLaterPages=footer)
    print(str(output), flush=True)


if __name__ == '__main__':
    main()
