"""Read-only NHANES input and all-cause mortality overlap audit."""
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
import hashlib
import json
import urllib.request
import time
from datetime import datetime, timezone
import pandas as pd
import numpy as np
from assays import harmonize_apob

ROOT = Path(__file__).resolve().parents[2] / '.artifacts' / 'mortality-age'
DATA = ROOT / 'data'
DATA.mkdir(parents=True, exist_ok=True)
CYCLES = {
    1999: ['DEMO', 'BPX', 'BMX', 'LAB10', 'LAB11', 'CVX'],
    2001: ['DEMO_B', 'BPX_B', 'BMX_B', 'L10_B', 'L11_B', 'CVX_B'],
    2003: ['DEMO_C', 'BPX_C', 'BMX_C', 'L10_C', 'L11_C', 'CVX_C'],
    2005: ['DEMO_D', 'BPX_D', 'BMX_D', 'GHB_D', 'CRP_D', 'TRIGLY_D'],
    2007: ['DEMO_E', 'BPX_E', 'BMX_E', 'GHB_E', 'CRP_E', 'APOB_E'],
    2009: ['DEMO_F', 'BPX_F', 'BMX_F', 'GHB_F', 'CRP_F', 'APOB_F'],
    2011: ['DEMO_G', 'BPX_G', 'BMX_G', 'GHB_G', 'APOB_G', 'MGX_G'],
    2013: ['DEMO_H', 'BPX_H', 'BMX_H', 'GHB_H', 'APOB_H', 'MGX_H'],
    2015: ['DEMO_I', 'BPX_I', 'BMX_I', 'GHB_I', 'APOB_I', 'HSCRP_I'],
}

def download(item):
    year, name, kind = item
    if kind == 'mort':
        url = 'https://ftp.cdc.gov/pub/health_statistics/nchs/datalinkage/linked_mortality/' + name
    else:
        url = f'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/{year}/DataFiles/{name}'
    path = DATA / name
    if not path.exists():
        for attempt in range(3):
            try:
                with urllib.request.urlopen(url, timeout=50) as response:
                    raw = response.read()
                if name.endswith('.xpt') and not raw.startswith(b'HEADER RECORD'):
                    raise ValueError('Response is not a SAS transport file')
                if kind == 'mort' and (b'<html' in raw[:500].lower() or len(raw) < 10000):
                    raise ValueError('Response is not a mortality data file')
                path.write_bytes(raw)
                break
            except Exception:
                if attempt == 2:
                    raise
                time.sleep(attempt + 1)
    raw = path.read_bytes()
    if name.endswith('.xpt'):
        frame = pd.read_sas(path, format='xport')
        if frame.SEQN.duplicated().any():
            raise ValueError(f'Duplicate respondent IDs: {name}')
        rows = len(frame)
        columns = list(frame.columns)
    else:
        rows = len(raw.splitlines())
        columns = []
    return {'file': name, 'url': url, 'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest(), 'rows': rows, 'columns': columns,
            'retrievedUtc': datetime.fromtimestamp(path.stat().st_mtime, timezone.utc).isoformat()}

items = [(year, name + '.xpt', 'xpt') for year, names in CYCLES.items() for name in names]
items += [(1999, 'SSCARD_A.xpt', 'xpt')]
items += [(year, f'NHANES_{year}_{year+1}_MORT_2019_PUBLIC.dat', 'mort') for year in CYCLES]
manifest = []
with ThreadPoolExecutor(max_workers=4) as pool:
    futures = {pool.submit(download, item): item for item in items}
    for future in as_completed(futures):
        record = future.result()
        manifest.append(record)
        print(f"Verified {record['file']}: {record['rows']} records", flush=True)
(ROOT / 'downloads.json').write_text(json.dumps(sorted(manifest, key=lambda r: r['file']), indent=2))

