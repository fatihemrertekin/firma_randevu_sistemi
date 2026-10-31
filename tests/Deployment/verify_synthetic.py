"""Real Docker acceptance; creates only a new explicit synthetic fixture, then stops it."""
import base64
import copy
import hashlib
import hmac
import http.cookiejar
import json
import os
from pathlib import Path
import socket
import ssl
import struct
import sys
import tempfile
import time
import urllib.error
import urllib.request
from unittest.mock import patch

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'deploy'))
import initialize_synthetic as fixture


def check(value, message):
    if not value:
        raise RuntimeError(message)


def reject(options):
    try:
        fixture.initialize(options)
    except (RuntimeError, ValueError, KeyError, OSError):
        return
    raise RuntimeError('Geçersiz hedef kabul edildi.')


def credentials(path):
    return dict(line.split('=', 1) for line in path.read_text().splitlines())


def verify_mount_order():
    item = dict(Id='container', Image='image', Config={'Labels': {fixture.LABEL: 'owner'}}, HostConfig={},
        State={'Running': True}, Mounts=[dict(Destination='/keys', Source='keys'), dict(Destination='/tls', Source='cert')],
        NetworkSettings={'Networks': {'a': {'NetworkID': 'network'}}})
    with patch.object(fixture, 'run', return_value=json.dumps([item])) as inspect:
        before = fixture.resource_state({'container': ['test']}, 'owner')
        item['Mounts'].reverse(); inspect.return_value = json.dumps([item])
        check(before == fixture.resource_state({'container': ['test']}, 'owner'), 'Mount sırası eşitliği bozuyor.')
        item['Mounts'][0]['Source'] = 'foreign'; inspect.return_value = json.dumps([item])
        check(before != fixture.resource_state({'container': ['test']}, 'owner'), 'Farklı mount kaynağı yakalanmadı.')


def verify_source_line_endings():
    with tempfile.TemporaryDirectory() as directory:
        path = Path(directory) / 'source.py'
        path.write_bytes(b'first\nsecond\n')
        source, artifact = fixture.source_hash(path), fixture.file_hash(path)
        path.write_bytes(b'first\r\nsecond\r\n')
        check(source == fixture.source_hash(path), 'Git satır sonu kaynak eşitliğini bozuyor.')
        check(artifact != fixture.file_hash(path), 'Özel dosya bayt kontrolü zayıfladı.')
        path.write_bytes(b'changed\r\nsecond\r\n')
        check(source != fixture.source_hash(path), 'Gerçek kaynak farkı yakalanmadı.')


