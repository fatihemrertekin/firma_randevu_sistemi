"""P06-06a packaged HTTP readiness under a real paused, warm PostgreSQL connection."""
import json
from pathlib import Path
import re
import ssl
import sys
import time
import urllib.error
import urllib.request

sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'deploy'))
import initialize_synthetic as fixture
import synthetic_recovery as recovery
from synthetic_update import keys

check=fixture.require


def get(port,path='/health/ready',ca=None):
    url=f"{'https' if ca else 'http'}://127.0.0.1:{port}"+path
    context=ssl.create_default_context(cafile=str(ca)) if ca else None
    try:
        with urllib.request.urlopen(url,context=context,timeout=10) as response:return response.status,response.read(2048)
    except urllib.error.HTTPError as error:return error.code,error.read(2048)


def verify(options):
    check(Path.cwd().resolve()==ROOT and re.fullmatch(r'fr-synthetic-[a-z0-9][a-z0-9-]{0,24}',options.instance),'Depo kökü ve sentetik hedef gerekli.')
    folder=ROOT/'.local/readiness-acceptance'/options.instance;source=ROOT/'.local/synthetic'/options.instance
    check(not folder.exists() and not source.exists(),'Yeni kabul/kaynak kimliği gerekli.')
    fixture.private_directory(folder);compose=['docker','compose','-f',str(source/'compose.json')];paused=False
    try:
        fixture.initialize(options)
        # Antiforgery DP anahtarlarını üretir; hesap girişi/POST gerekmez.
        for port in options.ports[:2]:check(get(port,'/api/auth/csrf')[0]==200,'Sentetik DP hazırlığı başarısız.')
        before={suffix:recovery.snapshot(options.instance+'-db-'+suffix) for suffix in ('a','b')}
        before_keys={suffix:keys(options.instance+'-app-'+suffix) for suffix in ('a','b')}
        manifest=json.loads((source/'manifest.json').read_text())
        containers=json.loads(fixture.run(['docker','inspect',*manifest['names']['container']]))
        starts={v['Name']:v['State']['StartedAt'] for v in containers}
        for _ in range(3):check(get(options.ports[0])[0]==200,'Havuz ısıtma başarısız.')
        fixture.run(['docker','pause',options.instance+'-db-a']);paused=True
        try:
            started=time.monotonic();status,body=get(options.ports[0]);elapsed=time.monotonic()-started
            check(status==503 and not body and elapsed<10,'Sıcak havuz/yanıtsız DB 503 ve süre sınırı başarısız.')
            check(get(options.ports[0],'/health/live')[0]==200,'DB arızasında live kapandı.')
            check(get(options.ports[1])[0]==200,'A DB arızasında B etkilendi.')
            check(get(options.ports[2],ca=source/'private/ca.pem')[0]==200,'A DB arızasında merkez etkilendi.')
        finally:
            fixture.run(['docker','unpause',options.instance+'-db-a']);paused=False
        recovery.ready(options.ports[0]);check(get(options.ports[0])[0]==200,'DB açılınca readiness dönmedi.')
        for suffix in ('a','b'):
            check(recovery.snapshot(options.instance+'-db-'+suffix)==before[suffix] and keys(options.instance+'-app-'+suffix)==before_keys[suffix],'Sağlık probu veri/şema/yetki/anahtarı değiştirdi.')
        check({v['Name']:v['State']['StartedAt'] for v in json.loads(fixture.run(['docker','inspect',*manifest['names']['container']]))}==starts,'Duraklatma konteyneri yeniden başlattı.')
        check(fixture.resource_state(manifest['names'],manifest['owner'])==manifest['resources'],'Kaynak/config değişti.')
        result=dict(pass_=True,instance=options.instance,appImage=options.app_image,operatorImage=options.operator_image,warm200=True,paused503=True,failedBodyEmpty=True,
            pauseRequestSeconds=round(elapsed,3),live200=True,recovered200=True,otherFirmAndOperatorHealthy=True,tableCount=len(before['a']['tables']),bothFirmSnapshotsAndKeysPreserved=True,
            containerStartAndResourcesPreserved=True,mainTouched=False,migrationExecuted=False)
        fixture.write_json(folder/'acceptance.json',result)
        print('P06-06a-EVIDENCE:'+json.dumps(result),flush=True)
    finally:
        if paused:fixture.run(['docker','unpause',options.instance+'-db-a'])
        if (source/'compose.json').exists():fixture.run(compose+['stop'])


if __name__=='__main__':
    try:verify(fixture.arguments())
    except (RuntimeError,ValueError,KeyError,TypeError,OSError):
        print('Sentetik readiness kabulü başarısız; ayrıntılar gizlendi, kaynak/volume korunuyor.',file=sys.stderr);sys.exit(1)
