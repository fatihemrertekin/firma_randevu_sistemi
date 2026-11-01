"""Recover the fake operator without its original Identity DB, DP volume or private directory."""
import base64
import hashlib
import hmac
import http.cookiejar
import io
import json
from pathlib import Path
import re
import secrets
import ssl
import struct
import sys
import time
import urllib.request
import warnings
import zipfile
from unittest.mock import patch

sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'deploy'))
import initialize_synthetic as fixture
import synthetic_operator_recovery as operator
import synthetic_recovery as recovery
from synthetic_update import keys
from verify_synthetic import mfa
from verify_isolation import request
from verify_recovery import seed

check=fixture.require


def live_keys(container):
    # docker cp arşiv API'si, gizlenen eski bind dizinine yeniden ihtiyaç duyabiliyor.
    rows=fixture.run(['docker','exec',container,'sh','-c','find /app/keys -type f -exec sha256sum {} +']).splitlines()
    result={}
    for row in rows:
        digest,name=row.split(None,1)
        check(re.fullmatch(r'[0-9a-f]{64}',digest) and re.fullmatch(r'/app/keys/key-[0-9a-f-]+\.xml',name),'Beklenmeyen firma DP dosyası.')
        check(Path(name).name not in result,'Mükerrer firma DP dosyası.');result[Path(name).name]=digest
    check(result,'Firma DP boş.')
    return result


