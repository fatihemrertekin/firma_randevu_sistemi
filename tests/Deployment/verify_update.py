"""P06-05 trusted local image release, failed candidate recovery and data-preserving rollback."""
import argparse
import copy
from datetime import datetime, timedelta, timezone
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tarfile
import time
import uuid
import urllib.request
from unittest.mock import patch

sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'deploy'))
import initialize_synthetic as fixture
import synthetic_recovery as recovery
import synthetic_update as update
from verify_recovery import seed
from verify_synthetic import mfa
from verify_isolation import request


def check(value,message):fixture.require(value,message)


def build(revision,root,tag):
    fixture.require(not root.exists(),'Yeni imaj build dizini gerekli.')
    fixture.private_directory(root)
    archive=recovery.binary(['git','archive',revision])
    with tarfile.open(fileobj=io.BytesIO(archive)) as stream:stream.extractall(root,filter='data')
    with (root.parent/(root.name+'-build.log')).open('wb') as log:
        try:
            result=subprocess.run(['docker','build','--pull=false','--label','org.opencontainers.image.revision='+revision,
                '-f',str(root/'deploy/Dockerfile'),'-t',tag,str(root)],stdout=log,stderr=subprocess.STDOUT,timeout=900)
        except subprocess.TimeoutExpired as error:raise RuntimeError('Sentetik imaj build süresi aşıldı; ayrı logu inceleyin.') from error
    check(result.returncode==0,'Sentetik imaj build başarısız; ayrı logu inceleyin.')
    return json.loads(fixture.run(['docker','image','inspect',tag]))[0]['Id']


def review_guard(args,baseline,candidate,catalog):
    options=argparse.Namespace(instance=args.instance+'-review',ports=[p+10 for p in args.ports],app_image=baseline,operator_image=args.operator_image,postgres_image=args.postgres_image)
    root=ROOT/'.local/synthetic'/options.instance
    command=['docker','compose','-f',str(root/'compose.json')]
    try:
        fixture.initialize(options)
        with urllib.request.urlopen(f'http://127.0.0.1:{options.ports[0]}/api/auth/csrf',timeout=10) as response:check(response.status==200,'DP anahtarı hazırlığı başarısız.')
        seed(options.instance+'-db-a',options.instance+'-a')
        package=ROOT/'.local/recovery-packages'/options.instance/'firm.frbk';key=ROOT/'.local/recovery-keys'/options.instance/'recovery.key'
        recovery.backup(root,package,key);fixture.run(command+['start','app-a','operator']);fixture.ready(options.ports,root/'private/ca.pem')
        before_b=recovery.snapshot(options.instance+'-db-b')
        b_start=fixture.run(['docker','inspect','--format','{{.State.StartedAt}}',options.instance+'-app-b'])
        original_ready=recovery.ready
        def committed_change(port):
            original_ready(port)
            recovery.sql(options.instance+'-db-a',f'''INSERT INTO "SubscriptionPayments" ("Id","PeriodId","AmountTry","PaidOn","Reference","RequestKey","RequestHash","RecordedBy","RecordedAt") SELECT '{uuid.uuid4()}',"Id",600,CURRENT_DATE,'SYNTHETIC-IN-FLIGHT','{uuid.uuid4()}',repeat('B',64),'{uuid.uuid4()}',clock_timestamp() FROM "SubscriptionPeriods";''')
            raise RuntimeError('Kontrollü sağlık hatası; bu sırada yeni mali kayıt commit edildi.')
        with patch.object(recovery,'ready',side_effect=committed_change):
            try:update.switch(options.instance,options.instance+'-a',baseline,candidate,'upgrade',catalog,package,key)
            except RuntimeError:pass
            else:raise RuntimeError('Veri değişiminde otomatik geri dönüş zorlandı.')
        state=json.loads((root/'update.json').read_text())
        check(state['status']=='review' and state['requestedImage']==candidate,'Review kaydı eksik.')
        check(not json.loads(fixture.run(['docker','inspect',options.instance+'-app-a']))[0]['State']['Running'],'Review durumunda A açık kaldı.')
        check(recovery.sql(options.instance+'-db-a','SELECT count(*) FROM "SubscriptionPayments";')=='2','Yeni commit edilmiş ödeme kayboldu.')
        check(recovery.snapshot(options.instance+'-db-b')==before_b and fixture.run(['docker','inspect','--format','{{.State.StartedAt}}',options.instance+'-app-b'])==b_start,'Review B verisi/runtime etkiledi.')
        with urllib.request.urlopen(f'http://127.0.0.1:{options.ports[1]}/health/ready',timeout=3) as response:check(response.status==200,'Review sırasında B hizmeti kapandı.')
        return options.instance
    finally:
        if (root/'compose.json').exists():fixture.run(command+['stop'])


