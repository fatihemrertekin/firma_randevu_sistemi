"""Bounded single-firm synthetic backup/clean restore. No production target or overwrite."""
import argparse
import base64
import hashlib
import io
import json
from pathlib import Path
import re
import secrets
import socket
import subprocess
import sys
import tarfile
import time
import urllib.error
import urllib.request
import zipfile

import initialize_synthetic as fixture

# shortcut: Küçük sentetik paket RAM'de sınırlıdır; gerçek hacimde incelenmiş akışlı araç seç.
LIMIT = 64 * 1024**2
SQL_FILES = ('app-user-grants.sql', 'subscription-operator-target.sql', 'Invalidate-RestoredPublicAccess.sql')


def binary(args, data=None):
    try:
        result = subprocess.run(args, input=data, capture_output=True, timeout=120, cwd=fixture.ROOT)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise RuntimeError('Kurtarma alt işlemi çalışmadı; kaynaklar korunuyor.') from error
    fixture.require(result.returncode == 0, 'Kurtarma alt işlemi başarısız; sır içerebilen çıktı gizlendi.')
    fixture.require(len(result.stdout) <= LIMIT * 2, 'Sentetik çıktı boyut sınırını aşıyor.')
    return result.stdout


def env_bytes(data):
    return dict(line.split('=', 1) for line in data.decode('utf-8').splitlines())


def crypt(data, key, operation):
    fixture.no_links(key)
    fixture.require(key.is_file() and key.stat().st_size == 32 and len(data) <= LIMIT+34, 'Paket/anahtar boyutu geçersiz.')
    return base64.b64decode(binary(['pwsh', '-NoProfile', '-NonInteractive', '-File',
        str(fixture.ROOT/'deploy/Protect-SyntheticRecovery.ps1'), '-Operation', operation, '-KeyFile', str(key)], base64.b64encode(data)), validate=True)


def sql(container, text):
    return binary(['docker', 'exec', '-i', container, 'psql', '-X', '-U', 'postgres', '-d',
        'firma_randevu', '-At', '-v', 'ON_ERROR_STOP=1'], text.encode()).decode().strip()


def snapshot(container):
    query = """SELECT format($q$SELECT json_build_object('table',%L,'rows',count(*),'hash',encode(sha256(convert_to(coalesce(string_agg(row_to_json(t)::text,'' ORDER BY row_to_json(t)::text),''),'UTF8')),'hex')) FROM %I t;$q$,tablename,tablename) FROM pg_tables WHERE schemaname='public' ORDER BY tablename
\\gexec
"""
    entries = [json.loads(line) for line in sql(container,query).splitlines()]
    rows = {entry['table']:str(entry['rows'])+':'+entry['hash'] for entry in entries}
    schema = binary(['docker', 'exec', container, 'pg_dump', '-U', 'postgres', '-d', 'firma_randevu',
        '--schema-only', '--no-owner', '--no-acl', '--restrict-key=FRSyntheticRecovery'])
    grants = sql(container, """SELECT row_to_json(t) FROM (SELECT grantee,table_name,column_name,privilege_type FROM information_schema.column_privileges WHERE table_schema='public' AND grantee IN ('app_user','subscription_operator') ORDER BY 1,2,3,4) t;""")
    grants += '\nTABLE\n'+sql(container, "SELECT row_to_json(t) FROM (SELECT grantee,table_name,privilege_type,is_grantable FROM information_schema.table_privileges WHERE table_schema='public' AND grantee IN ('app_user','subscription_operator') ORDER BY 1,2,3,4) t;")
    sequences = sql(container, "SELECT row_to_json(t) FROM (SELECT * FROM pg_sequences WHERE schemaname='public' ORDER BY sequencename) t;")
    roles = sql(container, "SELECT row_to_json(t) FROM (SELECT rolname,rolsuper,rolinherit,rolcreaterole,rolcreatedb,rolcanlogin,rolbypassrls FROM pg_roles WHERE rolname IN ('app_user','subscription_operator') ORDER BY rolname) t;")
    fixture.require(sql(container,"SELECT count(*) FROM pg_auth_members WHERE member IN (SELECT oid FROM pg_roles WHERE rolname IN ('app_user','subscription_operator'));")=='0','Sentetik uygulama/mali rol üyeliği geniş; otomatik kopyalanmaz.')
    marker = sql(container, "SELECT setting FROM pg_db_role_setting s CROSS JOIN LATERAL unnest(s.setconfig) setting WHERE setdatabase=(SELECT oid FROM pg_database WHERE datname=current_database()) AND setrole=0 ORDER BY setting;")
    return dict(tables=rows, schema=hashlib.sha256(schema).hexdigest(), grants=fixture.digest(grants),
        sequences=fixture.digest(sequences), roles=roles, marker=marker)


