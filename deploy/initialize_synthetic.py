"""Repo-root-only, disposable synthetic A/B + operator acceptance environment.

No production target, implicit image pull, repair, reset or volume deletion.
"""
import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import socket
import ssl
import subprocess
import sys
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
SERVICES = ('db-a', 'db-b', 'db-i', 'app-a', 'app-b', 'operator')
VOLUMES = ('db_a', 'db_b', 'db_i', 'keys_a', 'keys_b', 'keys_operator')
NETWORKS = ('a', 'b', 'identity')
LABEL = 'tr.firmarandevu.synthetic'


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def run(args, data=None, env=None):
    # SQL/bootstrap errors may contain secrets. Never forward process output on failure.
    try:
        result = subprocess.run(args, input=data, text=True, encoding='utf-8',
                                errors='replace', stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                timeout=240, cwd=ROOT, env=env)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise RuntimeError(f'{args[0]} çalıştırılamadı veya süre sınırı aşıldı.') from error
    require(result.returncode == 0, f'{args[0]} işlemi başarısız (çıkış {result.returncode}); fixture korunuyor.')
    return result.stdout.strip()


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


def file_hash(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_hash(path):
    # Git checkout satır sonunu değiştirebilir; özel kurulum dosyaları file_hash kullanır.
    return hashlib.sha256(path.read_bytes().replace(b'\r\n', b'\n')).hexdigest()


def no_links(path):
    for item in (path, *path.parents):
        require(not item.is_symlink() and not item.is_junction(), 'Bağlantılı/junction hedef reddedildi.')


@contextmanager
def lock():
    path = ROOT / '.local/synthetic.lock'
    path.parent.mkdir(exist_ok=True)
    with path.open('a+b') as handle:
        handle.seek(0); handle.write(b'0'); handle.flush(); handle.seek(0)
        try:
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as error:
            raise RuntimeError('Başka bir sentetik kurulum çalışıyor.') from error
        try:
            yield
        finally:
            if os.name == 'nt':
                handle.seek(0); msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(handle.fileno(), fcntl.LOCK_UN)


def private_directory(path):
    path.mkdir(parents=True, mode=0o700)
    if os.name == 'nt':
        sid = run(['whoami', '/user', '/fo', 'csv', '/nh']).split(',')[-1].strip('"')
        run(['icacls', str(path), '/inheritance:r', '/grant:r', f'*{sid}:(OI)(CI)F'])
    else:
        path.chmod(0o700)


def write(path, value, public=False):
    path.write_text(value, encoding='utf-8')
    if os.name != 'nt':
        path.chmod(0o644 if public else 0o600)


def write_json(path, value):
    write(path, json.dumps(value, indent=2) + '\n')


def environment(path, values):
    require(all('\n' not in str(v) and '\r' not in str(v) for v in values.values()), 'Geçersiz env değeri.')
    write(path, ''.join(f'{key}={value}\n' for key, value in values.items()))


def resource_names(instance):
    return {'container': [f'{instance}-{s}' for s in SERVICES],
            'volume': [f'{instance}-{v}' for v in VOLUMES],
            'network': [f'{instance}-{n}' for n in NETWORKS]}


def inventory():
    return {kind: set(run(['docker', kind, 'ls', *flags, '--format', '{{.Names}}' if kind == 'container' else '{{.Name}}']).splitlines())
            for kind, flags in (('container', ['-a']), ('volume', []), ('network', []))}


def resource_state(names, owner):
    result = {}
    for kind, resources in names.items():
        for name in resources:
            item = json.loads(run(['docker', kind, 'inspect', name]))[0]
            labels = item['Config']['Labels'] if kind == 'container' else item['Labels']
            require(labels.get(LABEL) == owner, f'Yabancı kaynak: {name}')
            if kind == 'container':
                require(item['State']['Running'], f'Durmuş kaynak: {name}')
                if 'Health' in item['State']:
                    require(item['State']['Health']['Status'] == 'healthy', f'Sağlıksız kaynak: {name}')
                # Eski Docker sürümleri inspect mount listesini farklı sıralarda döndürebilir.
                value = [item['Id'], item['Image'], item['Config'], item['HostConfig'], sorted(item['Mounts'], key=lambda m: m['Destination']),
                         {name: net['NetworkID'] for name, net in item['NetworkSettings']['Networks'].items()}]
            else:
                value = [item.get('Id', item['Name']), item.get('CreatedAt', item.get('Created')), labels,
                         item.get('Driver'), item.get('Options'), item.get('Internal')]
            result[name] = digest(value)
    return result


def ready(ports, ca):
    context = ssl.create_default_context(cafile=str(ca))
    for url in (f'http://127.0.0.1:{ports[0]}', f'http://127.0.0.1:{ports[1]}', f'https://127.0.0.1:{ports[2]}'):
        for attempt in range(40):
            try:
                with urllib.request.urlopen(url + '/health/ready', context=context, timeout=3) as response:
                    if response.status == 200:
                        break
            except (OSError, urllib.error.URLError):
                pass
            if attempt == 39:
                raise RuntimeError('Sentetik sağlık kontrolü başarısız; kaynaklar korunuyor.')
            time.sleep(.5)


def initialize(options):
    require(Path.cwd().resolve() == ROOT, 'Komutu depo kökünden çalıştırın.')
    require(re.fullmatch(r'fr-synthetic-[a-z0-9][a-z0-9-]{0,24}', options.instance), 'Açık fr-synthetic-… kimliği gerekli.')
    ports = options.ports
    require(len(set(ports)) == 3 and all(1024 <= p <= 65535 and p != 8080 for p in ports), 'Üç ayrı port gerekli; ana8080 yasak.')
    images = [options.app_image, options.operator_image, options.postgres_image]
    require(all(re.fullmatch(r'sha256:[0-9a-f]{64}', i) for i in images), 'İmajlar yerel tam sha256 kimliği olmalı.')
    folder = ROOT / '.local/synthetic' / options.instance
    no_links(folder)
    fingerprint = {str(p.relative_to(ROOT)): source_hash(p) for p in
                   [Path(__file__).resolve(), ROOT / 'deploy/New-SyntheticCertificates.ps1',
                    ROOT / 'deploy/app-user-grants.sql', ROOT / 'deploy/subscription-operator-target.sql',
                    ROOT / 'deploy/subscription-operator-identity.sql',
                    *sorted((ROOT / 'src/Server/Infrastructure/Migrations').glob('*.cs'))]}
    config = dict(version=1, root=str(folder), instance=options.instance, ports=ports, images=images, source=fingerprint)
    names = resource_names(options.instance)
    with lock():
        for image in images:
            require(json.loads(run(['docker', 'image', 'inspect', image]))[0]['Id'] == image, 'Yerel imaj bulunamadı.')
        current = inventory()
        if folder.exists():
            manifest_path = folder / 'manifest.json'
            require(manifest_path.is_file(), 'Manifest yok; mevcut dizin benimsenmez.')
            manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
            require(manifest['config'] == config, 'Aynı kimlik farklı config/kaynak/imaj ile kullanılamaz.')
            require(manifest['state'] == 'ready', 'Yarım kurulum otomatik onarılmaz; kaynaklar korunuyor.')
            for name, expected in manifest['files'].items():
                path = folder / name
                require(path.is_relative_to(folder) and '..' not in Path(name).parts, 'Geçersiz dosya referansı.')
                no_links(path)
                require(path.is_file() and file_hash(path) == expected, 'Kurulum dosyası eksik/değişmiş; üzerine yazılmaz.')
            require(resource_state(names, manifest['owner']) == manifest['resources'], 'Kaynak kimliği/config değişmiş; üzerine yazılmaz.')
            ready(ports, folder / 'private/ca.pem')
            return dict(state='verified', manifest=str(manifest_path))
        require(all(not set(resources) & current[kind] for kind, resources in names.items()), 'Kaynak adı çakışıyor; hiçbir kaynak benimsenmez.')
        require(not run(['docker', 'container', 'ls', '-a', '--filter', f'label=com.docker.compose.project={options.instance}', '-q']), 'Compose proje kimliği zaten kullanılıyor.')
        for port in ports:
            with socket.socket() as sock:
                if os.name == 'nt':
                    sock.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
                try:
                    sock.bind(('127.0.0.1', port))
                except OSError as error:
                    raise RuntimeError('Hedef port kullanımda; kurulum başlatılmadı.') from error
        child_env = dict(os.environ, ASPNETCORE_ENVIRONMENT='Development')
        migration = run(['dotnet', 'tool', 'run', 'dotnet-ef', 'migrations', 'script', '--idempotent', '--no-build',
                         '--project', 'src/Server', '--startup-project', 'src/Server'], env=child_env)
        require(migration.startswith('--') or migration.startswith('CREATE'), 'Migration çıktısı SQL değil.')
        require(all(p.stem in migration for p in (ROOT / 'src/Server/Infrastructure/Migrations').glob('*.cs')
                    if re.fullmatch(r'\d{14}_[A-Za-z0-9]+', p.stem)), 'Derlenmiş migration kaynağın gerisinde; önce build çalıştırın.')
        owner = secrets.token_hex(24)
        private_directory(folder / 'private')
        private = folder / 'private'
        manifest = dict(state='preparing', config=config, owner=owner, names=names)
        write_json(folder / 'manifest.json', manifest)
        tls_password = secrets.token_hex(32)
        write(private / 'tls-password', tls_password)
        run(['pwsh', '-NoProfile', '-File', str(ROOT / 'deploy/New-SyntheticCertificates.ps1'), '-Directory', str(private)])
        if os.name != 'nt':
            for path in private.iterdir():
                path.chmod(0o644 if path.suffix in ('.pem', '.crt', '.pfx') else 0o600)
        services, volumes, networks = {}, {}, {}
        for v in VOLUMES:
            volumes[v] = dict(name=f'{options.instance}-{v}', labels={LABEL: owner})
        for n in NETWORKS:
            networks[n] = dict(name=f'{options.instance}-{n}', labels={LABEL: owner})

        def connection(host, database, user, password):
            return f'Host={host};Database={database};Username={user};Password={password};SSL Mode=VerifyFull;Root Certificate=/tls/ca.pem;GSS Encryption Mode=Disable;Timeout=3'

        def bind(name, target):
            return dict(type='bind', source=str(private / name), target=target, read_only=True)

        admins, apps, targets = {}, {}, {}
        for suffix in ('a', 'b', 'i'):
            service = 'db-' + suffix
            database = 'operator_identity' if suffix == 'i' else 'firma_randevu'
            admins[suffix] = secrets.token_hex(32)
            environment(private / f'{service}.env', dict(POSTGRES_DB=database, POSTGRES_PASSWORD=admins[suffix]))
            services[service] = dict(image=images[2], env_file=[str(private / f'{service}.env')], networks=['identity' if suffix == 'i' else suffix],
                volumes=[f'db_{suffix}:/var/lib/postgresql', bind(service+'.crt', '/tls/db.crt'), bind(service+'.key', '/tls/db.key')],
                command=['bash', '-c', 'cp /tls/db.key /tmp/fixture.key && chown postgres:postgres /tmp/fixture.key && chmod 600 /tmp/fixture.key && exec docker-entrypoint.sh postgres -c ssl=on -c ssl_cert_file=/tls/db.crt -c ssl_key_file=/tmp/fixture.key'],
                healthcheck=dict(test=['CMD', 'pg_isready', '-U', 'postgres', '-d', database], interval='2s', timeout='3s', retries=40))
        for index, suffix in enumerate(('a', 'b')):
            apps[suffix], targets[suffix] = secrets.token_hex(32), secrets.token_hex(32)
            env = dict(ASPNETCORE_ENVIRONMENT='Development', Auth__InstanceId=options.instance+'-'+suffix, Auth__KeysDirectory='/app/keys',
                ConnectionStrings__AppDatabase=connection('db-'+suffix, 'firma_randevu', 'app_user', apps[suffix]), IdentityEmail__Mode='Local',
                BookingVerification__Enabled='false', BookingSms__DispatchEnabled='false', AppointmentReminders__Enabled='false', PublicBooking__AccessEnabled='false', Netgsm__Enabled='false')
            environment(private / f'app-{suffix}.env', env)
            environment(private / f'owner-{suffix}.env', dict(APP_BOOTSTRAP_OWNER_EMAIL='owner@example.test', APP_BOOTSTRAP_OWNER_PASSWORD='Aa!'+secrets.token_hex(24)))
            services['app-'+suffix] = dict(image=images[0], env_file=[str(private / f'app-{suffix}.env')], networks=[suffix],
                ports=[f'127.0.0.1:{ports[index]}:8080'], volumes=[f'keys_{suffix}:/app/keys', bind('ca.pem', '/tls/ca.pem')])
        identity_password = secrets.token_hex(32)
        operator_env = dict(ASPNETCORE_ENVIRONMENT='Production', ASPNETCORE_URLS='https://+:8443', AllowedHosts='localhost;127.0.0.1',
            ASPNETCORE_Kestrel__Certificates__Default__Path='/tls/operator.pfx', ASPNETCORE_Kestrel__Certificates__Default__Password=tls_password,
            Operator__InstanceId=options.instance+'-operator', Operator__KeysDirectory='/app/keys',
            ConnectionStrings__OperatorIdentity=connection('db-i', 'operator_identity', 'operator_identity_user', identity_password))
        for index, suffix in enumerate(('a', 'b')):
            operator_env[f'SubscriptionTargets__{index}__InstanceId'] = options.instance+'-'+suffix
            operator_env[f'SubscriptionTargets__{index}__Name'] = 'Sentetik '+suffix.upper()+' Salon'
            operator_env[f'SubscriptionTargets__{index}__ConnectionString'] = connection('db-'+suffix, 'firma_randevu', 'subscription_operator', targets[suffix])
        environment(private / 'operator.env', operator_env)
        environment(private / 'operator-bootstrap.env', dict(ConnectionStrings__OperatorIdentity=connection('db-i', 'operator_identity', 'postgres', admins['i']),
            OPERATOR_BOOTSTRAP_EMAIL='operator@example.test', OPERATOR_BOOTSTRAP_PASSWORD='Aa!'+secrets.token_hex(24)))
        services['operator'] = dict(image=images[1], env_file=[str(private / 'operator.env')], networks=list(NETWORKS),
            ports=[f'127.0.0.1:{ports[2]}:8443'], volumes=['keys_operator:/app/keys', bind('ca.pem', '/tls/ca.pem'), bind('operator.pfx', '/tls/operator.pfx')])
        for name, service in services.items():
            service.update(container_name=f'{options.instance}-{name}', labels={LABEL: owner}, pull_policy='never')
            service['environment'] = {'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command': 'Warning'} if not name.startswith('db-') else {}
            if not name.startswith('db-'):
                service.update(read_only=True, tmpfs=['/tmp'], security_opt=['no-new-privileges:true'], cap_drop=['ALL'])
        write_json(folder / 'compose.json', dict(name=options.instance, services=services, volumes=volumes, networks=networks))
        write(private / 'migration.sql', migration)
        compose = ['docker', 'compose', '--env-file', str(private / 'app-a.env'), '-f', str(folder / 'compose.json')]
        run(compose + ['config', '--quiet'])
        run(compose + ['up', '-d', '--wait', '--wait-timeout', '120', 'db-a', 'db-b', 'db-i'])

        def sql(suffix, text):
            database = 'operator_identity' if suffix == 'i' else 'firma_randevu'
            return run(compose + ['exec', '-T', 'db-'+suffix, 'psql', '-X', '-U', 'postgres', '-d', database, '-v', 'ON_ERROR_STOP=1'], text)

        for suffix in ('a', 'b'):
            sql(suffix, f"CREATE ROLE app_user LOGIN PASSWORD '{apps[suffix]}';\n" + migration + '\n' + (ROOT / 'deploy/app-user-grants.sql').read_text(encoding='utf-8-sig'))
            variables = f"\\set database_name firma_randevu\n\\set instance_id {options.instance}-{suffix}\n\\set target_password {targets[suffix]}\n"
            sql(suffix, variables + (ROOT / 'deploy/subscription-operator-target.sql').read_text(encoding='utf-8-sig'))
        identity = f"\\set database_name operator_identity\n\\set instance_id {options.instance}-operator\n\\set identity_password {identity_password}\n" + (ROOT / 'deploy/subscription-operator-identity.sql').read_text(encoding='utf-8-sig')
        sql('i', identity)
        run(compose + ['run', '--rm', '--no-deps', '--env-from-file', str(private / 'operator-bootstrap.env'), 'operator', 'initialize-operator'])
        sql('i', identity)
        for suffix in ('a', 'b'):
            run(compose + ['run', '--rm', '--no-deps', '--env-from-file', str(private / f'owner-{suffix}.env'), 'app-'+suffix, 'bootstrap-owner'])
        run(compose + ['up', '-d', '--no-build', 'app-a', 'app-b', 'operator'])
        ready(ports, private / 'ca.pem')
        manifest.update(state='ready', resources=resource_state(names, owner),
            files={str(p.relative_to(folder)): file_hash(p) for p in [folder / 'compose.json', *private.iterdir()]})
        write_json(folder / 'manifest.json', manifest)
        return dict(state='created', manifest=str(folder / 'manifest.json'))


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--instance', required=True)
    parser.add_argument('--ports', type=int, nargs=3, required=True, metavar=('FIRM_A', 'FIRM_B', 'OPERATOR'))
    for name in ('app', 'operator', 'postgres'):
        parser.add_argument('--'+name+'-image', required=True)
    return parser.parse_args()


def main():
    try:
        print(json.dumps(initialize(arguments()), ensure_ascii=False))
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        return 1
    except (ValueError, KeyError, OSError):
        # Exception details can contain environment values supplied by a malformed file.
        print('Sentetik kurulum reddedildi/başarısız. Hedef, manifest, imaj ve sağlık koşullarını denetleyin; mevcut kaynaklar korunmuştur.', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
