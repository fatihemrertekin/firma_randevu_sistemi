"""P06-04 portable encrypted single-firm restore, with the entire source unavailable."""
import base64
from datetime import datetime, timedelta, timezone
import hashlib
import hmac
import http.cookiejar
import io
import json
from pathlib import Path
import secrets
import shutil
import struct
import sys
import time
import urllib.request
import uuid
import zipfile

sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'deploy'))
import initialize_synthetic as fixture
import synthetic_recovery as recovery
from verify_synthetic import mfa
from verify_isolation import request


def check(value,message):fixture.require(value,message)


def seed(container,firm):
    uid=lambda:str(uuid.uuid4())
    now=datetime.now(timezone.utc).replace(microsecond=0)
    at=lambda minutes:(now+timedelta(minutes=minutes)).isoformat()
    token=secrets.token_urlsafe(32);receipt=secrets.token_urlsafe(32)
    digest=lambda s:hashlib.sha256(s.encode()).hexdigest().upper()
    staff,service,appointment,version,audit,budget,job,period=[uid() for _ in range(8)]
    def insert(table,values):
        columns=','.join('"'+k+'"' for k in values)
        payload=json.dumps(values).replace("'","''")
        recovery.sql(container,f'''INSERT INTO "{table}" ({columns}) SELECT {columns} FROM json_populate_record(NULL::"{table}",'{payload}'::json);''')
    insert('StaffMembers',dict(Id=staff,Name='Sentetik çalışan',IsActive=True,IsDeleted=False,Version=uid()))
    insert('ServiceDefinitions',dict(Id=service,Name='Sentetik hizmet',DurationMinutes=30,BufferBeforeMinutes=0,BufferAfterMinutes=0,Price=150,Currency='TRY',IsActive=True,IsDeleted=False,Version=uid()))
    insert('Appointments',dict(Id=appointment,Source='Public',BookingPolicyVersion=uid(),CustomerCancellationLeadMinutes=120,RequiresBusinessApproval=False,
        RequestKey=uid(),RequestHash=digest('request'),CustomerName='Sentetik müşteri',CustomerPhone='+905000000001',CreatedAt=at(-5),Version=version))
    insert('AppointmentTimeBlocks',dict(AppointmentId=appointment,StaffMemberId=staff,ServiceDefinitionId=service,StaffName='Sentetik çalışan',ServiceName='Sentetik hizmet',
        DurationMinutes=30,BufferBeforeMinutes=0,BufferAfterMinutes=0,Price=150,Currency='TRY',StartsAt=at(180),EndsAt=at(210),OccupiedStartsAt=at(180),OccupiedEndsAt=at(210),Status='Cancelled'))
    insert('AppointmentAudits',dict(Id=audit,AppointmentId=appointment,AppointmentVersion=version,OccurredAt=at(-1),Kind='PublicCancelled'))
    insert('PublicAppointmentAccesses',dict(AppointmentId=appointment,TokenHash=digest(token),Version=uid(),FirstIssuedAt=at(-4),ExpiresAt=at(60),
        CancellationRequestKey=uid(),CancellationExpectedVersion=version,CancellationAuditId=audit))
    insert('BookingVerifications',dict(Id=uid(),RequestKey=uid(),RequestHash=digest('receipt-request'),SecretHash=digest(receipt),PhoneHash=digest('phone'),IpHash=digest('ip'),
        UsedCodeHashes=[],CodeVersion=1,FailedAttempts=0,CreatedAt=at(-5),ExpiresAt=at(5),ResendAt=at(-4),VerifiedAt=at(-3),VerifiedUntil=at(2),UpdatedAt=at(-1),AppointmentId=appointment,ConsumedAt=at(-1),ReplayUntil=at(1439)))
    insert('BookingSmsBudgetPolicies',dict(Id=budget,InstanceId=firm,KeyId=digest('inactive-key'),UnitPriceTry=.10,DailyCalls=2,MonthlyCalls=10,TotalCalls=10,
        DailyTry=1,MonthlyTry=2,TotalTry=2,StartsAt=at(-10),EndsAt=at(1440)))
    insert('AppointmentReminderJobs',dict(Id=job,AppointmentId=appointment,AppointmentVersion=version,BudgetApprovalId=budget,StartsAt=at(180),DueAt=at(0),Deadline=at(15),
        Status='Processing',ProtectedPhone='SYNTHETIC-UNSENT',Attempts=1,NextAttemptAt=at(0),LeaseId=uid(),LeaseUntil=at(1),StartedOnceAt=at(0)))
    insert('BookingSmsAttempts',dict(Purpose='Reminder',JobId=job,Number=1,PolicyId=budget,ReservedTry=.10,ReservedAt=at(-1),StartedAt=at(0),LeaseId=uid(),Outcome='Started',RetainUntil=at(44640)))
    first=now.date().replace(day=1);following=(first.replace(day=28)+timedelta(days=4)).replace(day=1)
    insert('SubscriptionPeriods',dict(Id=period,StartsOn=str(first),EndsOn=str(following),DueOn=str(first),AmountTry=1000,Version=uid(),RecordedBy=uid(),RecordedAt=at(-3)))
    insert('SubscriptionPayments',dict(Id=uid(),PeriodId=period,AmountTry=400,PaidOn=str(now.date()),Reference='SYNTHETIC-RESTORE',RequestKey=uid(),RequestHash=digest('payment'),RecordedBy=uid(),RecordedAt=at(-2)))
    return dict(appointment=appointment,token=token,receiptHash=digest(receipt),version=version)