def local_path(path):
    path = Path(path).absolute()
    fixture.no_links(path)
    fixture.require(path.is_relative_to(fixture.ROOT/'.local') and '..' not in path.parts, 'Yalnız proje .local hedefi kabul edilir.')
    return path


def archive(files, manifest):
    manifest = dict(manifest, files={n: hashlib.sha256(v).hexdigest() for n, v in files.items()})
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', compression=zipfile.ZIP_DEFLATED) as output:
        for name, data in dict(files, **{'manifest.json': json.dumps(manifest).encode()}).items():
            output.writestr(name, data)
    fixture.require(len(stream.getvalue()) <= LIMIT, 'Sentetik paket 64MiB sınırını aşıyor.')
    return stream.getvalue()


def backup(source, package, key):
    fixture.require(Path.cwd().resolve() == fixture.ROOT, 'Depo kökü gerekli.')
    source, package, key = map(local_path, (source, package, key))
    manifest = json.loads((source/'manifest.json').read_text())
    config = manifest['config']
    fixture.require(manifest['state'] == 'ready' and re.fullmatch(r'fr-synthetic-[a-z0-9-]+', config['instance']), 'Yalnız hazır sentetik kaynak kabul edilir.')
    fixture.require(source == fixture.ROOT/'.local/synthetic'/config['instance'], 'Kaynak kökü farklı.')
    fixture.require(not package.exists() and not key.exists() and package.parent != key.parent, 'Paket/anahtar yeni ve ayrı dizinlerde olmalı.')
    fixture.require(not package.parent.exists() and not key.parent.exists(), 'Yeni paket/anahtar dizini gerekli.')
    fixture.require(manifest['names'] == fixture.resource_names(config['instance']), 'Kaynak envanteri farklı.')
    fixture.require(fixture.resource_state(manifest['names'], manifest['owner']) == manifest['resources'], 'Kaynak değişmiş.')
    for name, expected in manifest['files'].items():
        path = source/name
        fixture.require(path.is_relative_to(source) and '..' not in Path(name).parts, 'Geçersiz kaynak yolu.')
        fixture.no_links(path)
        fixture.require(fixture.file_hash(path) == expected, 'Kaynak dosyası değişmiş.')
    private = source/'private'
    app_env = env_bytes((private/'app-a.env').read_bytes())
    fixture.require(all(app_env.get(k, 'false') == 'false' for k in ('BookingVerification__Enabled', 'BookingSms__DispatchEnabled', 'AppointmentReminders__Enabled', 'Netgsm__Enabled')), 'Gönderim kapalı olmalı.')
    compose = ['docker', 'compose', '-f', str(source/'compose.json')]
    # DP anahtarları ve DB aynı firma yazıcıları durduktan sonra alınır; DB çalışır.
    fixture.run(compose+['stop', 'app-a', 'operator'])
    container = config['instance']+'-db-a'
    before = snapshot(container)
    files = {'database.dump': binary(['docker', 'exec', container, 'pg_dump', '-U', 'postgres', '-d', 'firma_randevu', '-Fc']),
        'schema.sql': binary(['docker','exec',container,'pg_dump','-U','postgres','-d','firma_randevu','--schema-only','--no-owner','--no-acl','--restrict-key=FRSyntheticRecovery']),
        'app.env': (private/'app-a.env').read_bytes(), 'ca.pem': (private/'ca.pem').read_bytes(),
        'db.crt': (private/'db-a.crt').read_bytes(), 'db.key': (private/'db-a.key').read_bytes()}
    op = env_bytes((private/'operator.env').read_bytes())
    files['target-password'] = op['SubscriptionTargets__0__ConnectionString'].split('Password=', 1)[1].split(';')[0].encode()
    keys = binary(['docker', 'cp', config['instance']+'-app-a:/app/keys/.', '-'])
    with tarfile.open(fileobj=io.BytesIO(keys)) as stream:
        for member in stream:
            if member.isdir():
                continue
            name = Path(member.name).name
            fixture.require(member.isfile() and re.fullmatch(r'key-[0-9a-f-]+\.xml', name), 'Geçersiz DP anahtar dosyası.')
            fixture.require('keys/'+name not in files, 'Mükerrer DP dosyası.')
            files['keys/'+name] = stream.extractfile(member).read()
    fixture.require(any(n.startswith('keys/') for n in files), 'DP anahtarları eksik.')
    fixture.require(before == snapshot(container), 'Yedek alınırken veri değişti.')
    metadata = dict(version=1, firm=app_env['Auth__InstanceId'], images=[config['images'][0], config['images'][2]],
        snapshot=before, createdUtc=time.time(), tools=fixture.run(['docker', 'exec', container, 'pg_dump', '--version']),
        provisioning={n: fixture.source_hash(fixture.ROOT/'deploy'/n) for n in SQL_FILES})
    with fixture.lock():
        for parent in (package.parent, key.parent):
            fixture.require(not parent.exists(), 'Yeni paket/anahtar dizini gerekli.')
        fixture.private_directory(package.parent);fixture.private_directory(key.parent)
        with key.open('xb') as handle:handle.write(secrets.token_bytes(32))
        if sys.platform != 'win32':key.chmod(0o600)
        encrypted = crypt(archive(files, metadata), key, 'Encrypt')
        with package.open('xb') as handle:handle.write(encrypted)
    return dict(firm=metadata['firm'], package=str(package), encryptedBytes=len(encrypted), plaintextArchiveOnDisk=False)


