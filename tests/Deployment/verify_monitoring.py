"""P06-06 local receiver, bounded alerts and measured isolated-source recovery."""
import copy
import hashlib
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
import json
from pathlib import Path
import re
import ssl
import subprocess
import sys
import threading
import time
from unittest.mock import patch
import uuid
import urllib.request

sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'deploy'))
import initialize_synthetic as fixture
import synthetic_recovery as recovery
import monitor_synthetic as monitor
from verify_recovery import seed
from verify_synthetic import mfa
from verify_isolation import request

check=fixture.require


def self_checks(folder):
    now=time.time();sample=dict(app_live=True,app_ready=True,database=True,observer=True,host_disk=0,database_disk=0,app_disk=0,tls=now+3*86400)
    for value,expected in ((69.99,None),(70,'warning'),(84.99,'warning'),(85,'critical'),(100,'critical')):
        check(monitor.signals(dict(sample,host_disk=value),None,now).get('host_disk')==expected,'Disk eşik sınırı yanlış.')
    for delta,expected in ((2*86400+1,None),(2*86400,'warning'),(86400,'critical'),(0,'critical')):
        check(monitor.signals(dict(sample,tls=now+delta),None,now).get('tls')==expected,'TLS eşik sınırı yanlış.')
    payload=folder/'digest-check.bin';payload.write_bytes(b'SYNTHETIC')
    receipt=dict(firm='fr-synthetic-unit-a',failed=False,good=dict(package=str(payload.relative_to(ROOT)),sha256=fixture.file_hash(payload),createdUtc=now-86400))
    check('backup_age' not in monitor.signals(sample,receipt,now) and monitor.signals(sample,receipt,now+.01)['backup_age']=='warning','Yedek yaş sınırı yanlış.')
    payload.write_bytes(b'SYNTHETIC-CHANGED')
    check(monitor.signals(sample,receipt,now)['backup_integrity']=='critical','Değişmiş yedek hash alarmı yok.')
    state=dict(firm='fr-synthetic-unit-a',active={},pending=[],attempts=0,nextAttempt=0,receiverPort=12345)
    check(monitor.advance(state,{'database':'critical'},now)==1,'İlk olay yok.')
    event=copy.deepcopy(state['pending'][0]);monitor.validate_event(event,state['firm'])
    check(monitor.advance(state,{'database':'critical'},now)==0 and state['pending']==[event],'Tekrar alarm seli.')
    check(monitor.advance(state,{},now)==1 and state['pending'][-1]['level']=='resolved','Tek düzelme olayı yok.')
    retry=folder/'retry-self-check';fixture.private_directory(retry)
    with patch.object(monitor,'send',side_effect=OSError('SYNTHETIC-NOT-LOGGED')) as sender:
        for when in (now,now+1,now+30,now+31,now+90,now+1000):monitor.deliver(retry,state,when)
        check(sender.call_count==3 and state['attempts']==3 and len(state['pending'])==2,'Üç deneme sınırı veya kayıt koruması bozuk.')
    tampered=dict(event,extra='SYNTHETIC-SECRET')
    try:monitor.validate_event(tampered,state['firm'])
    except RuntimeError:pass
    else:raise RuntimeError('Allowlist dışı alıcı alanı kabul edildi.')


