"""只读测量独立 DSO 测试环境；不把短样本当作生产容量保证。"""
import argparse
import concurrent.futures
import json
from pathlib import Path
import statistics
import time
import urllib.error
import urllib.parse
import urllib.request

parser=argparse.ArgumentParser()
parser.add_argument('--fixture',required=True)
parser.add_argument('--output',required=True)
args=parser.parse_args()
fixture=json.loads(Path(args.fixture).read_text())
base=fixture['apiBase']
if urllib.parse.urlsplit(base).hostname not in ('127.0.0.1','localhost'):
    raise SystemExit('Benchmark requires the localhost isolated-test handoff')
requests=[('/api/dso/v1/context','doctor'),('/api/dso/v1/patients?pageSize=20','manager'),('/api/dso/v1/crm/leads?pageSize=20','consultant'),('/api/dso/v1/operations/summary','operator')]

def measure(index):
    path,role=requests[index%len(requests)]
    request=urllib.request.Request(base+path,headers={'Authorization':'Bearer '+fixture['staffTokens'][role],'Accept-Language':'en'})
    start=time.perf_counter()
    try:
        with urllib.request.urlopen(request,timeout=20) as response:
            body=json.load(response)
            status=response.status if body.get('code')==0 else 500
    except urllib.error.HTTPError as error:
        status=error.code
    except (OSError,ValueError):
        status=0
    return status,(time.perf_counter()-start)*1000

report={'environment':'isolated-synthetic','method':'read-only mixed context/patients/CRM/operations, warm small dataset','runtimeLimits':{'apiCpu':1,'apiMemoryMiB':512,'postgresCpu':0.5,'postgresMemoryMiB':256},'productionCapacityGuarantee':False,'samples':[]}
for concurrency in [1,5,20]:
    started=time.perf_counter()
    with concurrent.futures.ThreadPoolExecutor(max_workers=concurrency) as pool:
        values=list(pool.map(measure,range(200)))
    elapsed=time.perf_counter()-started
    latencies=sorted(value[1] for value in values)
    counts={str(status):sum(value[0]==status for value in values) for status in set(value[0] for value in values)}
    sample={'concurrency':concurrency,'requests':len(values),'elapsedSeconds':round(elapsed,3),'requestsPerSecond':round(len(values)/elapsed,1),'p50Ms':round(statistics.median(latencies),1),'p95Ms':round(latencies[int(len(latencies)*0.95)-1],1),'p99Ms':round(latencies[int(len(latencies)*0.99)-1],1),'statuses':counts}
    report['samples'].append(sample)
    print(json.dumps(sample))
Path(args.output).write_text(json.dumps(report,indent=2)+'\n')
if any(sample['statuses']!={'200':sample['requests']} for sample in report['samples']):
    raise SystemExit('Benchmark contained failed requests')
