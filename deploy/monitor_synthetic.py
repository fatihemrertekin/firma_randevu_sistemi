"""One-shot, local synthetic monitoring; no scheduler, repair or external recipient."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import socket
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
import zipfile

sys.dont_write_bytecode=True
import initialize_synthetic as fixture
import synthetic_recovery as recovery

SIGNALS={'app_live','app_ready','database','observer','backup_missing','backup_age','backup_failure','backup_integrity','host_disk','database_disk','app_disk','tls'}
LEVELS={'warning','critical','resolved'}


def root(instance):
    fixture.require(Path.cwd().resolve()==fixture.ROOT and re.fullmatch(r'fr-synthetic-[a-z0-9][a-z0-9-]{0,24}',instance),'Depo kökü ve açık sentetik hedef gerekli.')
    return recovery.local_path(fixture.ROOT/'.local/monitoring'/instance)


def save(path,value):
    fixture.no_links(path);temporary=path.with_suffix('.tmp');fixture.no_links(temporary)
    fixture.write_json(temporary,value);os.replace(temporary,path)


def read(path):
    fixture.no_links(path);fixture.require(path.stat().st_size<=128*1024,'İzleme kayıt boyutu geçersiz.')
    return json.loads(path.read_text(encoding='utf-8'))


def fingerprint(item):
    return fixture.digest([item['Image'],item['Config'],item['HostConfig'],sorted(item['Mounts'],key=lambda m:m['Destination'])])


def arm(instance,receiver_port):
    folder=root(instance);source=fixture.ROOT/'.local/synthetic'/instance
    fixture.no_links(source)
    with fixture.lock():
        fixture.require(not folder.exists(),'İzleme kaydı mevcut; otomatik yeniden hedefleme yok.')
        manifest=read(source/'manifest.json');config=manifest['config']
        fixture.require(manifest['state']=='ready' and config['instance']==instance and manifest['names']==fixture.resource_names(instance),'Hazır kaynak kimliği farklı.')
        fixture.require(1024<=receiver_port<=65535 and receiver_port not in [8080,8081,*config['ports']],'Yalnız ayrı loopback sahte alıcı portu gerekli.')
        for name,h in config['source'].items():
            path=fixture.ROOT/name;fixture.no_links(path)
            fixture.require(path.is_relative_to(fixture.ROOT) and '..' not in path.parts and fixture.source_hash(path)==h,'Kurulum kaynağı değişmiş.')
        for name,h in manifest['files'].items():
            path=source/name;fixture.no_links(path)
            fixture.require(path.is_relative_to(source) and '..' not in path.parts and fixture.file_hash(path)==h,'Kaynak dosyası değişmiş.')
        fixture.require(fixture.resource_state(manifest['names'],manifest['owner'])==manifest['resources'],'İlk kaynak doğrulaması başarısız.')
        items=json.loads(fixture.run(['docker','inspect',*manifest['names']['container']]))
        fixture.private_directory(folder)
        ca=(source/'private/ca.pem').read_bytes();(folder/'ca.pem').write_bytes(ca)
        state=dict(version=1,instance=instance,firm=instance+'-a',owner=manifest['owner'],ports=config['ports'],receiverPort=receiver_port,
            manifestHash=fixture.file_hash(source/'manifest.json'),caHash=hashlib.sha256(ca).hexdigest(),
            containers={v['Name'].lstrip('/'):dict(id=v['Id'],hash=fingerprint(v)) for v in items},active={},pending=[],attempts=0,nextAttempt=0)
        save(folder/'state.json',state)
    return dict(state='armed',firm=instance+'-a')


def load(instance):
    folder=root(instance);state=read(folder/'state.json')
    fixture.require(state['version']==1 and state['instance']==instance and state['firm']==instance+'-a','İzleme kimliği farklı.')
    fixture.require(set(state['containers'])==set(fixture.resource_names(instance)['container']),'İzleme hedefleri farklı.')
    fixture.require(len(state['ports'])==3 and all(type(p) is int and 1024<=p<=65535 and p!=8080 for p in state['ports']) and len(set(state['ports']))==3,'İzleme portları geçersiz.')
    fixture.require(type(state['receiverPort']) is int and 1024<=state['receiverPort']<=65535 and state['receiverPort'] not in [8080,8081,*state['ports']],'Sahte alıcı portu geçersiz.')
    fixture.no_links(folder/'ca.pem');fixture.require(fixture.file_hash(folder/'ca.pem')==state['caHash'],'İzleme CA değişmiş.')
    fixture.require(set(state['active'])<=SIGNALS and all(v in {'warning','critical'} for v in state['active'].values()),'İzleme sinyali geçersiz.')
    fixture.require(type(state['attempts']) is int and 0<=state['attempts']<=3 and math.isfinite(state['nextAttempt']) and 0<=state['nextAttempt']<=4102444800,'Teslim deneme kaydı geçersiz.')
    fixture.require(isinstance(state['pending'],list) and len(state['pending'])<=64,'İzleme bekleyen kayıt sınırı.')
    for event in state['pending']:validate_event(event,state['firm'])
    fixture.require(len({e['id'] for e in state['pending']})==len(state['pending']),'Mükerrer olay kimliği.')
    return folder,state


def validate_event(event,firm):
    fixture.require(set(event)=={'id','firm','signal','level','at'} and str(uuid.UUID(event['id']))==event['id'] and event['firm']==firm and
        event['signal'] in SIGNALS and event['level'] in LEVELS and type(event['at']) in (int,float) and math.isfinite(event['at']) and 0<=event['at']<=4102444800,'Sahte alıcı olay sözleşmesi geçersiz.')


def backup(instance,package,key):
    folder,state=load(instance);source=fixture.ROOT/'.local/synthetic'/instance
    package,key=map(recovery.local_path,(package,key))
    fixture.require(fixture.file_hash(source/'manifest.json')==state['manifestHash'],'Yedek kaynağı değişmiş.')
    receipt=read(folder/'backup.json') if (folder/'backup.json').exists() else dict(firm=state['firm'],good=None)
    fixture.require(receipt['firm']==state['firm'],'Yedek firma kaydı farklı.')
    try:
        result=recovery.backup(source,package,key)
        metadata,_,_=recovery.read_package(package,key,state['firm'])
        receipt.update(good=dict(package=str(package.relative_to(fixture.ROOT)),sha256=fixture.file_hash(package),createdUtc=metadata['createdUtc']),lastAttempt=time.time(),failed=False)
    except (RuntimeError,OSError,ValueError,KeyError,zipfile.BadZipFile):
        receipt.update(lastAttempt=time.time(),failed=True)
        with fixture.lock():save(folder/'backup.json',receipt)
        raise RuntimeError('Sentetik yedek başarısız; önceki başarılı yedek bilgisi korundu. A/operatör durumunu kontrol edin.')
    with fixture.lock():save(folder/'backup.json',receipt)
    return dict(state='verified_backup',firm=state['firm'],encryptedBytes=result['encryptedBytes'])


def command(args):
    try:
        result=subprocess.run(args,capture_output=True,timeout=5,cwd=fixture.ROOT)
        fixture.require(result.returncode==0 and len(result.stdout)<=1024*1024,'İzleme alt işlemi başarısız; ayrıntılar gizlendi.')
        return result.stdout.decode('utf-8').strip()
    except (OSError,subprocess.TimeoutExpired,UnicodeError) as error:raise RuntimeError('İzleme alt işlemi kullanılamıyor.') from error


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args,**kwargs):return None


def http_ok(url):
    try:
        with urllib.request.build_opener(urllib.request.ProxyHandler({}),NoRedirect()).open(url,timeout=3) as response:return response.status==200
    except (OSError,urllib.error.URLError):return False


def collect(folder,state):
    base=f"http://127.0.0.1:{state['ports'][0]}"
    samples=dict(app_live=http_ok(base+'/health/live'),app_ready=http_ok(base+'/health/ready'),database=False,observer=False,database_disk=None,app_disk=None,tls=None)
    try:items=json.loads(command(['docker','inspect',*state['containers']]))
    except RuntimeError:items=None
    if items is not None:
        for item in items:
            name=item['Name'].lstrip('/');pin=state['containers'][name]
            fixture.require(item['Id']==pin['id'] and item['Config']['Labels'].get(fixture.LABEL)==state['owner'] and fingerprint(item)==pin['hash'],'Hedef sahiplik/config değişmiş; otomatik benimseme veya bildirim yok.')
            for index,service in enumerate(('app-a','app-b','operator')):
                if name==state['instance']+'-'+service:
                    port='8443/tcp' if service=='operator' else '8080/tcp'
                    fixture.require(item['HostConfig']['PortBindings'][port]==[{'HostIp':'127.0.0.1','HostPort':str(state['ports'][index])}],'İzleme port/hedef bağı farklı.')
        samples['observer']=True
        database=state['instance']+'-db-a'
        try:
            samples['database']=command(['docker','exec',database,'psql','-X','-U','postgres','-d','firma_randevu','-Atc','SELECT 1;'])=='1'
            disk=command(['docker','exec',database,'df','-P','/var/lib/postgresql']).splitlines()[-1].split()
            samples['database_disk']=int(disk[-2].rstrip('%'))
        except (RuntimeError,ValueError,IndexError):pass
        try:samples['app_disk']=int(command(['docker','exec',state['instance']+'-app-a','df','-P','/tmp']).splitlines()[-1].split()[-2].rstrip('%'))
        except (RuntimeError,ValueError,IndexError):pass
    usage=shutil.disk_usage(fixture.ROOT);samples['host_disk']=usage.used*100/usage.total;samples['host_free_bytes']=usage.free
    try:
        context=ssl.create_default_context(cafile=str(folder/'ca.pem'))
        with socket.create_connection(('127.0.0.1',state['ports'][2]),timeout=3) as connection:
            with context.wrap_socket(connection,server_hostname='127.0.0.1') as tls:samples['tls']=ssl.cert_time_to_seconds(tls.getpeercert()['notAfter'])
    except (OSError,ValueError,KeyError):pass
    # Mevcut HTTP kapısı pooled OpenAsync kullanır; canlı DB sorgusu olmadan hazır sayma.
    samples['ready_http']=samples['app_ready'];samples['app_ready']=samples['app_ready'] and samples['database']
    return samples


def signals(samples,receipt,now):
    active={k:'critical' for k in ('app_live','app_ready','database','observer') if not samples[k]}
    for key in ('host_disk','database_disk','app_disk'):
        value=samples[key]
        if value is None:active[key]='critical'
        else:
            fixture.require(type(value) in (int,float) and math.isfinite(value) and 0<=value<=100,'Disk ölçümü geçersiz.')
            if value>=70:active[key]='critical' if value>=85 else 'warning'
    expiry=samples['tls']
    fixture.require(expiry is None or type(expiry) in (int,float) and math.isfinite(expiry),'TLS ölçümü geçersiz.')
    if expiry is None or expiry-now<=86400:active['tls']='critical'
    elif expiry-now<=2*86400:active['tls']='warning'
    if receipt is None or receipt['good'] is None:active['backup_missing']='critical'
    else:
        good=receipt['good'];age=now-good['createdUtc']
        fixture.require(math.isfinite(age) and age>=0,'Yedek zamanı geçersiz/ileride.')
        if age>86400:active['backup_age']='warning'
        try:valid=fixture.file_hash(recovery.local_path(fixture.ROOT/good['package']))==good['sha256']
        except OSError:valid=False
        if not valid:active['backup_integrity']='critical'
    if receipt and receipt['failed']:active['backup_failure']='critical'
    return active


def advance(state,active,now):
    events=[dict(id=str(uuid.uuid4()),firm=state['firm'],signal=key,level=active.get(key,'resolved'),at=now)
        for key in sorted(set(active)|set(state['active'])) if active.get(key)!=state['active'].get(key)]
    fixture.require(len(state['pending'])+len(events)<=64,'Teslim kuyruğu dolu; kayıt korunuyor, elle inceleme gerekli.')
    state['active']=active;state['pending']+=events
    return len(events)


def send(port,events):
    request=urllib.request.Request(f'http://127.0.0.1:{port}/events',json.dumps(events).encode(),{'Content-Type':'application/json'},method='POST')
    with urllib.request.build_opener(urllib.request.ProxyHandler({}),NoRedirect()).open(request,timeout=3) as response:
        fixture.require(response.status==200,'Sahte alıcı onayı başarısız.')
        fixture.require(json.loads(response.read(8193))=={'accepted':[e['id'] for e in events]},'Sahte alıcı onayı eşleşmiyor.')


def deliver(folder,state,now):
    if state['pending'] and state['attempts']<3 and now>=state['nextAttempt']:
        state['attempts']+=1;state['nextAttempt']=now+30*2**(state['attempts']-1)
        save(folder/'state.json',state)
        try:send(state['receiverPort'],state['pending'])
        except (RuntimeError,OSError,ValueError,urllib.error.URLError):pass
        else:state.update(pending=[],attempts=0,nextAttempt=0)
        save(folder/'state.json',state)


def scan(instance,now=None):
    now=time.time() if now is None else now
    fixture.require(type(now) in (int,float) and math.isfinite(now) and 0<=now<=4102444800,'İzleme saati geçersiz.')
    with fixture.lock():
        folder,state=load(instance);samples=collect(folder,state)
        receipt=read(folder/'backup.json') if (folder/'backup.json').exists() else None
        if receipt:fixture.require(receipt['firm']==state['firm'] and type(receipt['failed']) is bool,'Yedek kimliği/durumu farklı.')
        emitted=advance(state,signals(samples,receipt,now),now)
        # Göndermeden önce kalıcı kimlik; yanıt kaybında alıcı aynı kimliği tekrar güvenle kabul eder.
        save(folder/'state.json',state)
        deliver(folder,state,now)
        return dict(firm=state['firm'],active=state['active'],emitted=emitted,pending=len(state['pending']),delivery='blocked' if state['attempts']==3 else 'pending' if state['pending'] else 'acknowledged',samples=samples)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='operation',required=True)
    for name in ('arm','backup','scan'):
        p=sub.add_parser(name);p.add_argument('--instance',required=True)
        if name=='arm':p.add_argument('--receiver-port',type=int,required=True)
        if name=='backup':p.add_argument('--package',required=True);p.add_argument('--key',required=True)
    args=vars(parser.parse_args());operation=args.pop('operation')
    try:
        result=dict(arm=arm,backup=backup,scan=scan)[operation](**args);print(json.dumps(result))
        sys.exit(3 if result.get('pending') else 2 if result.get('active') else 0)
    except (RuntimeError,ValueError,TypeError,KeyError,OSError,zipfile.BadZipFile):
        print('Sentetik izleme reddedildi/başarısız; hedef ve kayıtlar korunur, sır içerebilen ayrıntılar gizlendi.',file=sys.stderr);sys.exit(1)
