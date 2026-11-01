"""Closed-by-default recovery of the single fake operator in an owned synthetic fixture."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import secrets
import socket
import sys
import tarfile
import zipfile

import initialize_synthetic as fixture
import synthetic_recovery as recovery

TABLES = {'AspNetUsers', 'AspNetRoles', 'AspNetUserClaims', 'AspNetRoleClaims', 'AspNetUserRoles', 'AspNetUserLogins', 'AspNetUserTokens'}
PROVISION = fixture.ROOT/'deploy/subscription-operator-identity.sql'
INVALIDATE = fixture.ROOT/'deploy/Invalidate-RestoredOperatorAccess.sql'
check = fixture.require


def sql(container, text, database='operator_identity'):
    return recovery.binary(['docker','exec','-i',container,'psql','-X','-U','postgres','-d',database,'-At','-v','ON_ERROR_STOP=1'],text.encode()).decode().strip()


def schema(container, database='operator_identity'):
    return recovery.binary(['docker','exec',container,'pg_dump','-U','postgres','-d',database,'--schema-only','--no-owner','--no-acl','--restrict-key=OperatorRecovery'])


def snapshot(container):
    rows=sql(container,"""SELECT format($q$SELECT json_build_object('table',%L,'rows',count(*),'hash',encode(sha256(convert_to(coalesce(string_agg(row_to_json(t)::text,'' ORDER BY row_to_json(t)::text),''),'UTF8')),'hex')) FROM %I t;$q$,tablename,tablename) FROM pg_tables WHERE schemaname='public' ORDER BY tablename