def read_package(package, key, expected_firm):
    package, key = map(local_path, (package, key))
    fixture.require(package.stat().st_size <= LIMIT+34, 'Paket boyutu geçersiz.')
    data = crypt(package.read_bytes(), key, 'Decrypt')
    with zipfile.ZipFile(io.BytesIO(data)) as stream:
        items = stream.infolist();names=[i.filename for i in items]
        fixture.require(len(names) == len(set(names)) and len(names) <= 100 and sum(i.file_size for i in items) <= LIMIT, 'Arşiv boyutu/mükerrer dosya reddi.')
        allowed = {'manifest.json', 'database.dump', 'schema.sql', 'app.env', 'ca.pem', 'db.crt', 'db.key', 'target-password'}
        fixture.require(all(n in allowed or re.fullmatch(r'keys/key-[0-9a-f-]+\.xml', n) for n in names), 'Yabancı arşiv yolu.')
        fixture.require(allowed <= set(names) and any(n.startswith('keys/') for n in names), 'Paket bileşeni eksik.')
        files = {n: stream.read(n) for n in names}
    manifest = json.loads(files.pop('manifest.json'))
    fixture.require(manifest['version'] == 1 and manifest['firm'] == expected_firm and re.fullmatch(r'fr-synthetic-[a-z0-9-]+-a', expected_firm), 'Paket firma/sürüm eşleşmesi yanlış.')
    fixture.require(manifest['files'] == {n:hashlib.sha256(v).hexdigest() for n,v in files.items()}, 'Paket bütünlüğü yanlış.')
    fixture.require(hashlib.sha256(files['schema.sql']).hexdigest() == manifest['snapshot']['schema'], 'Kaynak şema snapshot eşleşmesi yanlış.')
    fixture.require(re.fullmatch(b'[0-9a-f]{64}',files['target-password']), 'Mali rol parolası biçimi yanlış.')
    fixture.require(manifest['provisioning'] == {n:fixture.source_hash(fixture.ROOT/'deploy'/n) for n in SQL_FILES}, 'Provisioning sürümü farklı; otomatik yükseltme yok.')
    env = env_bytes(files['app.env'])
    fixture.require(env['Auth__InstanceId'] == expected_firm and env['IdentityEmail__Mode'] == 'Local', 'Paket kimlik/yerel iletişim sözleşmesi yanlış.')
    fixture.require(set(env) == {'ASPNETCORE_ENVIRONMENT','Auth__InstanceId','Auth__KeysDirectory','ConnectionStrings__AppDatabase',
        'IdentityEmail__Mode','BookingVerification__Enabled','BookingSms__DispatchEnabled','AppointmentReminders__Enabled','PublicBooking__AccessEnabled','Netgsm__Enabled'}, 'Bilinmeyen config anahtarı.')
    fixture.require(env['ASPNETCORE_ENVIRONMENT']=='Development' and env['Auth__KeysDirectory']=='/app/keys' and
        re.fullmatch(r'Host=db-a;Database=firma_randevu;Username=app_user;Password=[0-9a-f]{64};SSL Mode=VerifyFull;Root Certificate=/tls/ca.pem;GSS Encryption Mode=Disable;Timeout=3',env['ConnectionStrings__AppDatabase']), 'Paket bağlantı/config sözleşmesi farklı.')
    fixture.require(all(env.get(k, 'false') == 'false' for k in ('BookingVerification__Enabled', 'BookingSms__DispatchEnabled', 'AppointmentReminders__Enabled', 'Netgsm__Enabled')), 'Gönderim açık paket reddedildi.')
    fixture.require(files['database.dump'].startswith(b'PGDMP'), 'pg_dump arşivi geçersiz.')
    return manifest, files, env