cys = pd.read_sas(DATA / 'SSCARD_A.xpt', format='xport').set_index('SEQN')
all_rows = []
summary = []
for year, names in CYCLES.items():
    merged = pd.read_sas(DATA / (names[0]+'.xpt'), format='xport').set_index('SEQN')
    for name in names[1:]:
        frame = pd.read_sas(DATA / (name+'.xpt'), format='xport').set_index('SEQN')
        overlap = merged.columns.intersection(frame.columns)
        frame = frame.drop(columns=overlap)
        merged = merged.join(frame, how='left', validate='one_to_one')
    if year <= 2003:
        merged = merged.join(cys, how='left', validate='one_to_one')
    mortality = pd.read_fwf(DATA / f'NHANES_{year}_{year+1}_MORT_2019_PUBLIC.dat',
        colspecs=[(0,6),(14,15),(15,16),(42,45),(45,48)],
        names=['SEQN','eligible','event','followup_interview','followup_exam'], na_values=['.']).set_index('SEQN')
    merged = merged.join(mortality, how='left', validate='one_to_one')
    out = pd.DataFrame(index=merged.index)
    out['cycle'] = year
    out['age'] = merged['RIDAGEYR']
    out['male'] = (merged['RIAGENDR'] == 1).astype(float)
    out['event'] = merged['event']
    out['time'] = merged['followup_exam']
    out['eligible'] = merged['eligible']
    out['pregnant'] = merged['RIDEXPRG'] if 'RIDEXPRG' in merged else np.nan
    out['stratum'] = merged['SDMVSTRA']
    out['psu'] = merged['SDMVPSU']
    for w in ['WTMEC2YR','WTMEC4YR','WTSAF2YR','WTSSCB2Y','WTSSCB4Y']:
        out[w] = merged[w] if w in merged else np.nan
    out['sbp'] = merged[[f'BPXSY{i}' for i in range(1,4)]].replace(0,np.nan).mean(axis=1)
    out['dbp'] = merged[[f'BPXDI{i}' for i in range(1,4)]].replace(0,np.nan).mean(axis=1)
    out['whr'] = merged['BMXWAIST'] / merged['BMXHT']
    out['hba1c'] = merged['LBXGH']
    out['apob_raw'] = merged['LBXAPB'] if 'LBXAPB' in merged else np.nan
    out['apob'] = harmonize_apob(out['apob_raw'], year)
    out['crp'] = merged['LBXCRP']*10 if 'LBXCRP' in merged else merged['LBXHSCRP'] if 'LBXHSCRP' in merged else np.nan
    out['cystatin'] = merged['SSCYST'] if 'SSCYST' in merged else np.nan
    out['vo2'] = merged['CVDESVO2'] if 'CVDESVO2' in merged else np.nan
    grip_cols = [f'MGXH{h}T{t}' for h in (1,2) for t in (1,2,3)]
    if all(c in merged for c in grip_cols):
        valid_grip = pd.DataFrame({c: merged[c].where(merged[c+'E']==1) for c in grip_cols})
        out['grip'] = valid_grip.max(axis=1)
    else:
        out['grip'] = np.nan
    out = out[(out.age >=18) & (out.age <80) & (out.eligible==1) & out.event.isin([0,1]) & (out.time>0) & (out.pregnant!=1) & merged['RIAGENDR'].isin([1,2])]
    core = ['sbp','dbp','whr','hba1c']
    counts = {'cycle':year,'adults':len(out),'deaths':int(out.event.sum())}
    for label, features in {'core':core,'blood':core+['crp','cystatin'],'lipid_inflammation':core+['apob','crp'], 'strength':core+['apob','grip'], 'fitness':core+['crp','cystatin','vo2'], 'all_eight':core+['apob','crp','cystatin','grip','vo2']}.items():
        selected = out.dropna(subset=features)
        counts[label+'_n'] = len(selected)
        counts[label+'_deaths'] = int(selected.event.sum())
    summary.append(counts)
    all_rows.append(out)
pd.concat(all_rows).reset_index().to_csv(ROOT / 'harmonized.csv', index=False)
(ROOT / 'overlap.json').write_text(json.dumps(summary, indent=2))
(ROOT / 'apob-assays.json').write_text(json.dumps({
    'input': 'Raw LBXAPB in mg/dL', 'target': '2007-2008 BN ProSpec scale',
    '2005-2006Factor': .923, 'otherCyclesFactor': 1.,
    'source': 'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2007/DataFiles/APOB_E.htm',
    'note': 'Previously exported fits require refitting and validation before replacement.'
}, indent=2))
print(json.dumps(summary, indent=2), flush=True)