def mfa(base, password, email, ca):
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()),
        urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=str(ca))))

    def post(path, value):
        with opener.open(base+'/api/auth/csrf', timeout=10) as response:
            csrf = json.load(response)['token']
        request = urllib.request.Request(base+'/api/auth/'+path, json.dumps(value).encode(),
            {'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf})
        with opener.open(request, timeout=15) as response:
            body = response.read()
            return response.status, json.loads(body) if body else None

    check(post('login', dict(email=email, password=password))[0] == 204, 'İlk sentetik giriş başarısız.')
    status, setup = post('mfa/setup', dict(password=password))
    check(status == 200, 'MFA kurulamadı.')
    key = setup['key']
    raw = base64.b32decode(key.upper() + '=' * ((-len(key)) % 8))
    mac = hmac.new(raw, struct.pack('>Q', int(time.time()) // 30), hashlib.sha1).digest()
    offset = mac[-1] & 15
    code = str((struct.unpack('>I', mac[offset:offset+4])[0] & 0x7fffffff) % 1000000).zfill(6)
    code_value = {'value' if email.startswith('operator') else 'code': code}
    check(post('mfa/enable', dict(password=password, **code_value))[0] == 200, 'MFA açılamadı.')
    check(post('login', dict(email=email, password=password))[0] == 202, 'MFA zorunlu değil.')
    check(post('mfa/login', code_value)[0] == 204, 'MFA girişi başarısız.')


def verify(options):
    verify_mount_order()
    verify_source_line_endings()
    folder = ROOT / '.local/synthetic' / options.instance
    check(not folder.exists(), 'Kabul testi yalnız yeni fixture kimliği kabul eder.')
    compose = ['docker', 'compose', '-f', str(folder / 'compose.json')]
    created = False
    try:
        # All negative preflight checks leave even the candidate directory absent.
        for invalid in (dict(instance='firma-randevu-local'), dict(ports=[8080, *options.ports[1:]]),
                        dict(ports=[options.ports[0]]*3), dict(app_image='firma-randevu:local'),
                        dict(operator_image='sha256:'+'0'*64)):
            candidate = copy.copy(options)
            candidate.__dict__.update(invalid)
            before = fixture.inventory()
            reject(candidate)
            check(fixture.inventory() == before and not folder.exists(), 'Ret kaynak oluşturdu.')
        with socket.socket() as busy:
            busy.bind(('127.0.0.1', options.ports[0])); busy.listen()
            reject(options)
            check(not folder.exists(), 'Dolu portta dosya yazıldı.')
        try:
            os.chdir(ROOT.parent)
            reject(options)
            check(not folder.exists(), 'Başka çalışma dizininde dosya yazıldı.')
        finally:
            os.chdir(ROOT)
        # A pre-existing foreign volume must never be adopted or modified.
        collision = options.instance+'-db_a'
        fixture.run(['docker', 'volume', 'create', collision])
        before = fixture.run(['docker', 'volume', 'inspect', collision])
        reject(options)
        check(fixture.run(['docker', 'volume', 'inspect', collision]) == before and not folder.exists(), 'Yabancı volume değişti.')
        # This empty test-owned collision volume is kept; use another explicit ID for the real fixture.
        options = copy.copy(options)
        options.instance += '-run'
        folder = ROOT / '.local/synthetic' / options.instance
        check(not folder.exists(), 'Kabul hedefi zaten var.')
        compose = ['docker', 'compose', '-f', str(folder / 'compose.json')]
        check(fixture.initialize(options)['state'] == 'created', 'İlk kurulum yok.')
        created = True
        manifest = json.loads((folder / 'manifest.json').read_text())
        private = folder / 'private'
        secrets_seen = []
        for file in private.glob('*.env'):
            for key, value in credentials(file).items():
                if key.endswith('PASSWORD') or key.endswith('__Password'):
                    secrets_seen.append(value)
                    check(value not in (folder / 'manifest.json').read_text(), 'Manifest sır içeriyor.')
        for suffix in ('a', 'b'):
            connection = credentials(private / f'app-{suffix}.env')['ConnectionStrings__AppDatabase']
            secrets_seen.append(connection.split('Password=', 1)[1].split(';')[0])
        for key, connection in credentials(private / 'operator.env').items():
            if key == 'ConnectionStrings__OperatorIdentity' or key.endswith('__ConnectionString'):
                secrets_seen.append(connection.split('Password=', 1)[1].split(';')[0])
        check(len(secrets_seen) == len(set(secrets_seen)), 'Parolalar ayrı değil.')
        check(all(secret not in (folder / name).read_text() for secret in secrets_seen for name in ('manifest.json', 'compose.json')), 'Açık config sır içeriyor.')
        for suffix, port in zip(('a', 'b'), options.ports[:2]):
            account = credentials(private / f'owner-{suffix}.env')
            mfa(f'http://127.0.0.1:{port}', account['APP_BOOTSTRAP_OWNER_PASSWORD'], account['APP_BOOTSTRAP_OWNER_EMAIL'], private / 'ca.pem')
        account = credentials(private / 'operator-bootstrap.env')
        mfa(f'https://127.0.0.1:{options.ports[2]}', account['OPERATOR_BOOTSTRAP_PASSWORD'], account['OPERATOR_BOOTSTRAP_EMAIL'], private / 'ca.pem')

        def sql(suffix, query):
            return fixture.run(compose + ['exec', '-T', 'db-'+suffix, 'psql', '-X', '-U', 'postgres', '-d',
                'operator_identity' if suffix == 'i' else 'firma_randevu', '-At', '-v', 'ON_ERROR_STOP=1'], query)

        def snapshot():
            values = {}
            for suffix in ('a', 'b', 'i'):
                query = "SELECT format('SELECT md5(coalesce(string_agg(row_to_json(t)::text, %L ORDER BY row_to_json(t)::text), %L)) FROM %I t;', '', '', tablename) FROM pg_tables WHERE schemaname='public' ORDER BY tablename;\n\\gexec\n"
                values[suffix] = fixture.digest(sql(suffix, query))
                values[suffix+'-marker'] = fixture.digest(sql(suffix, "SELECT setconfig FROM pg_db_role_setting ORDER BY setdatabase, setrole;"))
            for service in ('app-a', 'app-b', 'operator'):
                values[service+'-keys'] = fixture.digest(fixture.run(compose + ['exec', '-T', service, 'sh', '-c', 'sha256sum /app/keys/*']))
            return values

        for suffix in ('a', 'b'):
            sql(suffix, f'UPDATE "BusinessProfiles" SET "Name" = \'SYNTHETIC-PRESERVE-{suffix}\';')
            check(sql(suffix, 'SELECT count(*) FROM "AspNetUsers" WHERE "TwoFactorEnabled";') == '1', 'Owner/MFA yok.')
        before = snapshot()
        files_before = {str(p.relative_to(folder)): fixture.file_hash(p) for p in folder.rglob('*') if p.is_file()}
        check(fixture.initialize(options)['state'] == 'verified', 'İkinci kurulum salt okunur doğrulanmadı.')
        check(before == snapshot(), 'DB/işaret/MFA/anahtar koruması başarısız.')
        check(files_before == {str(p.relative_to(folder)): fixture.file_hash(p) for p in folder.rglob('*') if p.is_file()}, 'İkinci çalıştırma dosya değiştirdi.')
        resources_before = fixture.resource_state(manifest['names'], manifest['owner'])
        candidate = copy.copy(options); candidate.ports = [options.ports[0]+10, *options.ports[1:]]
        reject(candidate)
        file = private / 'owner-a.env'; original = file.read_bytes()
        file.write_bytes(original+b'\n')
        reject(options)
        file.write_bytes(original)
        resources_after = fixture.resource_state(manifest['names'], manifest['owner'])
        check(resources_after == resources_before, 'Config reddinde farklı kaynak: '+','.join(k for k in resources_before if resources_before[k] != resources_after.get(k)))
        after = snapshot()
        check(after == before, 'Config reddinde farklı veri/anahtar: '+','.join(k for k in before if before[k] != after.get(k)))
        fixture.run(compose + ['stop', 'app-a'])
        reject(options)
        fixture.run(compose + ['start', 'app-a'])
        fixture.ready(options.ports, private / 'ca.pem')
        check(snapshot() == before, 'Restart veriyi/anahtarı değiştirdi.')
        report = dict(pass_=True, instance=options.instance, first=True, repeatReadOnly=True, mfaAndSentinelsPreserved=True,
                      conflictAndTamperRejected=True, stoppedTargetRejected=True, tlsVerified=True, mainTouched=False,
                      resources=manifest['names'], images=manifest['config']['images'])
        fixture.write_json(folder / 'acceptance.json', report)
        print(json.dumps(report))
    finally:
        if created or (folder / 'compose.json').is_file():
            fixture.run(compose + ['stop'])


if __name__ == '__main__':
    try:
        verify(fixture.arguments())
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