def ready(port):
    for attempt in range(40):
        try:
            with urllib.request.urlopen(f'http://127.0.0.1:{port}/health/ready', timeout=3) as response:
                if response.status == 200:return
        except (OSError, urllib.error.URLError):pass
        time.sleep(.5)
    raise RuntimeError('Kurtarılan hedef hazır olmadı; kaynaklar korunuyor.')


def restore(package, key, expected_firm, target, port):
    fixture.require(Path.cwd().resolve() == fixture.ROOT and re.fullmatch(r'fr-restore-[a-z0-9][a-z0-9-]{0,24}', target), 'Açık yeni fr-restore hedefi gerekli.')
    fixture.require(1024 <= port <= 65535 and port != 8080, 'Ana8080/yanlış port reddedildi.')
    manifest, files, env = read_package(package, key, expected_firm)
    root = local_path(fixture.ROOT/'.local/recovery-targets'/target)
    names = dict(container=[target+'-db', target+'-app'], volume=[target+'-data', target+'-keys'], network=[target+'-net'])
    with fixture.lock():
        fixture.require(not root.exists(), 'Mevcut hedefe restore yok.')
        inventory = fixture.inventory()
        fixture.require(all(not set(v)&inventory[k] for k,v in names.items()), 'Yabancı/mevcut kaynak çakışması.')
        for image in manifest['images']:
            fixture.require(re.fullmatch(r'sha256:[0-9a-f]{64}', image) and json.loads(fixture.run(['docker','image','inspect',image]))[0]['Id'] == image, 'Sabit yerel kurtarma imajı eksik.')
        with socket.socket() as sock:
            if sys.platform == 'win32':sock.setsockopt(socket.SOL_SOCKET,socket.SO_EXCLUSIVEADDRUSE,1)
            sock.bind(('127.0.0.1',port))
        fixture.private_directory(root/'private');private=root/'private';owner=secrets.token_hex(24)
        for name in ('ca.pem','db.crt','db.key'): (private/name).write_bytes(files[name])
        if sys.platform != 'win32':
            for p in private.iterdir():p.chmod(0o644 if p.suffix in ('.pem','.crt') else 0o600)
        fixture.environment(private/'app.env', dict(env, PublicBooking__AccessEnabled='false'))
        fixture.environment(private/'db.env', dict(POSTGRES_DB='firma_randevu',POSTGRES_PASSWORD=secrets.token_hex(32)))
        def bind(name,target):return dict(type='bind',source=str(private/name),target=target,read_only=True)
        db=dict(image=manifest['images'][1],env_file=[str(private/'db.env')],networks={'net':{'aliases':['db-a']}},
            volumes=['data:/var/lib/postgresql',bind('db.crt','/tls/db.crt'),bind('db.key','/tls/db.key'),bind('ca.pem','/tls/ca.pem')],
            command=['bash','-c','cp /tls/db.key /tmp/recovery.key && chown postgres:postgres /tmp/recovery.key && chmod 600 /tmp/recovery.key && exec docker-entrypoint.sh postgres -c ssl=on -c ssl_cert_file=/tls/db.crt -c ssl_key_file=/tmp/recovery.key'],
            healthcheck=dict(test=['CMD','pg_isready','-h','127.0.0.1','-U','postgres','-d','firma_randevu'],interval='2s',timeout='3s',retries=40))
        app=dict(image=manifest['images'][0],env_file=[str(private/'app.env')],networks=['net'],ports=[f'127.0.0.1:{port}:8080'],
            volumes=['keys:/app/keys',bind('ca.pem','/tls/ca.pem')],read_only=True,tmpfs=['/tmp:size=64m,mode=1777'],cap_drop=['ALL'],security_opt=['no-new-privileges:true'])
        for name,service in [('db',db),('app',app)]:service.update(container_name=target+'-'+name,labels={fixture.LABEL:owner},pull_policy='never',cpus=.5,mem_limit='512m',memswap_limit='512m',pids_limit=128,logging=dict(driver='json-file',options={'max-size':'1m','max-file':'2'}))
        config=dict(name=target,services=dict(db=db,app=app),networks={'net':dict(name=target+'-net',labels={fixture.LABEL:owner})},volumes={k:dict(name=target+'-'+v,labels={fixture.LABEL:owner}) for k,v in [('data','data'),('keys','keys')]})
        fixture.write_json(root/'compose.json',config)
        fixture.write_json(root/'manifest.json',dict(state='preparing',owner=owner,names=names,firm=expected_firm))
        compose=['docker','compose','-f',str(root/'compose.json')]
        fixture.run(compose+['up','-d','--wait','db'])
        container=target+'-db'
        fixture.require(sql(container,"SELECT count(*) FROM pg_tables WHERE schemaname='public';")=='0', 'Hedef DB boş değil.')
        password=env['ConnectionStrings__AppDatabase'].split('Password=',1)[1].split(';')[0]
        fixture.require(re.fullmatch('[0-9a-f]{64}',password) and re.fullmatch('[0-9a-f]{64}',files['target-password'].decode()), 'Geçersiz sentetik parola biçimi.')
        sql(container,f"CREATE ROLE app_user LOGIN PASSWORD '{password}';")
        binary(['docker','exec','-i',container,'pg_restore','-U','postgres','-d','firma_randevu','--single-transaction','--exit-on-error','--no-owner','--no-acl'],files['database.dump'])
        sql(container,(fixture.ROOT/'deploy/app-user-grants.sql').read_text(encoding='utf-8-sig'))
        sql(container,f"\\set database_name firma_randevu\n\\set instance_id {expected_firm}\n\\set target_password {files['target-password'].decode()}\n"+(fixture.ROOT/'deploy/subscription-operator-target.sql').read_text(encoding='utf-8-sig'))
        # PostgreSQL restore sırasında CHECK ifadelerini yeniden ayrıştırıp eşdeğer cast'leri katlar.
        # Aynı kaynak DDL'sini ayrı boş referans DB'de ayrıştır; hiçbir ifadeyi regex ile atlama.
        binary(['docker','exec',container,'createdb','-U','postgres','recovery_schema_reference'])
        binary(['docker','exec','-i',container,'psql','-X','-U','postgres','-d','recovery_schema_reference','-v','ON_ERROR_STOP=1'],files['schema.sql'])
        reference=binary(['docker','exec',container,'pg_dump','-U','postgres','-d','recovery_schema_reference','--schema-only','--no-owner','--no-acl','--restrict-key=FRSyntheticRecovery'])
        normalized=dict(manifest['snapshot'],schema=hashlib.sha256(reference).hexdigest())
        fixture.require(snapshot(container)==normalized, 'Ham restore veri/ayrıştırılmış şema/sequence/rol/grant/marker eşitliği başarısız.')
        sql(container,(fixture.ROOT/'deploy/Invalidate-RestoredPublicAccess.sql').read_text(encoding='utf-8-sig'))
        after=snapshot(container)
        changed=[n for n in after['tables'] if after['tables'][n]!=manifest['snapshot']['tables'][n]]
        fixture.require(set(changed)<= {'BookingSmsBudgetPolicies','BookingSmsAttempts','AppointmentReminderJobs','PublicAppointmentAccesses','BookingVerifications'}, 'Güvenlik iptalinde kapsam dışı veri farkı.')
        fixture.require(all(after[k]==normalized[k] for k in after if k!='tables'), 'İptal şema/yetkiyi değiştirdi.')
        stream=io.BytesIO()
        with tarfile.open(fileobj=stream,mode='w') as output:
            directory=tarfile.TarInfo('.');directory.type=tarfile.DIRTYPE;directory.uid=directory.gid=1654;directory.mode=0o700;output.addfile(directory)
            for name,data in files.items():
                if name.startswith('keys/'):
                    item=tarfile.TarInfo(name[5:]);item.uid=item.gid=1654;item.mode=0o600;item.size=len(data);output.addfile(item,io.BytesIO(data))
        fixture.run(compose+['create','app'])
        binary(['docker','cp','-a','-',target+'-app:/app/keys/'],stream.getvalue())
        fixture.run(compose+['start','app']);ready(port)
        result=dict(state='restored',firm=expected_firm,target=target,rawRestoreEqual=True,securityChangedTables=changed,
            sourceRequired=False,plaintextArchiveOnDisk=False,owner=owner,names=names)
        fixture.write_json(root/'manifest.json',result)
        return result


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='operation',required=True)
    b=sub.add_parser('backup');b.add_argument('--source',required=True)
    r=sub.add_parser('restore');r.add_argument('--expected-firm',required=True);r.add_argument('--target',required=True);r.add_argument('--port',type=int,required=True)
    for p in (b,r):p.add_argument('--package',required=True);p.add_argument('--key',required=True)
    args=vars(parser.parse_args());operation=args.pop('operation')
    try:print(json.dumps((backup if operation=='backup' else restore)(**args)))
    except (RuntimeError,ValueError,KeyError,OSError,zipfile.BadZipFile,tarfile.TarError):
        print('Sentetik kurtarma reddedildi/başarısız; mevcut kaynaklar korunuyor. Sır içerebilen ayrıntılar gizlendi.',file=sys.stderr);sys.exit(1)