def verify(args):
    prior=dict(mode='upgrade',previous='old',requested='new',outcome='success')
    check(not update.rollback_allowed([],'old','new') and update.rollback_allowed([prior],'new','old') and
        not update.rollback_allowed([prior],'old','new') and not update.rollback_allowed([prior,dict(mode='rollback',previous='new',requested='old',outcome='success')],'old','new'), 'Rollback yönü güncel yedek kuralını aşabiliyor.')
    check(Path.cwd().resolve()==ROOT,'Depo kökü gerekli.')
    # Komut argümanları kaynak kurulumun kendi doğrulamasından önce dosya yolu olamaz.
    import re
    check(re.fullmatch(r'fr-synthetic-[a-z0-9][a-z0-9-]{0,24}',args.instance),'Geçersiz sentetik kimlik.')
    folder=ROOT/'.local/update-acceptance'/args.instance
    source=ROOT/'.local/synthetic'/args.instance
    check(not folder.exists() and not source.exists(),'Yeni kabul/kaynak kimliği gerekli.')
    fixture.private_directory(folder)
    revision=fixture.run(['git','rev-parse','HEAD'])
    baseline=args.baseline_image or build(update.MINIMUM_REVISION,folder/'baseline','firma-randevu-update:'+args.instance+'-baseline')
    candidate_tag='firma-randevu-update:'+args.instance+'-candidate'
    candidate=build(revision,folder/'candidate',candidate_tag)
    check(baseline!=candidate,'Güncelleme farklı iki tam imaj kimliğiyle sınanmalı.')
    bad_tag='firma-randevu-update:'+args.instance+'-unready'
    # Yalnız testte: aynı uygulama yanlış portta başlar, gerçek HTTP sağlık kapısı başarısız olur.
    # BuildKit FROM alanında yerel ID'yi repository adı sayar; tek test türevini yerel etiketten üret.
    data=f'FROM {candidate_tag}\nENV ASPNETCORE_URLS=http://+:8099\n'.encode()
    recovery.binary(['docker','build','--pull=false','--label','org.opencontainers.image.revision='+revision,'-t',bad_tag,'-'],data)
    check(json.loads(fixture.run(['docker','image','inspect',candidate_tag]))[0]['Id']==candidate,'Aday build etiketi değişti.')
    bad=json.loads(fixture.run(['docker','image','inspect',bad_tag]))[0]['Id']
    catalog=folder/'releases.json'
    releases=[update.release(baseline,update.MINIMUM_REVISION),update.release(candidate,revision),update.release(bad,revision)]
    fixture.write_json(catalog,releases)
    options=argparse.Namespace(instance=args.instance,ports=args.ports,app_image=baseline,operator_image=args.operator_image,postgres_image=args.postgres_image)
    compose=['docker','compose','-f',str(source/'compose.json')]
    package=ROOT/'.local/recovery-packages'/args.instance/'firm.frbk'
    key=ROOT/'.local/recovery-keys'/args.instance/'recovery.key'
    results=[];started=time.monotonic()
    try:
        fixture.initialize(options)
        sessions={}
        for suffix,port in zip(('a','b'),args.ports[:2]):
            account=recovery.env_bytes((source/f'private/owner-{suffix}.env').read_bytes())
            sessions[suffix]=mfa(f'http://127.0.0.1:{port}',account['APP_BOOTSTRAP_OWNER_PASSWORD'],account['APP_BOOTSTRAP_OWNER_EMAIL'],source/'private/ca.pem')[0]
        # Vade ilk INSERT'te verilir; mali kayıt immutability koruması UPDATE ile aşılmaz.
        test=seed(args.instance+'-db-a',args.instance+'-a',due_on=(datetime.now(timezone.utc)-timedelta(days=2)).date())
        b_before=recovery.snapshot(args.instance+'-db-b')
        recovery.backup(source,package,key)
        fixture.run(compose+['start','app-a','operator']);fixture.ready(args.ports,source/'private/ca.pem')
        base=f'http://127.0.0.1:{args.ports[0]}'
        def canary(blocked=True):
            client=sessions['a'];check(request(client,base+'/api/auth/me')[0]==200,'MFA/DP oturumu korunmadı.')
            code,summary=request(client,base+'/api/subscription');check(code==200 and summary['periods'][0]['outstandingTry']==('600.00' if blocked else '0.00'),'Abonelik borcu değişti.')
            profile=request(client,base+'/api/business-profile/')[1]
            token=request(client,base+'/api/auth/csrf')[1]['token']
            status,body=request(client,base+'/api/business-profile/',dict(name='SYNTHETIC-BLOCKED' if blocked else 'SYNTHETIC-AFTER-UPGRADE',version=profile['version']),{'X-CSRF-TOKEN':token})
            check(status==(403 if blocked else 200),'Salt-okunur/yazma politikası farklı: '+str(status))
            if blocked:check(body.get('code')=='subscription_read_only','Yanlış ret nedeni.')
            check(request(sessions['b'],f'http://127.0.0.1:{args.ports[1]}/api/auth/me')[0]==200,'B oturumu etkilendi.')
        canary()
        before=recovery.snapshot(args.instance+'-db-a')
        params=dict(instance=args.instance,firm=args.instance+'-a',expected_image=baseline,image=candidate,mode='upgrade',catalog=catalog,package=package,key=key)
        def reject(changes):
            actual=copy.copy(params);actual.update(changes)
            old=fixture.resource_state(fixture.resource_names(args.instance),json.loads((source/'manifest.json').read_text())['owner'])
            try:update.switch(**actual)
            except (RuntimeError,ValueError,KeyError,OSError):pass
            else:raise RuntimeError('Geçersiz imaj işlemi kabul edildi.')
            check(fixture.resource_state(fixture.resource_names(args.instance),json.loads((source/'manifest.json').read_text())['owner'])==old and recovery.snapshot(args.instance+'-db-a')==before,'Ret hedefi değiştirdi.')
            check(not (source/'update.json').exists(),'Ret journal oluşturdu.')
        for invalid in [dict(firm=args.instance+'-b'),dict(instance='firma-randevu-local'),dict(image='firma-randevu:local'),
            dict(image='sha256:'+'0'*64),dict(expected_image=candidate),dict(key=folder/'missing.key'),dict(mode='rollback')]:reject(invalid)
        for kind in ('migrations','old-revision'):
            changed=copy.deepcopy(releases)
            if kind=='migrations':changed[1]['migrations']=changed[1]['migrations'][:-1]
            else:changed[1]['revision']='a6657698e5d06e45f68f939044223cdac3a83477'
            path=folder/(kind+'.json');fixture.write_json(path,changed);reject(dict(catalog=path))
        config=source/'private/app-a.env';original=config.read_bytes();config.write_bytes(original+b'\n')
        try:reject({})
        finally:config.write_bytes(original)
        print('P06-05: Yanlış firma/imaj/config/yedek/sürüm/şema retleri değişiklik yapmadan geçti.',flush=True)
        try:update.switch(**dict(params,image=bad))
        except RuntimeError:pass
        else:raise RuntimeError('Sağlıksız aday başarılı sayıldı.')
        state=json.loads((source/'update.json').read_text())
        check(state['status']=='ready' and state['image']==baseline and state['history'][-1]['outcome']=='failed_rolled_back','Başarısız aday güvenle geri dönmedi.')
        canary();check(recovery.snapshot(args.instance+'-db-a')==before,'Hata dönüşü veri değiştirdi.')
        print('P06-05: Gerçek sağlık hatasında yalnız A önceki imaja döndü; B çalışıyor.',flush=True)
        results.append(update.switch(**params));canary()
        a_id=fixture.run(['docker','inspect','--format','{{.Id}}',args.instance+'-app-a'])
        noop=update.switch(**dict(params,expected_image=candidate))
        check(noop['state']=='verified' and not noop['restarted'] and fixture.run(['docker','inspect','--format','{{.Id}}',args.instance+'-app-a'])==a_id,'Aynı sürüm gereksiz yeniden oluşturuldu.')
        # Yeni ödeme geri dönüşte korunmalı: eski DB yedeği otomatik restore edilmez.
        recovery.sql(args.instance+'-db-a',f'''INSERT INTO "SubscriptionPayments" ("Id","PeriodId","AmountTry","PaidOn","Reference","RequestKey","RequestHash","RecordedBy","RecordedAt") SELECT '{uuid.uuid4()}',"Id",600,CURRENT_DATE,'SYNTHETIC-AFTER-UPGRADE','{uuid.uuid4()}',repeat('A',64),'{uuid.uuid4()}',clock_timestamp() FROM "SubscriptionPeriods";''')
        canary(False)
        after_write=recovery.snapshot(args.instance+'-db-a')
        results.append(update.switch(**dict(params,expected_image=candidate,image=baseline,mode='rollback')))
        check(recovery.snapshot(args.instance+'-db-a')==after_write,'Rollback güncelleme sonrası yeni veriyi kaybetti.')
        client=sessions['a'];check(request(client,base+'/api/auth/me')[0]==200,'Rollback DP oturumu kayboldu.')
        check(request(client,base+'/api/subscription')[1]['periods'][0]['outstandingTry']=='0.00','Yeni ödeme kayboldu.')
        check(request(client,base+'/api/business-profile/')[1]['name']=='SYNTHETIC-AFTER-UPGRADE','Yeni işletme kaydı kayboldu.')
        check(recovery.snapshot(args.instance+'-db-b')==b_before,'B verisi değişti.')
        check(update.switch(**dict(params,image=baseline,mode='rollback'))['state']=='verified','Tekrar rollback gereksiz işlem yaptı.')
        review_instance=review_guard(args,baseline,candidate,catalog)
        fixture.write_json(folder/'acceptance.json',dict(pass_=True,instance=args.instance,baseline=baseline,candidate=candidate,baselineRevision=update.MINIMUM_REVISION,candidateRevision=revision,
            distinctImmutableImages=True,backupVerified=True,migrationExecuted=False,dbRestored=False,tableCount=len(before['tables']),failedCandidateRolledBack=True,upgradeAndRollback=True,
            newPaymentAndProfilePreserved=True,oldCookieWorks=True,subscriptionPolicyPreserved=True,otherFirmDataAndRuntimePreserved=True,noOpVerified=True,
            results=results,seconds=round(time.monotonic()-started,3),reviewOnCommittedDataDrift=True,reviewInstance=review_instance,mainTouched=False,registryOrVpsProven=False))
        print('P06-05: İki ayrı imaj arasında upgrade/rollback, yeni ödeme+profil, DP/borç politikası ve B koruması geçti.',flush=True)
    finally:
        if (source/'compose.json').exists():fixture.run(compose+['stop'])


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--instance',required=True);parser.add_argument('--ports',type=int,nargs=3,required=True)
    parser.add_argument('--operator-image',required=True);parser.add_argument('--postgres-image',required=True);parser.add_argument('--baseline-image')
    try:verify(parser.parse_args())
    except (RuntimeError,ValueError,KeyError,TypeError,OSError) as error:
        print(str(error) if isinstance(error,RuntimeError) else 'Sentetik imaj kabulü başarısız; ayrı log/manifest/journal korunuyor.',file=sys.stderr);sys.exit(1)
