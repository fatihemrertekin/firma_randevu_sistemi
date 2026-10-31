"""P06-03 negative isolation and bounded-resource acceptance on the owned fixture only."""
import ipaddress
import json
import math
import ssl
import statistics
import subprocess
import time
import urllib.error
import urllib.request

import initialize_synthetic as fixture


def check(value, message):
    fixture.require(value, message)


def execute(args, data=None):
    try:
        result = subprocess.run(args, input=None if data is None else data.encode('utf-8'),
            capture_output=True, timeout=35, cwd=fixture.ROOT)
        result.stdout = result.stdout.decode('utf-8', errors='replace')
        result.stderr = result.stderr.decode('utf-8', errors='replace')
        return result
    except subprocess.TimeoutExpired as error:
        raise RuntimeError('İzolasyon kontrolünün süre sınırı aşıldı.') from error


def request(opener, url, body=None, headers=None):
    req = urllib.request.Request(url, None if body is None else json.dumps(body).encode(),
        dict({'Content-Type': 'application/json'}, **(headers or {})))
    try:
        with opener.open(req, timeout=12) as response:
            data = response.read()
            return response.status, json.loads(data) if data else None
    except urllib.error.HTTPError as error:
        data = error.read()
        return error.code, json.loads(data) if data else None


def verify_isolation(options, folder, compose, sessions, sql, before, snapshot):
    private = folder / 'private'
    manifest = json.loads((folder / 'manifest.json').read_text())
    config = json.loads((folder / 'compose.json').read_text())
    report = dict(pass_=False, instance=options.instance, checks=[], measurements={}, mainTouched=False)

    def passed(name, count=None):
        report['checks'].append(dict(name=name, count=count))
        fixture.write_json(folder / 'isolation.json', report)
        print('P06-03: '+name+' geçti.', flush=True)

    def inspect(service):
        return json.loads(fixture.run(['docker', 'inspect', options.instance+'-'+service]))[0]

    def inside(service, *args):
        return fixture.run(compose + ['exec', '-T', service, *args])

    def env(name):
        return dict(line.split('=', 1) for line in (private / name).read_text().splitlines())

    def password(connection):
        return connection.split('Password=', 1)[1].split(';')[0]

    containers = {s: inspect(s) for s in fixture.SERVICES}
    expected_nets = {'app-a': ['a'], 'app-b': ['b'], 'db-a': ['a', 'operator-a'],
        'db-b': ['b', 'operator-b'], 'db-i': ['identity'], 'operator': ['operator-a', 'operator-b', 'identity']}
    for name in fixture.NETWORKS:
        network = json.loads(fixture.run(['docker', 'network', 'inspect', options.instance+'-'+name]))[0]
        check(not network['Internal'], 'Fixture loopback yayını için köprü ağı gerekli.')
    for service, item in containers.items():
        check(item['Config']['Labels'].get(fixture.LABEL) == manifest['owner'], 'Yabancı fixture kaynağı.')
        check(set(item['NetworkSettings']['Networks']) == {options.instance+'-'+n for n in expected_nets[service]}, service+': ağ matrisi yanlış.')
        check(not item['HostConfig']['Privileged'] and not item['HostConfig']['PidMode'], service+': geniş host erişimi.')
        check(item['HostConfig']['NanoCpus'] == 500000000 and item['HostConfig']['PidsLimit'] == 128, service+': CPU/PID sınırı eksik.')
        limit = (384 if service.startswith('db-') else 512) * 1024**2
        check(item['HostConfig']['Memory'] == item['HostConfig']['MemorySwap'] == limit, service+': bellek/swap sınırı eksik.')
        check(item['HostConfig']['LogConfig'] == {'Type': 'json-file', 'Config': {'max-size': '1m', 'max-file': '2'}}, service+': log sınırı eksik.')
        if service.startswith('db-'):
            check(item['Config']['Healthcheck']['Test'][:4] == ['CMD', 'pg_isready', '-h', '127.0.0.1'], 'DB sağlığı geçici init soketini hazır sayıyor.')
            check(not item['HostConfig']['PortBindings'], 'DB portu hosta açıldı.')
            allowed = {'/var/lib/postgresql', '/tls/db.crt', '/tls/db.key', '/tls/ca.pem'}
            volume = 'db_'+service[-1]
        else:
            allowed = {'/app/keys', '/tls/ca.pem'} | ({'/tls/operator.pfx'} if service == 'operator' else set())
            volume = 'keys_operator' if service == 'operator' else 'keys_'+service[-1]
            check(item['Config']['User'] not in ('', '0', 'root') and inside(service, 'id', '-u') != '0', 'Uygulama root çalışıyor.')
            check(item['HostConfig']['ReadonlyRootfs'] and item['HostConfig']['CapDrop'] == ['ALL'], 'Salt okunur/cap-drop koruması yok.')
            status = inside(service, 'cat', '/proc/1/status')
            check('CapEff:\t0000000000000000' in status and 'NoNewPrivs:\t1' in status, 'Çalışan süreç yetkisi geniş.')
            check(execute(compose + ['exec', '-T', service, 'touch', '/app/p06-write-denied']).returncode != 0, 'Uygulama köküne yazılabiliyor.')
            inside(service, 'sh', '-c', 'test ! -e /var/run/docker.sock && test ! -e /run/docker.sock && test ! -e /tls/db.key')
            for binding in item['HostConfig']['PortBindings'].values():
                check(all(b['HostIp'] == '127.0.0.1' for b in binding), 'Loopback dışına uygulama yayını.')
        check({m['Destination'] for m in item['Mounts']} == allowed, service+': beklenmeyen mount.')
        for mount in item['Mounts']:
            if mount['Type'] == 'volume':
                check(mount['Name'] == options.instance+'-'+volume, 'Başka firmanın volume erişimi.')
            else:
                wanted = next(v['source'] for v in config['services'][service]['volumes'] if isinstance(v, dict) and v['target'] == mount['Destination']).replace('\\', '/')
                sources = {wanted}
                if wanted[1:2] == ':':
                    sources.add('/run/desktop/mnt/host/'+wanted[0].lower()+wanted[2:])
                check(mount['Type'] == 'bind' and not mount['RW'] and mount['Source'].replace('\\', '/') in sources, 'Yazılabilir/yabancı dosya mount.')
    check(containers['app-a']['Image'] == containers['app-b']['Image'] == options.app_image, 'A/B firma imajı farklı.')
    passed('Ağ, mount, non-root, read-only ve çalışan süreç sınırları', 6)

    def tcp(source, target, port):
        for network in containers[target]['NetworkSettings']['Networks'].values():
            address = str(ipaddress.ip_address(network['IPAddress']))
            result = execute(compose + ['exec', '-T', source, 'bash', '-c', f'timeout 2 bash -c "exec 3<>/dev/tcp/{address}/{port}"'])
            check(result.returncode in (0, 1, 124), 'TCP probu çalıştırılamadı.')
            yield result.returncode == 0

    for suffix, other in (('a', 'b'), ('b', 'a')):
        check(any(tcp('app-'+suffix, 'db-'+suffix, 5432)), 'Pozitif DB ağı kontrolü başarısız.')
        for target, port in [('db-'+other, 5432), ('db-i', 5432)]:
            check(not any(tcp('app-'+suffix, target, port)), 'Firma yasak ağa erişiyor: '+target)
        # Yayımlanmış web yüzeyi erişilebiliyorsa HTTP yetkisi reddedilmeli.
        for target, port in [('app-'+other, 8080), ('operator', 8443)]:
            reachable = list(tcp('app-'+suffix, target, port))
            report.setdefault('publishedWebPaths', []).append(dict(source='app-'+suffix, target=target, tcpReachable=any(reachable)))
            if any(reachable):
                network = list(containers[target]['NetworkSettings']['Networks'].values())[reachable.index(True)]
                address = str(ipaddress.ip_address(network['IPAddress']))
                if port == 8443:
                    command = ['timeout', '8', 'openssl', 's_client', '-connect', address+':8443', '-servername', 'localhost', '-verify_hostname', 'localhost', '-verify_return_error', '-CAfile', '/tls/ca.pem', '-quiet', '-ignore_unexpected_eof']
                else:
                    command = ['bash', '-c', f'exec 3<>/dev/tcp/{address}/8080; cat >&3; cat <&3']
                result = execute(compose + ['exec', '-T', 'app-'+suffix, *command], 'GET /api/auth/me HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n')
                check(result.returncode == 0 and result.stdout.startswith('HTTP/1.1 401'), 'Yayımlanan web yüzeyi kimliksiz isteği reddetmedi.')
    for service in ('db-a', 'db-b', 'db-i'):
        check(any(tcp('operator', service, 5432)), 'Operatör izinli DB ağına erişemiyor.')
    passed('İzinli DB TCP, özel DB/Identity ağ reddi ve webde 401', 13)

    apps = {s: password(env(f'app-{s}.env')['ConnectionStrings__AppDatabase']) for s in ('a', 'b')}
    op_env = env('operator.env')
    targets = {s: password(op_env[f'SubscriptionTargets__{i}__ConnectionString']) for i, s in enumerate(('a', 'b'))}

    def db_check(suffix, user, secret, query, expected=None, host=None, ca='/tls/ca.pem'):
        host = host or 'db-'+suffix
        database = 'operator_identity' if suffix == 'i' else 'firma_randevu'
        data = f"\\setenv PGPASSWORD '{secret}'\n\\connect 'host={host} dbname={database} user={user} sslmode=verify-full sslrootcert={ca} connect_timeout=2'\n{query}\n"
        result = execute(compose + ['exec', '-T', 'db-'+suffix, 'psql', '-X', '-U', 'postgres', '-d', database, '-At', '-v', 'ON_ERROR_STOP=1', '-v', 'VERBOSITY=verbose'], data)
        if expected:
            check(result.returncode != 0 and expected in result.stderr, 'Beklenen DB reddi yok: '+user+'/'+expected)
        else:
            check(result.returncode == 0, 'Pozitif DB/TLS/yetki kontrolü başarısız: '+user)
        return result.stdout

    for suffix, other in (('a', 'b'), ('b', 'a')):
        db_check(suffix, 'app_user', apps[suffix], 'SELECT count(*) FROM "BusinessProfiles";')
        db_check(suffix, 'app_user', apps[other], 'SELECT 1;', 'password authentication failed')
        db_check(suffix, 'app_user', apps[suffix], 'SELECT 1;', 'does not match host name', host='localhost')
        db_check(suffix, 'app_user', apps[suffix], 'SELECT 1;', 'certificate verify failed', ca='/tls/db.crt')
        for query in ('CREATE TABLE p06_forbidden (id int);', 'UPDATE "SubscriptionPeriods" SET "Version"="Version" WHERE false;', "SELECT pg_read_file('PG_VERSION');"):
            db_check(suffix, 'app_user', apps[suffix], query, '42501')
        db_check(suffix, 'subscription_operator', targets[suffix], 'SELECT count(*) FROM "SubscriptionPeriods"; UPDATE "SubscriptionPeriods" SET "Version"="Version" WHERE false;')
        for query in ('SELECT count(*) FROM "AspNetUsers";', 'SELECT count(*) FROM "BusinessProfiles";', 'DELETE FROM "SubscriptionPeriods" WHERE false;'):
            db_check(suffix, 'subscription_operator', targets[suffix], query, '42501')
        check(sql(suffix, "SELECT count(*) FROM pg_roles WHERE rolname IN ('app_user','subscription_operator') AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls);") == '0', 'Geniş DB rolü.')
    identity_secret = password(op_env['ConnectionStrings__OperatorIdentity'])
    db_check('i', 'operator_identity_user', identity_secret, 'SELECT count(*) FROM "AspNetUsers";')
    passed('Ayrı DB parolaları, dar rol ve yanlış TLS host/CA reddi', 23)

    bases = dict(a=f'http://127.0.0.1:{options.ports[0]}', b=f'http://127.0.0.1:{options.ports[1]}', operator=f'https://127.0.0.1:{options.ports[2]}')
    anonymous = urllib.request.build_opener(urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=str(private/'ca.pem'))))
    cookies, csrf = {}, {}
    for name, (client, jar) in sessions.items():
        check(request(client, bases[name]+'/api/auth/me')[0] == 200, 'Pozitif MFA oturumu çalışmıyor.')
        csrf[name] = request(client, bases[name]+'/api/auth/csrf')[1]['token']
        cookies[name] = {c.name: c.value for c in jar}
    auth_names = {n: '__Host-SubscriptionOperator' if n == 'operator' else 'FirmaRandevu.Dev' for n in cookies}
    csrf_names = {}
    for name, values in cookies.items():
        names = [k for k in values if 'csrf' in k.lower() or 'antiforgery' in k.lower()]
        check(auth_names[name] in values and len(names) == 1, 'Beklenen Identity/CSRF cookie sözleşmesi yok.')
        csrf_names[name] = names[0]
    for source in sessions:
        for target in sessions:
            if source == target:
                continue
            forged = {auth_names[target]: cookies[source][auth_names[source]]}
            header = {'Cookie': '; '.join(k+'='+v for k, v in forged.items())}
            check(request(anonymous, bases[target]+'/api/auth/me', headers=header)[0] == 401, 'Yabancı oturum kabul edildi.')
            header = {'Cookie': auth_names[target]+'='+cookies[target][auth_names[target]]+'; '+csrf_names[target]+'='+cookies[source][csrf_names[source]], 'X-CSRF-TOKEN': csrf[source]}
            status, problem = request(anonymous, bases[target]+'/api/auth/logout', {}, header)
            check(status == 400 and problem.get('code') == 'csrf_invalid', 'Yabancı CSRF çifti kabul edildi.')
    passed('Üç oturum arasında isimleri eşleştirilmiş cookie/CSRF değiş tokuşu reddi', 12)

    operator = sessions['operator'][0]
    route = lambda suffix: bases['operator']+'/api/targets/'+options.instance+'-'+suffix+'/subscription'
    check(request(operator, bases['operator']+'/api/targets/not-allowed/subscription')[0] == 404, 'Bilinmeyen hedef kabul edildi.')
    for suffix in ('a', 'b'):
        check(request(operator, route(suffix))[0] == 200, 'İzinli mali API kontrolü başarısız.')
    try:
        sql('a', "ALTER DATABASE firma_randevu SET app.subscription_instance='unknown';")
        check(request(operator, route('a'))[0] == 503 and request(operator, route('b'))[0] == 200, 'Yanlış firma marker reddi yok.')
    finally:
        sql('a', f"ALTER DATABASE firma_randevu SET app.subscription_instance='{options.instance}-a';")
    try:
        sql('a', 'ALTER ROLE subscription_operator CREATEDB;')
        check(request(operator, route('a'))[0] == 503, 'Geniş mali rol kabul edildi.')
    finally:
        sql('a', 'ALTER ROLE subscription_operator NOCREATEDB;')
    try:
        sql('i', "ALTER DATABASE operator_identity SET app.operator_identity='unknown';")
        check(request(operator, bases['operator']+'/api/auth/me')[0] == 503, 'Yanlış Identity marker kabul edildi.')
    finally:
        sql('i', f"ALTER DATABASE operator_identity SET app.operator_identity='{options.instance}-operator';")
    try:
        fixture.run(compose + ['stop', 'db-b'])
        start = time.monotonic()
        check(request(operator, route('b'))[0] == 503, 'Kapalı hedef başarılı sayıldı.')
        report['measurements']['offlineTargetSeconds'] = round(time.monotonic()-start, 3)
        check(request(operator, route('a'))[0] == request(operator, bases['operator']+'/health/ready')[0] == 200, 'B arızası A/merkezi kapattı.')
    finally:
        fixture.run(compose + ['start', 'db-b'])
        fixture.ready(options.ports, private/'ca.pem')
    # Merkez sağlığı hedef havuzundaki kesilmiş bağlantıların toparlandığını göstermez.
    start = time.monotonic()
    recovery_statuses = []
    while True:
        status = request(operator, route('b'))[0]
        recovery_statuses.append(status)
        if status == 200:
            break
        check(status == 503 and time.monotonic()-start < 20, 'DB kesintisinden sonra hedef 20 saniyede toparlanmadı: '+str(status))
        time.sleep(.25)
    report['measurements']['targetRecoverySeconds'] = round(time.monotonic()-start, 3)
    report['measurements']['targetRecoveryStatuses'] = recovery_statuses
    passed('Bilinmeyen hedef, firma/Identity marker, geniş rol ve kapalı DB reddi', 5)

    limits = inside('app-a', 'sh', '-c', 'cat /sys/fs/cgroup/cpu.max /sys/fs/cgroup/memory.max /sys/fs/cgroup/memory.swap.max /sys/fs/cgroup/pids.max').splitlines()
    check(limits == ['50000 100000', str(512*1024**2), '0', '128'], 'Çalışan cgroup sınırları farklı.')
    def throttled():
        return int(dict(line.split() for line in inside('app-a', 'cat', '/sys/fs/cgroup/cpu.stat').splitlines())['nr_throttled'])
    start = throttled()
    check(execute(compose + ['exec', '-T', 'app-a', 'bash', '-c', 'timeout 3 bash -c "while :; do :; done"']).returncode == 124, 'CPU probu çalışmadı.')
    check(throttled() > start, 'CPU kotasının uygulanması ölçülemedi.')
    report['measurements']['cpuThrottledPeriods'] = throttled()-start
    try:
        result = execute(compose + ['exec', '-T', 'app-a', 'dd', 'if=/dev/zero', 'of=/tmp/p06-limit', 'bs=1M', 'count=65', 'status=none'])
        check(result.returncode != 0 and 'No space left on device' in result.stderr, 'Geçici disk limiti uygulanmadı.')
    finally:
        inside('app-a', 'rm', '-f', '/tmp/p06-limit')
    passed('Gerçek CPU throttling, cgroup bellek/süreç ve 64MiB geçici disk sınırı', 4)

    # Aynı sürücü/limit ile sahte log üret; Docker'ın log dosyalarını hosttan açma.
    probe = options.instance+'-log-probe'
    script = 'echo P06-BEGIN; line=$(head -c 1024 /dev/zero | tr "\\000" X); i=0; while [ "$i" -lt 4096 ]; do printf "%s\\n" "$line"; i=$((i+1)); done; echo P06-END'
    fixture.run(['docker', 'run', '-d', '--name', probe, '--label', fixture.LABEL+'='+manifest['owner'], '--network', 'none', '--read-only', '--user', '65534', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true', '--memory', '32m', '--cpus', '0.1', '--pids-limit', '16', '--log-driver', 'json-file', '--log-opt', 'max-size=1m', '--log-opt', 'max-file=2', '--entrypoint', 'sh', options.postgres_image, '-c', script])
    check(fixture.run(['docker', 'wait', probe]) == '0', 'Log probu çalışmadı.')
    logs = fixture.run(['docker', 'logs', probe])
    check('P06-BEGIN' not in logs and 'P06-END' in logs and len(logs.encode()) < 2*1024**2, 'Log rotasyonu eski kayıtları sınırlamadı.')
    report['measurements']['retainedProbeLogBytes'] = len(logs.encode())
    passed('1MiB×2 log rotasyonu gerçek 4MiB sahte çıktı ile doğrulandı')

    elapsed = []
    for i in range(10):
        start = time.monotonic()
        check(request(operator, route('a' if i % 2 == 0 else 'b'))[0] == 200, 'Sınırlı kaynakla mali okuma başarısız.')
        elapsed.append((time.monotonic()-start)*1000)
    report['measurements']['ledgerReadMs'] = dict(samples=len(elapsed), p50=round(statistics.median(elapsed), 2), p95=round(sorted(elapsed)[math.ceil(.95*len(elapsed))-1], 2))
    report['measurements']['containers'] = [json.loads(line) for line in fixture.run(['docker', 'stats', '--no-stream', '--format', '{{json .}}', *manifest['names']['container']]).splitlines()]
    report['measurements']['databaseBytes'] = {s: int(sql(s, 'SELECT pg_database_size(current_database());')) for s in ('a', 'b', 'i')}
    report['measurements']['dataVolumeKiB'] = {s: int(inside('db-'+s, 'du', '-sk', '/var/lib/postgresql').split()[0]) for s in ('a', 'b', 'i')}
    report['measurements']['persistentVolumeHardQuota'] = False
    secret_values = [*apps.values(), *targets.values(), identity_secret, (private/'tls-password').read_text()]
    secret_values += [v for f in private.glob('*.env') for k, v in env(f.name).items() if k.endswith('PASSWORD')]
    secret_values += [v for c in cookies.values() for v in c.values()] + list(csrf.values())
    for service in fixture.SERVICES:
        result = execute(['docker', 'logs', options.instance+'-'+service])
        check(result.returncode == 0 and all(v not in result.stdout+result.stderr for v in secret_values), 'Logda sır/oturum değeri bulundu.')
    check(snapshot() == before, 'Negatif testten sonra DB/marker/anahtar farkı kaldı.')
    report.update(pass_=True, databaseMarkersKeysPreserved=True, capacityGuarantee=False)
    passed('Log sır taraması, sınırlı okuma ölçümü ve tam veri/marker/anahtar eşitliği')