def verify(options):
    source=ROOT/'.local/synthetic'/options.instance
    report_root=ROOT/'.local/recovery-acceptance'/options.instance
    target='fr-restore-'+options.instance.removeprefix('fr-synthetic-')
    target_root=ROOT/'.local/recovery-targets'/target
    check(not report_root.exists() and not source.exists() and not target_root.exists(),'Yeni kaynak/hedef/kabul ID gerekli.')
    fixture.private_directory(report_root)
    package=ROOT/'.local/recovery-packages'/options.instance/'firm.frbk'
    key=ROOT/'.local/recovery-keys'/options.instance/'recovery.key'
    source_compose=['docker','compose','-f',str(source/'compose.json')]
    target_compose=['docker','compose','-f',str(target_root/'compose.json')]
    moved=None;started=time.monotonic()
    try:
        fixture.initialize(options)
        private=source/'private';account=recovery.env_bytes((private/'owner-a.env').read_bytes())
        client,jar=mfa(f'http://127.0.0.1:{options.ports[0]}',account['APP_BOOTSTRAP_OWNER_PASSWORD'],account['APP_BOOTSTRAP_OWNER_EMAIL'],private/'ca.pem')
        firm=options.instance+'-a';container=options.instance+'-db-a'
        test=seed(container,firm)
        before=recovery.snapshot(container)
        check(before['tables']['Appointments'].startswith('1:') and before['tables']['SubscriptionPayments'].startswith('1:'),'Kabul verisi eksik.')
        backup=recovery.backup(source,package,key)
        print('P06-04: Tutarlı tek firma DB/DP/config şifreli paketi hazır.',flush=True)
        plain=recovery.crypt(package.read_bytes(),key,'Decrypt')
        with zipfile.ZipFile(io.BytesIO(plain)) as stream:
            entries={n:stream.read(n) for n in stream.namelist()}
        for name in ('app-b.env','operator-bootstrap.env'):
            values=recovery.env_bytes((private/name).read_bytes())
            for k,v in values.items():
                secret=v.split('Password=',1)[1].split(';')[0] if 'Password=' in v else v if 'PASSWORD' in k else None
                if secret:check(all(secret.encode() not in data for data in entries.values()),'Başka firma/operatör sırrı pakete girdi.')
        copied=report_root/'input.frbk';copied.write_bytes(package.read_bytes())
        key_copy=ROOT/'.local/recovery-keycopies'/options.instance/'recovery.key';fixture.private_directory(key_copy.parent);key_copy.write_bytes(key.read_bytes())
        if sys.platform!='win32':key_copy.chmod(0o600)
        fixture.run(source_compose+['stop'])
        moved=source.with_name(source.name+'-unavailable');check(not moved.exists(),'Kaynak gizleme hedefi dolu.')
        source.rename(moved)
        original_package=package.with_suffix('.unavailable');package.rename(original_package)
        original_key=key.with_suffix('.unavailable');key.rename(original_key)
        loss=time.monotonic()
        check(not source.exists() and not package.exists() and not key.exists(),'Orijinal konumlar erişilebilir.')
        def target_inventory():
            return {kind:{n for n in names if n.startswith(target)} for kind,names in fixture.inventory().items()}
        def reject(p=copied,k=key_copy,f=firm,t=target,port=options.ports[0]+3):
            inventory=target_inventory()
            try:recovery.restore(p,k,f,t,port)
            except (RuntimeError,ValueError,KeyError,OSError,zipfile.BadZipFile):pass
            else:raise RuntimeError('Geçersiz kurtarma kabul edildi.')
            check(not target_root.exists() and target_inventory()==inventory,'Negatif kabul hedef kaynağı oluşturdu.')
        wrong=report_root/'wrong.key';wrong.write_bytes(secrets.token_bytes(32));reject(k=wrong)
        bad=report_root/'bad.frbk';data=bytearray(copied.read_bytes());data[-1]^=1;bad.write_bytes(data);reject(p=bad)
        reject(f=firm+'-wrong');reject(port=8080);reject(t='firma-randevu-local')
        # Geçerli şifreleme fakat eksik bileşen, hash hatası, yol enjeksiyonu ve bozuk dump da reddedilir.
        for kind in ('missing','hash','path','dump','schema','role-password','config'):
            changed=dict(entries)
            if kind=='missing':changed.pop('db.key')
            elif kind=='hash':changed['db.key']+=b'x'
            elif kind=='path':changed['../outside']=b'no'
            elif kind=='dump':
                changed['database.dump']=b'not-pgdump';meta=json.loads(changed['manifest.json']);meta['files']['database.dump']=hashlib.sha256(changed['database.dump']).hexdigest();changed['manifest.json']=json.dumps(meta).encode()
            elif kind=='schema':
                meta=json.loads(changed['manifest.json']);meta['snapshot']['schema']='0'*64;changed['manifest.json']=json.dumps(meta).encode()
            else:
                name='target-password' if kind=='role-password' else 'app.env'
                changed[name]=b'bad' if kind=='role-password' else changed[name].replace(b'ASPNETCORE_ENVIRONMENT=Development',b'ASPNETCORE_ENVIRONMENT=Production')
                meta=json.loads(changed['manifest.json']);meta['files'][name]=hashlib.sha256(changed[name]).hexdigest();changed['manifest.json']=json.dumps(meta).encode()
            stream=io.BytesIO()
            with zipfile.ZipFile(stream,'w') as output:
                for n,v in changed.items():output.writestr(n,v)
            invalid=report_root/(kind+'.frbk');invalid.write_bytes(recovery.crypt(stream.getvalue(),key_copy,'Encrypt'));reject(p=invalid)
        print('P06-04: Yanlış anahtar/bozuk-eksik paket/yol/dump/firma/hedef reddi kaynak oluşturmadan geçti.',flush=True)
        result=recovery.restore(copied,key_copy,firm,target,options.ports[0]+3)
        check(result['rawRestoreEqual'],'Ham restore karşılaştırması eksik.')
        restored=target+'-db';base=f'http://127.0.0.1:{options.ports[0]+3}'
        check(request(client,base+'/api/auth/me')[0]==200,'DP anahtarları eski MFA oturumunu çözemedi.')
        status,summary=request(client,base+'/api/subscription')
        check(status==200 and summary['periods'][0]['outstandingTry']=='600.00','Abonelik/kısmi ödeme geçmişi yanlış.')
        check(set(result['securityChangedTables'])=={'PublicAppointmentAccesses','BookingVerifications','BookingSmsBudgetPolicies','BookingSmsAttempts','AppointmentReminderJobs'},'Güvenlik iptal farkları eksik.')
        check(recovery.sql(restored,'SELECT "RevokedAt" IS NOT NULL FROM "BookingSmsBudgetPolicies";')=='t','Bütçe iptal edilmedi.')
        check(recovery.sql(restored,'SELECT "Outcome" FROM "BookingSmsAttempts";')=='Unknown','Belirsiz gönderim tarihi sıfırlandı.')
        check(recovery.sql(restored,'SELECT "Status" || \':\' || ("ProtectedPhone" IS NULL)::text FROM "AppointmentReminderJobs";')=='Unknown:true','Kuyruk iptal edilmedi.')
        check(recovery.sql(restored,'SELECT "SecretHash" FROM "BookingVerifications";')!=test['receiptHash'],'Eski makbuz sırrı korundu.')
        # Yalnız okuma erişimini aç; gerçek taşıyıcı/OTP/dispatch her zaman kapalıdır.
        app_env=recovery.env_bytes((target_root/'private/app.env').read_bytes())
        fixture.environment(target_root/'private/app.env',dict(app_env,PublicBooking__AccessEnabled='true'))
        try:
            fixture.run(target_compose+['up','-d','--no-build','app']);recovery.ready(options.ports[0]+3)
            anonymous=urllib.request.build_opener()
            path=base+'/api/public-booking/appointments/'+test['appointment']
            check(request(anonymous,path,headers={'Authorization':'Bearer '+test['token']})[0]==403,'Eski randevu tokenı yeniden açıldı.')
        finally:
            fixture.environment(target_root/'private/app.env',app_env)
        fixture.run(target_compose+['up','-d','--no-build','app']);recovery.ready(options.ports[0]+3)
        stable=recovery.snapshot(restored)
        for _ in range(2):
            fixture.run(target_compose+['restart']);recovery.ready(options.ports[0]+3)
            check(recovery.snapshot(restored)==stable,'İki restart veriyi/yetkiyi değiştirdi.')
            check(request(client,base+'/api/auth/me')[0]==200,'Restart DP oturumunu bozdu.')
        # Yeniden parola+MFA girişi: kurulum/bootstrap çalıştırmadan mevcut hesap kullanılır.
        fresh=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        def post(route,body):
            csrf=request(fresh,base+'/api/auth/csrf')[1]['token']
            return request(fresh,base+'/api/auth/'+route,body,{'X-CSRF-TOKEN':csrf})
        check(post('login',dict(email=account['APP_BOOTSTRAP_OWNER_EMAIL'],password=account['APP_BOOTSTRAP_OWNER_PASSWORD']))[0]==202,'Restore MFA zorunluluğunu kaybetti.')
        authenticator=recovery.sql(restored,'SELECT "Value" FROM "AspNetUserTokens" WHERE "Name"=\'AuthenticatorKey\';')
        raw=base64.b32decode(authenticator+'='*((-len(authenticator))%8));mac=hmac.new(raw,struct.pack('>Q',int(time.time())//30),hashlib.sha1).digest();offset=mac[-1]&15
        code=str((struct.unpack('>I',mac[offset:offset+4])[0]&0x7fffffff)%1000000).zfill(6)
        check(post('mfa/login',dict(code=code))[0]==204,'Restore sonrası gerçek MFA girişi başarısız.')
        current=target_inventory()
        try:recovery.restore(copied,key_copy,firm,target,options.ports[0]+3)
        except RuntimeError:pass
        else:raise RuntimeError('Mevcut hedef üstüne restore kabul edildi.')
        check(target_inventory()==current and recovery.snapshot(restored)==stable,'Tekrar restore hedefi değiştirdi.')
        logs=recovery.binary(['docker','logs',target+'-app'])
        for secret in (account['APP_BOOTSTRAP_OWNER_PASSWORD'],test['token'],authenticator):check(secret.encode() not in logs,'Logda kabul sırrı var.')
        fixture.write_json(report_root/'acceptance.json',dict(pass_=True,source=options.instance,target=target,rawRestoreEqual=True,
            tableCount=len(before['tables']),securityChangedTables=result['securityChangedTables'],originalSourceStoppedAndUnavailable=True,
            copiedKeyAndPackageOnly=True,oldCookieDecrypted=True,oldToken403=True,receiptAndBudgetRevoked=True,reminderUnknown=True,
            freshMfa=True,twoRestarts=True,mainTouched=False,plaintextArchiveOnDisk=False,encryptedBytes=backup['encryptedBytes'],
            localRecoverySeconds=round(time.monotonic()-loss,3),totalAcceptanceSeconds=round(time.monotonic()-started,3),offsiteHostLossProven=False))
        print('P06-04: Temiz restore, tüm tablo/şema/rol/grant, DP/MFA/borç, iptaller ve iki restart geçti.',flush=True)
    finally:
        if target_root.exists():fixture.run(target_compose+['stop'])
        if moved and moved.exists():moved.rename(source)
        if source.exists():fixture.run(source_compose+['stop'])


if __name__=='__main__':
    try:verify(fixture.arguments())
    except (RuntimeError,ValueError,KeyError,OSError,zipfile.BadZipFile) as error:
        print(str(error) if isinstance(error,RuntimeError) else 'Sentetik kurtarma kabulü başarısız; özel ayrıntılar gizlendi.',file=sys.stderr);sys.exit(1)