\\gexec
""")
    entries=[json.loads(row) for row in rows.splitlines()]
    check({e['table'] for e in entries}==TABLES,'Yalnız yedi operatör Identity tablosu kabul edilir.')
    check(sql(container,'SELECT count(*) FROM "AspNetUsers";')=='1' and sql(container,'SELECT "Email" FROM "AspNetUsers";')=='operator@example.test','Yalnız tek sahte operatör hesabı kabul edilir; gerçek hesap aracı değildir.')
    grants=sql(container,"SELECT row_to_json(t) FROM (SELECT grantee,table_name,column_name,privilege_type FROM information_schema.column_privileges WHERE table_schema='public' AND grantee='operator_identity_user' ORDER BY 1,2,3,4) t;")
    grants+='\n'+sql(container,"SELECT row_to_json(t) FROM (SELECT grantee,table_name,privilege_type,is_grantable FROM information_schema.table_privileges WHERE table_schema='public' AND grantee='operator_identity_user' ORDER BY 1,2,3,4) t;")
    roles=sql(container,"SELECT row_to_json(t) FROM (SELECT rolname,rolsuper,rolinherit,rolcreaterole,rolcreatedb,rolcanlogin,rolbypassrls FROM pg_roles WHERE rolname='operator_identity_user') t;")
    role=json.loads(roles)
    check(role['rolcanlogin'] and not any(role[k] for k in ('rolsuper','rolinherit','rolcreaterole','rolcreatedb','rolbypassrls')),'Geniş kimlik rolü taşınmaz.')
    check(sql(container,"SELECT count(*) FROM pg_auth_members WHERE member='operator_identity_user'::regrole;")=='0','Kimlik rol üyeliği geniş; otomatik taşınmaz.')
    marker=sql(container,"SELECT setting FROM pg_db_role_setting s CROSS JOIN LATERAL unnest(s.setconfig) setting WHERE setdatabase=(SELECT oid FROM pg_database WHERE datname=current_database()) AND setrole=0 ORDER BY setting;")
    sequences=sql(container,"SELECT row_to_json(t) FROM (SELECT * FROM pg_sequences WHERE schemaname='public' ORDER BY sequencename) t;")
    return dict(tables={e['table']:str(e['rows'])+':'+e['hash'] for e in entries},schema=hashlib.sha256(schema(container)).hexdigest(),
                grants=fixture.digest(grants),roles=roles,marker=marker,sequences=fixture.digest(sequences))


def backup(source, package, key):
    check(Path.cwd().resolve()==fixture.ROOT,'Depo kökü gerekli.')
    source,package,key=map(recovery.local_path,(source,package,key))
    m=json.loads((source/'manifest.json').read_text());instance=m['config']['instance']
    check(re.fullmatch(r'fr-synthetic-[a-z0-9][a-z0-9-]{0,24}',instance) and source==fixture.ROOT/'.local/synthetic'/instance and m['state']=='ready','Hazır sentetik kaynak gerekli.')
    check(m['names']==fixture.resource_names(instance) and fixture.resource_state(m['names'],m['owner'])==m['resources'],'Kaynak sahipliği/config değişti.')
    for name,h in m['files'].items():
        path=source/name;fixture.no_links(path)
        check(path.is_relative_to(source) and '..' not in Path(name).parts and fixture.file_hash(path)==h,'Kaynak özel dosyası değişti.')
    check(not package.parent.exists() and not key.parent.exists() and package.parent!=key.parent,'Yeni ve ayrı paket/anahtar dizinleri gerekli.')
    private=source/'private';op=instance+'-operator';db=instance+'-db-i'
    fixture.run(['docker','compose','-f',str(source/'compose.json'),'stop','operator'])
    before=snapshot(db)
    files={'identity.dump':recovery.binary(['docker','exec',db,'pg_dump','-U','postgres','-d','operator_identity','-Fc']),'identity.sql':schema(db)}
    for name in ('operator.env','ca.pem','db-i.crt','db-i.key','operator.pfx'):files[name]=(private/name).read_bytes()
    with tarfile.open(fileobj=io.BytesIO(recovery.binary(['docker','cp',op+':/app/keys/.','-']))) as stream:
        for member in stream:
            if member.isdir():continue
            name=Path(member.name).name
            check(member.isfile() and re.fullmatch(r'key-[0-9a-f-]+\.xml',name) and 'keys/'+name not in files,'Geçersiz/mükerrer DP anahtarı.')
            files['keys/'+name]=stream.extractfile(member).read()
    check(any(n.startswith('keys/') for n in files) and before==snapshot(db),'DP eksik veya yedek sırasında kimlik değişti.')
    nets={'network':[instance+'-operator-a',instance+'-operator-b']}
    metadata=dict(version=1,instance=instance,images=[m['config']['images'][1],m['config']['images'][2]],snapshot=before,
                  owner=m['owner'],networks=fixture.resource_state(nets,m['owner']),provision=fixture.source_hash(PROVISION))
    # Aynı dar okuyucu backup çıktısını da doğrular; yabancı config restore aşamasına bırakılmaz.
    plain=recovery.archive(files,metadata);read_archive(plain,instance+'-operator')
    with fixture.lock():
        check(not package.parent.exists() and not key.parent.exists(),'Paket/anahtar hedefi değişti.')
        fixture.private_directory(package.parent);fixture.private_directory(key.parent)
        with key.open('xb') as handle:handle.write(secrets.token_bytes(32))
        if sys.platform!='win32':key.chmod(0o600)
        with package.open('xb') as handle:handle.write(recovery.crypt(plain,key,'Encrypt'))
    return dict(state='backed_up',operator=instance+'-operator',plaintextArchiveOnDisk=False)


def read_archive(data, expected_operator):
    check(re.fullmatch(r'fr-synthetic-[a-z0-9][a-z0-9-]{0,24}-operator',expected_operator),'Açık sentetik operatör kimliği gerekli.')
    check(len(data)<=recovery.LIMIT,'Arşiv boyutu geçersiz.')
    allowed={'manifest.json','identity.dump','identity.sql','operator.env','ca.pem','db-i.crt','db-i.key','operator.pfx'}
    with zipfile.ZipFile(io.BytesIO(data)) as stream:
        entries=stream.infolist();names=[i.filename for i in entries]
        check(len(names)==len(set(names)) and len(names)<=100 and sum(i.file_size for i in entries)<=recovery.LIMIT,'Arşiv boyutu/mükerrer dosya reddi.')
        check(allowed<=set(names) and any(n.startswith('keys/') for n in names) and all(n in allowed or re.fullmatch(r'keys/key-[0-9a-f-]+\.xml',n) for n in names),'Eksik/yabancı paket yolu.')
        files={n:stream.read(n) for n in names}
    m=json.loads(files.pop('manifest.json'));instance=expected_operator.removesuffix('-operator')
    check(m['version']==1 and m['instance']==instance and m['provision']==fixture.source_hash(PROVISION),'Paket kimlik/sürüm/provisioning farklı.')
    check(m['files']=={n:hashlib.sha256(v).hexdigest() for n,v in files.items()} and files['identity.dump'].startswith(b'PGDMP'),'Bütünlük/dump geçersiz.')
    check(set(m['snapshot']['tables'])==TABLES and m['snapshot']['schema']==hashlib.sha256(files['identity.sql']).hexdigest(),'Şema/Identity kapsamı farklı.')
    check(isinstance(m['images'],list) and len(m['images'])==2 and all(isinstance(i,str) and re.fullmatch(r'sha256:[0-9a-f]{64}',i) for i in m['images']),'İki sabit imaj gerekli.')
    check(re.fullmatch(r'[0-9a-f]{48}',m['owner']) and set(m['networks'])=={instance+'-operator-a',instance+'-operator-b'},'Yabancı ağ kapsamı.')
    env=recovery.env_bytes(files['operator.env'])
    fixed=dict(ASPNETCORE_ENVIRONMENT='Production',ASPNETCORE_URLS='https://+:8443',AllowedHosts='localhost;127.0.0.1',
        ASPNETCORE_Kestrel__Certificates__Default__Path='/tls/operator.pfx',Operator__InstanceId=expected_operator,Operator__KeysDirectory='/app/keys')
    check(all(env.get(k)==v for k,v in fixed.items()) and re.fullmatch(r'[0-9a-f]{64}',env['ASPNETCORE_Kestrel__Certificates__Default__Password']),'Operatör config farklı.')
    identity='ConnectionStrings__OperatorIdentity'
    check(re.fullmatch(r'Host=db-i;Database=operator_identity;Username=operator_identity_user;Password=[0-9a-f]{64};SSL Mode=VerifyFull;Root Certificate=/tls/ca.pem;GSS Encryption Mode=Disable;Timeout=3',env[identity]),'Kimlik bağlantısı farklı.')
    expected=set(fixed)|{identity,'ASPNETCORE_Kestrel__Certificates__Default__Password'}
    for index,suffix in enumerate(('a','b')):
        prefix=f'SubscriptionTargets__{index}__';expected|={prefix+k for k in ('InstanceId','Name','ConnectionString')}
        check(env[prefix+'InstanceId']==instance+'-'+suffix and env[prefix+'Name']=='Sentetik '+suffix.upper()+' Salon','Firma izin listesi farklı.')
        check(re.fullmatch(r'Host=db-'+suffix+r';Database=firma_randevu;Username=subscription_operator;Password=[0-9a-f]{64};SSL Mode=VerifyFull;Root Certificate=/tls/ca.pem;GSS Encryption Mode=Disable;Timeout=3',env[prefix+'ConnectionString']),'Firma dar bağlantı sözleşmesi farklı.')
    check(set(env)==expected,'Bilinmeyen operatör config alanı.')
    return m,files,env


def read_package(package,key,expected_operator):
    package,key=map(recovery.local_path,(package,key));check(package.stat().st_size<=recovery.LIMIT+34,'Şifreli paket boyutu geçersiz.')
    return read_archive(recovery.crypt(package.read_bytes(),key,'Decrypt'),expected_operator)


def restore(package,key,expected_operator,target,port):
    check(Path.cwd().resolve()==fixture.ROOT and re.fullmatch(r'fr-oprestore-[a-z0-9][a-z0-9-]{0,24}',target),'Yeni açık fr-oprestore hedefi gerekli.')
    check(isinstance(port,int) and 1024<=port<=65535 and port!=8080,'Ana8080/yanlış port reddi.')
    m,files,env=read_package(package,key,expected_operator)
    root=recovery.local_path(fixture.ROOT/'.local/operator-recovery-targets'/target)
    names=dict(container=[target+'-db',target+'-operator'],volume=[target+'-data',target+'-keys'],network=[target+'-identity'])
    with fixture.lock():
        check(not root.exists(),'Mevcut hedefe restore yok.')
        inventory=fixture.inventory();check(all(not set(v)&inventory[k] for k,v in names.items()),'Yabancı/mevcut kaynak çakışması.')
        check(not fixture.run(['docker','container','ls','-a','--filter',f'label=com.docker.compose.project={target}','-q']),'Proje zaten kullanılıyor.')
        nets={'network':list(m['networks'])};check(fixture.resource_state(nets,m['owner'])==m['networks'],'Firma ağ sahipliği/config farklı.')
        for image in m['images']:
            check(json.loads(fixture.run(['docker','image','inspect',image]))[0]['Id']==image,'Yerel sabit imaj eksik.')
        with socket.socket() as sock:
            if sys.platform=='win32':sock.setsockopt(socket.SOL_SOCKET,socket.SO_EXCLUSIVEADDRUSE,1)
            sock.bind(('127.0.0.1',port))
        fixture.private_directory(root/'private');private=root/'private';owner=secrets.token_hex(24)
        # Windows metin yazımı PEM satır sonlarını değiştirmesin; şifreli PFX'i UID1654 okuyabilmeli.
        for name in ('ca.pem','db-i.crt','db-i.key','operator.pfx'):
            (private/name).write_bytes(files[name])
            if sys.platform!='win32':(private/name).chmod(0o600 if name=='db-i.key' else 0o644)
        fixture.environment(private/'operator.env',env)
        fixture.environment(private/'db.env',dict(POSTGRES_DB='operator_identity',POSTGRES_PASSWORD=secrets.token_hex(32)))
        def bind(name,destination):return dict(type='bind',source=str(private/name),target=destination,read_only=True)
        db=dict(image=m['images'][1],env_file=[str(private/'db.env')],networks={'identity':{'aliases':['db-i']}},
            volumes=['data:/var/lib/postgresql',bind('db-i.crt','/tls/db.crt'),bind('db-i.key','/tls/db.key'),bind('ca.pem','/tls/ca.pem')],
            command=['bash','-c','cp /tls/db.key /tmp/operator.key && chown postgres:postgres /tmp/operator.key && chmod 600 /tmp/operator.key && exec docker-entrypoint.sh postgres -c ssl=on -c ssl_cert_file=/tls/db.crt -c ssl_key_file=/tmp/operator.key'],
            healthcheck=dict(test=['CMD','pg_isready','-h','127.0.0.1','-U','postgres','-d','operator_identity'],interval='2s',timeout='3s',retries=40))
        op=dict(image=m['images'][0],env_file=[str(private/'operator.env')],networks=['identity','a','b'],ports=[f'127.0.0.1:{port}:8443'],
            volumes=['keys:/app/keys',bind('ca.pem','/tls/ca.pem'),bind('operator.pfx','/tls/operator.pfx')],read_only=True,tmpfs=['/tmp:size=64m,mode=1777'],cap_drop=['ALL'],security_opt=['no-new-privileges:true'],
            environment={'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command':'Warning'})
        for name,service in (('db',db),('operator',op)):
            service.update(container_name=target+'-'+name,labels={fixture.LABEL:owner},pull_policy='never',cpus=.5,mem_limit='512m',memswap_limit='512m',pids_limit=128,
                           logging=dict(driver='json-file',options={'max-size':'1m','max-file':'2'}))
        config=dict(name=target,services=dict(db=db,operator=op),volumes={v:dict(name=target+'-'+n,labels={fixture.LABEL:owner}) for v,n in (('data','data'),('keys','keys'))},
                    networks=dict(identity=dict(name=target+'-identity',labels={fixture.LABEL:owner}),a=dict(name=m['instance']+'-operator-a',external=True),b=dict(name=m['instance']+'-operator-b',external=True)))
        fixture.write_json(root/'compose.json',config)
        result=dict(state='preparing',operator=expected_operator,owner=owner,names=names)
        fixture.write_json(root/'manifest.json',result)
        compose=['docker','compose','-f',str(root/'compose.json')]
        fixture.run(compose+['up','-d','--wait','db']);container=target+'-db'
        check(sql(container,"SELECT count(*) FROM pg_tables WHERE schemaname='public';")=='0','Hedef kimlik DB boş değil.')
        password=env['ConnectionStrings__OperatorIdentity'].split('Password=',1)[1].split(';')[0]
        provision=f"\\set database_name operator_identity\n\\set instance_id {expected_operator}\n\\set identity_password {password}\n"+PROVISION.read_text(encoding='utf-8-sig')
        sql(container,provision)
        recovery.binary(['docker','exec','-i',container,'pg_restore','-U','postgres','-d','operator_identity','--single-transaction','--exit-on-error','--no-owner','--no-acl'],files['identity.dump'])
        sql(container,provision)
        recovery.binary(['docker','exec',container,'createdb','-U','postgres','operator_schema_reference'])
        sql(container,files['identity.sql'].decode(),'operator_schema_reference')
        normalized=dict(m['snapshot'],schema=hashlib.sha256(schema(container,'operator_schema_reference')).hexdigest())
        recovery.binary(['docker','exec',container,'dropdb','-U','postgres','operator_schema_reference'])
        check(snapshot(container)==normalized,'Identity tablo/DDL/sequence/rol/grant/marker farkı.')
        # Yedek sonrası iptaller bilinemez; hiçbir eski parola/MFA/oturum yeniden yetki kazanamaz.
        sql(container,f"SELECT set_config('recovery.expected_operator','{expected_operator}',false);\n"+INVALIDATE.read_text(encoding='utf-8-sig'))
        after=snapshot(container)
        changed=[n for n in sorted(TABLES) if after['tables'][n]!=normalized['tables'][n]]
        check('AspNetUsers' in changed and set(changed)<={'AspNetUsers','AspNetUserTokens'} and
            all(after[k]==normalized[k] for k in normalized if k!='tables'),'İptal yalnız kimlik doğrulama alanlarını değiştirmeli.')
        check(sql(container,'''SELECT count(*) FROM "AspNetUsers" WHERE "PasswordHash" IS NULL AND NOT "TwoFactorEnabled"
            AND "LockoutEnabled" AND "LockoutEnd">clock_timestamp();''')=='1' and
            sql(container,'SELECT count(*) FROM "AspNetUserTokens";')=='0','Kurtarılan hesap kapalı değil.')
        stream=io.BytesIO()
        with tarfile.open(fileobj=stream,mode='w') as output:
            directory=tarfile.TarInfo('.');directory.type=tarfile.DIRTYPE;directory.uid=directory.gid=1654;directory.mode=0o700;output.addfile(directory)
            for name,data in files.items():
                if name.startswith('keys/'):
                    item=tarfile.TarInfo(name[5:]);item.uid=item.gid=1654;item.mode=0o600;item.size=len(data);output.addfile(item,io.BytesIO(data))
        fixture.run(compose+['create','operator'])
        recovery.binary(['docker','cp','-a','-',target+'-operator:/app/keys/'],stream.getvalue())
        # shortcut: Güncel yetki kanıtı ve yeniden kayıt ayrı kapıdır; bu araç hesabı açmaz.
        result.update(state='restored_closed',rawRestoreEqual=True,operatorStarted=False,plaintextArchiveOnDisk=False,
                      accessInvalidated=True,reviewRequired=True,securityChangedTables=changed,accessPolicy=fixture.source_hash(INVALIDATE))
        fixture.write_json(root/'manifest.json',result)
        return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='operation',required=True)
    b=sub.add_parser('backup');b.add_argument('--source',required=True)
    r=sub.add_parser('restore');r.add_argument('--expected-operator',required=True);r.add_argument('--target',required=True);r.add_argument('--port',type=int,required=True)
    for command in (b,r):command.add_argument('--package',required=True);command.add_argument('--key',required=True)
    args=vars(parser.parse_args());operation=args.pop('operation')
    try:print(json.dumps((backup if operation=='backup' else restore)(**args)))
    except (RuntimeError,ValueError,TypeError,KeyError,OSError,zipfile.BadZipFile,tarfile.TarError):
        print('Sentetik operatör kurtarma reddedildi/başarısız; kaynaklar korunuyor, sır içerebilen ayrıntı gizlendi.',file=sys.stderr);sys.exit(1)