def verify(options):
    check(Path.cwd().resolve()==ROOT and re.fullmatch(r'fr-synthetic-[a-z0-9][a-z0-9-]{0,24}',options.instance),'Depo kökü ve sentetik kimlik gerekli.')
    check(len(set(options.ports))==3 and all(1024<=p<=65532 and p!=8080 for p in options.ports) and options.ports[0]+3 not in [8080,*options.ports],'Kabul portları geçersiz.')
    folder=ROOT/'.local/monitoring-acceptance'/options.instance;source=ROOT/'.local/synthetic'/options.instance
    target='fr-restore-'+options.instance.removeprefix('fr-synthetic-')
    target_root=ROOT/'.local/recovery-targets'/target
    check(not folder.exists() and not source.exists() and not target_root.exists(),'Yeni kabul/kaynak/hedef gerekli.')
    fixture.private_directory(folder);self_checks(folder)
    inbox={};requests=[];receiver_mode=dict(lose_ack=False)
    class Receiver(BaseHTTPRequestHandler):
        def log_message(self,*args):pass
        def do_POST(self):
            try:
                length=int(self.headers.get('Content-Length','0'));check(self.path=='/events' and 0<length<=16384,'Alıcı istek sınırı.')
                events=json.loads(self.rfile.read(length));check(isinstance(events,list) and 0<len(events)<=64,'Alıcı olay sınırı.')
                for event in events:monitor.validate_event(event,options.instance+'-a')
                requests.append([e['id'] for e in events])
                for event in events:
                    if event['id'] in inbox:check(inbox[event['id']]==event,'Aynı kimlik/farklı olay.')
                    inbox[event['id']]=event
                monitor.save(folder/'inbox.json',list(inbox.values()))
                lost=receiver_mode['lose_ack'];receiver_mode['lose_ack']=False
                self.send_response(503 if lost else 200);self.send_header('Content-Type','application/json');self.end_headers()
                self.wfile.write(json.dumps({'accepted':[e['id'] for e in events]}).encode())
            except (RuntimeError,ValueError,KeyError,OSError):self.send_error(400)
    server=ThreadingHTTPServer(('127.0.0.1',0),Receiver);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
    compose=['docker','compose','-f',str(source/'compose.json')];moved=[];started=time.monotonic()
    package=lambda phase:ROOT/'.local/recovery-packages'/options.instance/phase/'firm.frbk'
    key=lambda phase:ROOT/'.local/recovery-keys'/options.instance/phase/'recovery.key'
    def scan():return monitor.scan(options.instance)
    try:
        fixture.initialize(options)
        account=recovery.env_bytes((source/'private/owner-a.env').read_bytes())
        client=mfa(f'http://127.0.0.1:{options.ports[0]}',account['APP_BOOTSTRAP_OWNER_PASSWORD'],account['APP_BOOTSTRAP_OWNER_EMAIL'],source/'private/ca.pem')[0]
        seed(options.instance+'-db-a',options.instance+'-a')
        before_b=recovery.snapshot(options.instance+'-db-b');b_start=fixture.run(['docker','inspect','--format','{{.State.StartedAt}}',options.instance+'-app-b'])
        monitor.arm(options.instance,server.server_port)
        state_path=ROOT/'.local/monitoring'/options.instance/'state.json';original_state=state_path.read_bytes()
        changed=json.loads(original_state);changed['ports'][0],changed['ports'][1]=changed['ports'][1],changed['ports'][0]
        monitor.save(state_path,changed)
        try:
            try:scan()
            except RuntimeError:pass
            else:raise RuntimeError('B portuna değişmiş izleme bağı kabul edildi.')
            check(not inbox,'Yanlış hedefte alıcıya olay gönderildi.')
        finally:state_path.write_bytes(original_state)
        first=scan();check(first['active'].get('backup_missing')=='critical' and not first['pending'],'Eksik yedek alıcıya gitmedi.')
        count=len(inbox);check(scan()['emitted']==0 and len(inbox)==count,'Değişmeyen durum alarm seli.')
        for invalid in ('firma-randevu-local',options.instance+'-b'):
            try:monitor.scan(invalid)
            except (RuntimeError,OSError):pass
            else:raise RuntimeError('Yanlış izleme hedefi kabul edildi.')
        monitor.backup(options.instance,package('first'),key('first'))
        fixture.run(compose+['start','app-a','operator']);fixture.ready(options.ports,source/'private/ca.pem')
        check('backup_missing' not in scan()['active'],'Güncel yedek düzelmesi yok.')
        try:monitor.backup(options.instance,package('first'),key('first'))
        except RuntimeError:pass
        else:raise RuntimeError('Tekrar yedek yolu kabul edildi.')
        check(scan()['active'].get('backup_failure')=='critical','Gerçek başarısız yedek alarmı yok.')
        monitor.backup(options.instance,package('second'),key('second'))
        fixture.run(compose+['start','app-a','operator']);fixture.ready(options.ports,source/'private/ca.pem')
        check('backup_failure' not in scan()['active'],'Yeni doğrulanmış yedek failure olayını çözmedi.')
        print('P06-06: Yanlış hedef bağı, eksik/başarısız/yeni yedek ve alıcı olayları geçti.',flush=True)
        receipt=monitor.read(ROOT/'.local/monitoring'/options.instance/'backup.json');meta,_,_=recovery.read_package(package('second'),key('second'),options.instance+'-a')
        base=monitor.collect(ROOT/'.local/monitoring'/options.instance,monitor.load(options.instance)[1])
        # Eşik saati/ölçümü enjeksiyondur; gerçek diski doldurmaz veya TLS doğrulamasını kapatmaz.
        with patch.object(monitor,'collect',return_value=base):
            stale=monitor.scan(options.instance,now=meta['createdUtc']+86401);check(stale['active'].get('backup_age')=='warning','Eski yedek uyarısı yok.')
        scan()
        for value,level in ((70,'warning'),(85,'critical')):
            with patch.object(monitor,'collect',return_value=dict(base,host_disk=value,tls=time.time()+(36 if value==70 else 12)*3600)):
                result=scan();check(result['active'].get('host_disk')==level and result['active'].get('tls')==level,'Disk/TLS seviye geçişi yok.')
        scan()
        fixture.run(compose+['stop','app-a'])
        receiver_mode['lose_ack']=True
        down=scan();check(down['active'].get('app_live')=='critical' and down['pending']>0,'Uygulama arızası/yanıt kaybı yok.')
        pending=monitor.load(options.instance)[1]['pending'];prior=len(inbox);sent=len(requests)
        restarted=subprocess.run([sys.executable,'-X','utf8','deploy/monitor_synthetic.py','scan','--instance',options.instance],capture_output=True,timeout=45)
        check(restarted.returncode==3 and len(requests)==sent,'Yeni süreç backoff/kalıcı kuyruğu kaybetti.')
        check(monitor.scan(options.instance,now=time.time()+31)['pending']==0 and len(inbox)==prior and requests[-1]==[e['id'] for e in pending],'Yanıt kaybı aynı kimlikle tek mantıksal teslim olmadı.')
        fixture.run(compose+['start','app-a']);recovery.ready(options.ports[0]);scan()
        fixture.run(compose+['stop','db-a'])
        down=scan();check(down['samples']['app_live'] and not down['samples']['app_ready'] and down['active'].get('database')=='critical','App live/DB readiness ayrımı yok.')
        with urllib.request.urlopen(f'https://127.0.0.1:{options.ports[2]}/health/ready',context=ssl.create_default_context(cafile=str(source/'private/ca.pem')),timeout=5) as response:check(response.status==200,'A DB kapalıyken merkez sağlıklı olmalı; firma sağlığı ayrı kalır.')
        fixture.run(compose+['start','db-a']);recovery.ready(options.ports[0]);scan()
        try:
            disk=subprocess.run(['docker','exec',options.instance+'-app-a','dd','if=/dev/zero','of=/tmp/p06-monitor-limit','bs=1M','count=65','status=none'],capture_output=True,timeout=20)
            check(disk.returncode!=0 and b'No space left on device' in disk.stderr,'Gerçek 64MiB tmpfs sınırı yok.')
            check(scan()['active'].get('app_disk')=='critical','Gerçek tmpfs doluluk alarmı yok.')
        finally:fixture.run(['docker','exec',options.instance+'-app-a','rm','-f','/tmp/p06-monitor-limit'])
        scan()
        check(recovery.snapshot(options.instance+'-db-b')==before_b and fixture.run(['docker','inspect','--format','{{.State.StartedAt}}',options.instance+'-app-b'])==b_start,'A arızaları B verisi/başlangıcını etkiledi.')
        with urllib.request.urlopen(f'http://127.0.0.1:{options.ports[1]}/health/ready',timeout=3) as response:check(response.status==200,'A arızalarında B kapandı.')
        print('P06-06: App/DB/tmpfs, TLS-disk-yaş eşikleri, yanıt kaybı/yeniden süreç ve B koruması geçti.',flush=True)
        stage=ROOT/'.local/monitoring-staged'/options.instance;stage_key=ROOT/'.local/monitoring-staged-keys'/options.instance
        fixture.private_directory(stage);fixture.private_directory(stage_key)
        (stage/'input.frbk').write_bytes(package('second').read_bytes());(stage_key/'recovery.key').write_bytes(key('second').read_bytes())
        # Bu yeni mali işaret yedekten sonra commit edilir; restore'da yokluğu RPO sınırını kanıtlar.
        recovery.sql(options.instance+'-db-a',f'''INSERT INTO "SubscriptionPayments" ("Id","PeriodId","AmountTry","PaidOn","Reference","RequestKey","RequestHash","RecordedBy","RecordedAt") SELECT '{uuid.uuid4()}',"Id",600,CURRENT_DATE,'SYNTHETIC-AFTER-BACKUP','{uuid.uuid4()}',repeat('C',64),'{uuid.uuid4()}',clock_timestamp() FROM "SubscriptionPeriods";''')
        post_marker_at=float(recovery.sql(options.instance+'-db-a','SELECT extract(epoch FROM "RecordedAt") FROM "SubscriptionPayments" WHERE "Reference"=\'SYNTHETIC-AFTER-BACKUP\';'))
        loss_utc=time.time();loss=time.monotonic();fixture.run(compose+['stop'])
        for original in (source,ROOT/'.local/recovery-packages'/options.instance,ROOT/'.local/recovery-keys'/options.instance):
            unavailable=original.with_name(original.name+'.unavailable');fixture.no_links(original);fixture.no_links(unavailable)
            check(original.resolve().is_relative_to((ROOT/'.local').resolve()) and unavailable.resolve().is_relative_to((ROOT/'.local').resolve()) and not unavailable.exists(),'Kayıp simülasyon hedefi geçersiz.')
            original.rename(unavailable);moved.append((original,unavailable))
        lost=scan();check(not lost['samples']['app_live'] and not lost['samples']['database'] and lost['active'].get('backup_integrity')=='critical','Kaynak/özgün paket yokken kayıp sinyali yok.')
        detection=time.monotonic()-loss
        start=time.monotonic();inputs=folder/'input';fixture.private_directory(inputs);input_key=ROOT/'.local/monitoring-keycopies'/options.instance;fixture.private_directory(input_key)
        copied=inputs/'firm.frbk';copied_key=input_key/'recovery.key';copied.write_bytes((stage/'input.frbk').read_bytes());copied_key.write_bytes((stage_key/'recovery.key').read_bytes());acquire=time.monotonic()-start
        start=time.monotonic();recovery.read_package(copied,copied_key,options.instance+'-a');verify_seconds=time.monotonic()-start
        start=time.monotonic();restored=recovery.restore(copied,copied_key,options.instance+'-a',target,options.ports[0]+3);restore_seconds=time.monotonic()-start
        start=time.monotonic();db=target+'-db';base_url=f'http://127.0.0.1:{options.ports[0]+3}'
        check(restored['rawRestoreEqual'] and request(client,base_url+'/api/auth/me')[0]==200,'Tam restore/DP eski oturum başarısız.')
        check(request(client,base_url+'/api/subscription')[1]['periods'][0]['outstandingTry']=='600.00','Yedek anı kısmi borç geri gelmedi.')
        check(recovery.sql(db,'SELECT count(*) FROM "SubscriptionPayments" WHERE "Reference"=\'SYNTHETIC-RESTORE\';')=='1' and recovery.sql(db,'SELECT count(*) FROM "SubscriptionPayments" WHERE "Reference"=\'SYNTHETIC-AFTER-BACKUP\';')=='0','Kurtarma noktası işareti yanlış.')
        restored_marker_at=float(recovery.sql(db,'SELECT extract(epoch FROM "RecordedAt") FROM "SubscriptionPayments" WHERE "Reference"=\'SYNTHETIC-RESTORE\';'))
        check(set(restored['securityChangedTables'])=={'PublicAppointmentAccesses','BookingVerifications','BookingSmsBudgetPolicies','BookingSmsAttempts','AppointmentReminderJobs'},'Kurtarma güvenlik iptalleri eksik.')
        accept_seconds=time.monotonic()-start;total=time.monotonic()-loss
        check(scan()['emitted']==0,'Kayıp kaynak kendiliğinden düzeldi sayıldı.')
        for event in inbox.values():monitor.validate_event(event,options.instance+'-a')
        fixture.write_json(folder/'acceptance.json',dict(pass_=True,instance=options.instance,target=target,events=len(inbox),receiverRequests=len(requests),dedupLostAck=True,processRestartBackoff=True,retryCap=3,
            realAppDbFailures=True,realTmpfsFull=True,realTlsPeerVerified=True,clockAndThresholdInjection=True,backupFailureRecorded=True,bPreservedBeforeHostLoss=True,
            sourceStoppedAndUnavailable=True,originalPackagesKeysUnavailable=True,rawRestoreEqual=True,tableCount=len(meta['snapshot']['tables']),oldCookieWorks=True,securityRevocations=True,
            restoredPreBackupMarker=True,postBackupPaymentAbsent=True,restoredMarkerRecordedAtUtc=restored_marker_at,postBackupMarkerRecordedAtUtc=post_marker_at,backupCreatedUtc=meta['createdUtc'],simulatedLossUtc=loss_utc,recoveryPointGapSeconds=round(loss_utc-meta['createdUtc'],3),initialSamples=first['samples'],
            components=dict(stopRenameDetectSeconds=round(detection,3),localInputAcquisitionSeconds=round(acquire,3),packageValidationSeconds=round(verify_seconds,3),provisionRestoreRevokeReadySeconds=round(restore_seconds,3),acceptanceSeconds=round(accept_seconds,3)),
            localRecoveryTotalSeconds=round(total,3),totalAcceptanceSeconds=round(time.monotonic()-started,3),physicalHostOffsiteRpoRtoProven=False,mainTouched=False))
        print('P06-06: Sahte alıcı/kalıcı dedupe-backoff, gerçek app/DB/tmpfs, yedek/TLS eşikleri ve kaynak yokken ölçülmüş temiz kurtarma geçti.',flush=True)
        print('P06-06-EVIDENCE:'+json.dumps(monitor.read(folder/'acceptance.json')),flush=True)
    finally:
        if target_root.exists():fixture.run(['docker','compose','-f',str(target_root/'compose.json'),'stop'])
        for original,unavailable in reversed(moved):
            if original==source and unavailable.exists():unavailable.rename(original)
        if source.exists():fixture.run(compose+['stop'])
        server.shutdown();server.server_close();thread.join(timeout=5)


if __name__=='__main__':
    try:verify(fixture.arguments())
    except (RuntimeError,OSError,ValueError,KeyError,TypeError) as error:
        print(str(error) if isinstance(error,RuntimeError) else 'Sentetik izleme kabulü başarısız; özel kayıtlar korunuyor.',file=sys.stderr);sys.exit(1)