def verify(options):
    source=ROOT/'.local/synthetic'/options.instance;report=ROOT/'.local/operator-recovery-acceptance'/options.instance
    target='fr-oprestore-'+options.instance.removeprefix('fr-synthetic-');root=ROOT/'.local/operator-recovery-targets'/target
    check(not source.exists() and not report.exists() and not root.exists(),'Yeni kaynak/hedef/kabul kimliği gerekli.')
    fixture.private_directory(report)
    package=ROOT/'.local/operator-recovery-packages'/options.instance/'operator.frbk';key=ROOT/'.local/operator-recovery-keys'/options.instance/'recovery.key'
    copied=report/'input.frbk';key_copy=ROOT/'.local/operator-keycopies'/options.instance/'recovery.key'
    source_compose=['docker','compose','-f',str(source/'compose.json')];compose=['docker','compose','-f',str(root/'compose.json')]
    moved=None;started=time.monotonic()
    try:
        fixture.initialize(options);private=source/'private';operator_id=options.instance+'-operator'
        account=recovery.env_bytes((private/'operator-bootstrap.env').read_bytes());password=account['OPERATOR_BOOTSTRAP_PASSWORD']
        old,jar=mfa(f'https://127.0.0.1:{options.ports[2]}',password,account['OPERATOR_BOOTSTRAP_EMAIL'],private/'ca.pem')
        operator.sql(options.instance+'-db-i','''INSERT INTO "AspNetUserClaims" ("UserId","ClaimType","ClaimValue") SELECT "Id",'synthetic','identity-recovery' FROM "AspNetUsers";
INSERT INTO "AspNetRoleClaims" ("RoleId","ClaimType","ClaimValue") SELECT "Id",'synthetic','identity-recovery' FROM "AspNetRoles";
INSERT INTO "AspNetUserLogins" ("LoginProvider","ProviderKey","ProviderDisplayName","UserId") SELECT 'Synthetic','recovery','Sentetik giriş',"Id" FROM "AspNetUsers";''')
        for suffix in ('a','b'):seed(options.instance+'-db-'+suffix,options.instance+'-'+suffix)
        firms={s:recovery.snapshot(options.instance+'-db-'+s) for s in ('a','b')}
        firm_keys={s:live_keys(options.instance+'-app-'+s) for s in ('a','b')}
        firm_containers={c:json.loads(fixture.run(['docker','inspect',c]))[0]['State']['StartedAt'] for c in
            [options.instance+'-'+s for s in ('db-a','db-b','app-a','app-b')]}
        dp=keys(operator_id);before=operator.snapshot(options.instance+'-db-i')
        check(all(not v.startswith('0:') for v in before['tables'].values()) and bool(dp) and all(len(s['tables'])==56 for s in firms.values()),'Identity yedi tablo/sequence veya iki firma kabul verisi eksik.')
        operator.backup(source,package,key)
        metadata,files,env=operator.read_package(package,key,operator_id)
        check(metadata['snapshot']==before,'Yedek kimlik snapshot farklı.')
        for suffix in ('a','b'):
            app=recovery.env_bytes((private/f'app-{suffix}.env').read_bytes())
            secret=app['ConnectionStrings__AppDatabase'].split('Password=',1)[1].split(';')[0]
            check(all(secret.encode() not in data for data in files.values()),'Firma uygulama sırrı operatör paketine girdi.')
        check(all(password.encode() not in data for data in files.values()),'Bootstrap parolası pakete girdi.')
        copied.write_bytes(package.read_bytes());fixture.private_directory(key_copy.parent);key_copy.write_bytes(key.read_bytes())
        if sys.platform!='win32':key_copy.chmod(0o600)
        fixture.run(source_compose+['stop','db-i'])
        moved=source.with_name(source.name+'-unavailable');check(not moved.exists(),'Kaynak gizleme hedefi dolu.');source.rename(moved)
        package.rename(package.with_suffix('.unavailable'));key.rename(key.with_suffix('.unavailable'))
        loss=time.monotonic();check(not source.exists() and not package.exists() and not key.exists(),'Özgün konumlar hâlâ erişilebilir.')
        def inventory():return {k:{n for n in v if n.startswith(target)} for k,v in fixture.inventory().items()}
        def reject(p=copied,k=key_copy,e=operator_id,t=target,port=options.ports[2]+1):
            current=inventory()
            try:operator.restore(p,k,e,t,port)
            except (RuntimeError,ValueError,TypeError,KeyError,OSError,zipfile.BadZipFile):pass
            else:raise RuntimeError('Geçersiz operatör kurtarma kabul edildi.')
            check(not root.exists() and current==inventory(),'Negatif ret hedef kaynağı oluşturdu.')
        wrong=report/'wrong.key';wrong.write_bytes(secrets.token_bytes(32));reject(k=wrong)
        bad=report/'bad.frbk';data=bytearray(copied.read_bytes());data[-1]^=1;bad.write_bytes(data);reject(p=bad)
        reject(e=operator_id+'-wrong');reject(t='firma-randevu-local');reject(port=8080)
        negatives=['wrong-key','corruption','wrong-operator','main-target','main-port']
        for kind in ('missing','path','duplicate','hash','dump','schema','config','target','image','provision','networks'):
            changed=dict(files);meta=dict(metadata)
            if kind=='missing':changed.pop('operator.pfx')
            elif kind=='path':changed['../escape']=b'no'
            elif kind=='hash':changed['db-i.key']+=b'x'
            elif kind=='dump':changed['identity.dump']=b'bad'
            elif kind=='schema':meta['snapshot']=dict(meta['snapshot'],schema='0'*64)
            elif kind=='config':changed['operator.env']+=b'Unknown__Setting=true\n'
            elif kind=='target':changed['operator.env']=changed['operator.env'].replace(b'Host=db-a;',b'Host=internet;')
            elif kind=='image':meta['images']=meta['images'][:1]
            elif kind=='provision':meta['provision']='0'*64
            elif kind=='networks':meta['networks']={'firma-randevu-local_default':'0'*64}
            if kind!='hash':meta['files']={n:hashlib.sha256(v).hexdigest() for n,v in changed.items()}
            stream=io.BytesIO()
            with zipfile.ZipFile(stream,'w') as archive:
                for n,v in changed.items():archive.writestr(n,v)
                archive.writestr('manifest.json',json.dumps(meta))
                if kind=='duplicate':
                    with warnings.catch_warnings():
                        warnings.simplefilter('ignore',UserWarning);archive.writestr('operator.env',files['operator.env'])
            invalid=report/(kind+'.frbk');invalid.write_bytes(recovery.crypt(stream.getvalue(),key_copy,'Encrypt'));reject(p=invalid);negatives.append(kind)
        print('Operator recovery: authenticated archive/config/identity/image/network negatives passed before resources.',flush=True)
        original_run,original_binary=fixture.run,recovery.binary
        forbidden={operator_id,options.instance+'-db-i'}
        def guarded_run(args,*a,**k):
            check(not forbidden.intersection(args),'Restore özgün operatör/Identity konteynerini okudu.')
            return original_run(args,*a,**k)
        def guarded_binary(args,*a,**k):
            check(not forbidden.intersection(args),'Restore özgün operatör/Identity konteynerini okudu.')
            return original_binary(args,*a,**k)
        with patch.object(fixture,'run',side_effect=guarded_run),patch.object(recovery,'binary',side_effect=guarded_binary):
            result=operator.restore(copied,key_copy,operator_id,target,options.ports[2]+1)
        check(result['state']=='restored_closed' and result['rawRestoreEqual'] and not result['operatorStarted'],'Restore varsayılan kapalı değil.')
        check(not json.loads(fixture.run(['docker','inspect',target+'-operator']))[0]['State']['Running'],'Operatör kendiliğinden açıldı.')
        for name in ('ca.pem','db-i.crt','db-i.key','operator.pfx'):
            check((root/'private'/name).read_bytes()==files[name],'Kurtarılan TLS dosyası baytları farklı.')
        check((root/'private/operator.env').read_bytes()==files['operator.env'],'Kurtarılan operatör config baytları farklı.')
        check(keys(target+'-operator')==dp,'Kurtarılan DP dosyaları farklı.')
        print('Operator recovery: seven populated Identity tables/full schema/grants/sequences/DP equal; operator closed.',flush=True)
        # Yalnız bu tek sahte hesap kabulünde açık test başlangıcı; gerçek aktivasyon prosedürü değildir.
        fixture.run(compose+['start','operator'])
        base=f'https://127.0.0.1:{options.ports[2]+1}'
        fresh=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()),urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=str(root/'private/ca.pem'))))
        for _ in range(40):
            try:
                if request(fresh,base+'/health/ready')[0]==200:break
            except OSError:pass
            time.sleep(.5)
        check(request(fresh,base+'/api/auth/me')[0]==401,'Anonim kimlik erişimi açıldı.')
        check(request(old,base+'/api/auth/me')[0]==200,'DP eski MFA cookie çözülemedi.')
        status,targets=request(old,base+'/api/targets/?includeSummary=true')
        check(status==200 and {t['id'] for t in targets['items']}=={options.instance+'-a',options.instance+'-b'} and all(t['status']=='available' for t in targets['items']),'İzinli iki firma gerçek DB özeti yanlış.')
        check(all(t['summary']['latestStartedPeriod']['outstandingTry']=='600.00' for t in targets['items']),'Firma kısmi ödeme borcu farklı.')
        print('Operator recovery: TLS/old MFA cookie/anonymous401/two real600TRY summaries passed.',flush=True)
        def post(route,value):
            csrf=request(fresh,base+'/api/auth/csrf')[1]['token']
            return request(fresh,base+'/api/auth/'+route,value,{'X-CSRF-TOKEN':csrf})
        check(post('login',dict(email=account['OPERATOR_BOOTSTRAP_EMAIL'],password=password))[0]==202,'Yeni giriş MFA istemedi.')
        authenticator=operator.sql(target+'-db','SELECT "Value" FROM "AspNetUserTokens" WHERE "Name"=\'AuthenticatorKey\';')
        raw=base64.b32decode(authenticator+'='*((-len(authenticator))%8));mac=hmac.new(raw,struct.pack('>Q',int(time.time())//30),hashlib.sha1).digest();offset=mac[-1]&15
        code=str((struct.unpack('>I',mac[offset:offset+4])[0]&0x7fffffff)%1000000).zfill(6)
        check(post('mfa/login',dict(value=code))[0]==204,'Kurtarılan TOTP ile yeni MFA girişi başarısız.')
        print('Operator recovery: fresh password/TOTP passed; two restarts begin.',flush=True)
        stable=operator.snapshot(target+'-db')
        for _ in range(2):
            fixture.run(compose+['restart'])
            for attempt in range(40):
                try:
                    if request(fresh,base+'/health/ready')[0]==200:break
                except OSError:pass
                time.sleep(.5)
            check(request(fresh,base+'/api/auth/me')[0]==200 and operator.snapshot(target+'-db')==stable and keys(target+'-operator')==dp,'Restart kimlik/DP erişimini değiştirdi.')
        print('Operator recovery: two restarts preserved all Identity data and DP.',flush=True)
        current=inventory()
        try:operator.restore(copied,key_copy,operator_id,target,options.ports[2]+1)
        except RuntimeError:pass
        else:raise RuntimeError('Mevcut hedef üstüne restore kabul edildi.')
        check(current==inventory() and operator.snapshot(target+'-db')==stable,'Tekrar restore mevcut hedefi değiştirdi.')
        for suffix in ('a','b'):
            check(recovery.snapshot(options.instance+'-db-'+suffix)==firms[suffix] and live_keys(options.instance+'-app-'+suffix)==firm_keys[suffix],'Firma verisi/anahtarı değişti.')
        for name,start in firm_containers.items():check(json.loads(fixture.run(['docker','inspect',name]))[0]['State']['StartedAt']==start,'Firma konteyneri yeniden başladı.')
        print('Operator recovery: repeated restore rejected; both firm snapshots/DP/starts preserved.',flush=True)
        logs=recovery.binary(['docker','logs',target+'-operator'])
        for secret in (password,authenticator,*[v.split('Password=',1)[1].split(';')[0] for k,v in env.items() if k.endswith('ConnectionString') or k=='ConnectionStrings__OperatorIdentity']):check(secret.encode() not in logs,'Operatör logunda sır var.')
        summary=dict(pass_=True,instance=options.instance,target=target,identityTables=7,firmTablesEach=56,rawIdentityRestoreEqual=True,closedByDefault=True,
            oldMfaCookieWorks=True,freshMfa=True,twoRestarts=True,tlsBytesPreserved=True,firmDataKeysStartsPreserved=True,originalIdentityAndOperatorNotRead=True,
            originalPrivatePackageKeyUnavailable=True,negativeChecks=negatives,mainTouched=False,productionActivation=False,offsiteHostLossProven=False,
            localRecoveryAndAcceptanceSeconds=round(time.monotonic()-loss,3),totalSeconds=round(time.monotonic()-started,3))
        fixture.write_json(report/'acceptance.json',summary);print('OPERATOR-RECOVERY-EVIDENCE:'+json.dumps(summary),flush=True)
    finally:
        if root.exists():fixture.run(compose+['stop'])
        if moved and moved.exists():moved.rename(source)
        if source.exists():fixture.run(source_compose+['stop'])


if __name__=='__main__':
    binary=recovery.binary
    def diagnosed_binary(args,*a,**k):
        try:return binary(args,*a,**k)
        except RuntimeError as error:
            category=next((v for v in args if v in {'psql','pg_dump','pg_restore','createdb','dropdb','cp','logs'}),'bounded-command')
            raise RuntimeError('Operatör kabul alt işlemi başarısız; kategori='+category+', özel çıktı gizlendi.') from error
    try:
        with patch.object(recovery,'binary',side_effect=diagnosed_binary):verify(fixture.arguments())
    except (RuntimeError,ValueError,TypeError,KeyError,OSError,zipfile.BadZipFile) as error:
        print(str(error) if isinstance(error,RuntimeError) else 'Sentetik operatör kurtarma başarısız; tür='+type(error).__name__+', özel ayrıntılar gizlendi.',file=sys.stderr);sys.exit(1)
