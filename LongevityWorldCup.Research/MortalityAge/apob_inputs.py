"""Background retrieval and content verification for the ApoB study audit."""
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timezone
from pathlib import Path
import argparse
import hashlib
import json
import urllib.request
import pandas as pd

MODEL_SHA256='c289c1d135d6b132c84e1736b57aa434a61519cf2c94c673f3a4c7ba55da9dc2'
MODEL_URL=('https://raw.githubusercontent.com/nopara73/LongevityWorldCup/'
           'ec4131f33b2cbcd4b6fb0583658fe5819349d492/'
           'LongevityWorldCup.Website/wwwroot/research/mortality-age-model.json')
MORT2015_HASHES={
    2005:'e48aef9b7bf788393996d846aa8bcefbb1bce80f4a9d187756a6bda169bc610c',
    2007:'04644544f1f398c3ea8c565701f95ebca53c10b9635c3c3d9542e018f15a6fbd',
    2009:'36834e0d473517bad549c2935c92e185911e8c7efc14504dbcf25b34b8067c84',
    2011:'bbc4bb615af6a574aaf400159be2626ca9f0c9655cddb2b5df4e75e0772b319a',
    2013:'999ca4fbc4497f87cd2afb06cf1ed13eaabbcd2ffb39277e76ddfe115659eb05',
}
ASSETS='https://assets-eu.researchsquare.com/files/rs-1039792/v1/'


def jobs():
    result=[]
    for year in MORT2015_HASHES:
        suffix=chr(ord('D')+(year-2005)//2)
        parts=['DEMO','BPX','BMX','GHB','MCQ','SMQ','BPQ','DIQ','TCHOL','HDL',
               'TRIGLY','BIOPRO','DR1TOT','GLU','RXQ_RX']
        if year!=2005:parts.append('APOB')
        if year<=2009:parts.append('CRP')
        for part in parts:
            name=f'{part}_{suffix}.xpt'
            result.append((name,f'https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/{year}/DataFiles/{name}',None))
        for vintage in [2015,2019]:
            name=f'NHANES_{year}_{year+1}_MORT_{vintage}_PUBLIC.dat'
            url='https://ftp.cdc.gov/pub/Health_Statistics/NCHS/datalinkage/linked_mortality/'+name
            if vintage==2015:url='https://web.archive.org/web/20210115id_/'+url
            result.append((name,url,MORT2015_HASHES[year] if vintage==2015 else None))
    result.extend([
        ('public-model.json',MODEL_URL,MODEL_SHA256),
        ('study-table1.pptx',ASSETS+'85072e361552810b1e2197b0.pptx',None),
        ('study-table2.pptx',ASSETS+'0d9b733af295966b1a510c80.pptx',None),
        ('study-table3.pptx',ASSETS+'393fa1625278155720b938f3.pptx',None),
        ('study-flow.jpg',ASSETS+'476f25bf1beb067579c4ac24.jpg',None),
        ('study-curves.jpg',ASSETS+'ab83efb8f8a621934a8fafe5.jpg',None),
        ('APOB_E.htm','https://wwwn.cdc.gov/Nchs/Data/Nhanes/Public/2007/DataFiles/APOB_E.htm',None),
        ('mortality2015-readin.R','https://ftp.cdc.gov/pub/Health_Statistics/NCHS/datalinkage/'
         'linked_mortality/archived_files/R_ReadInProgramAllSurveys_2015.R',None),
    ])
    return result


def retrieve(item,cache,output,known_hashes):
    name,url,expected=item
    dest=output/'data'/name
    cached=cache/'data'/name
    source=dest if dest.exists() else cached
    existed=source.exists()
    if existed:
        raw=source.read_bytes()
        expected=expected or known_hashes.get(name)
    else:
        request=urllib.request.Request(url,headers={'User-Agent':'LWC-public-research-audit/1.0'})
        with urllib.request.urlopen(request,timeout=50) as response:raw=response.read()
    digest=hashlib.sha256(raw).hexdigest()
    if expected and digest!=expected:raise ValueError(f'Input hash changed: {name}')
    rows=None
    if name.endswith('.xpt'):
        if not raw.startswith(b'HEADER RECORD'):raise ValueError(f'Not a SAS transport file: {name}')
        if not source.exists():dest.write_bytes(raw);source=dest
        frame=pd.read_sas(source,format='xport')
        if 'SEQN' not in frame:raise ValueError(f'Missing respondent IDs: {name}')
        if not name.startswith('RXQ_RX') and frame.SEQN.duplicated().any():raise ValueError(f'Duplicate IDs: {name}')
        rows=len(frame)
    elif name.endswith('.dat'):
        lines=raw.splitlines()
        # The 2019 files omit trailing spaces after the final numeric field.
        if len(lines)<1000 or not all(46<=len(line)<=48 for line in lines):raise ValueError(f'Invalid mortality layout: {name}')
        rows=len(lines)
    elif name.endswith('.pptx'):
        if not raw.startswith(b'PK'):raise ValueError(f'Invalid supplement archive: {name}')
    elif name.endswith('.jpg'):
        if not raw.startswith(b'\xff\xd8') or not raw.endswith(b'\xff\xd9'):raise ValueError(f'Incomplete study figure: {name}')
    elif name=='public-model.json':
        model=json.loads(raw)
        if model['modelVersion']!='mortality-age-0.2-9cbd69d1609e':raise ValueError('Unexpected model version')
    if not source.exists() or name in ['public-model.json','study-flow.jpg','study-curves.jpg']:
        dest.write_bytes(raw)
    return dict(file=name,url=url,bytes=len(raw),sha256=digest,rows=rows,
                verifiedUtc=datetime.now(timezone.utc).isoformat(),source='existing' if existed else 'download')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    args.output=args.output.resolve()
    if '.artifacts' not in args.output.parts:raise ValueError('Raw downloads must remain under .artifacts')
    (args.output/'data').mkdir(parents=True,exist_ok=True)
    hashes={}
    for path in [args.cache/'downloads.json',args.output/'input-downloads.json']:
        if path.exists():hashes.update({r['file']:r['sha256'] for r in json.loads(path.read_text()) if 'sha256' in r})
    records=[]
    with ThreadPoolExecutor(max_workers=4) as pool:
        futures=[pool.submit(retrieve,item,args.cache,args.output,hashes) for item in jobs()]
        for future in as_completed(futures):
            records.append(future.result())
            print('Verified',records[-1]['file'],flush=True)
    # Write a completion manifest only after every input has passed validation.
    (args.output/'input-downloads.json').write_text(json.dumps(sorted(records,key=lambda r:r['file']),indent=2),encoding='utf-8')


if __name__=='__main__':main()
