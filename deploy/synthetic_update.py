"""Explicit image-only upgrade/rollback of synthetic firm A; never migrate or restore a DB."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import sys
import tarfile
import time

import initialize_synthetic as fixture
import synthetic_recovery as recovery

# P07-05 enforces overdue subscription access; older images are not compatible rollback targets.
MINIMUM_REVISION = '3ef17303624a84ebc922b33b9ddd981dd62f5699'


def migrations(revision):
    fixture.require(re.fullmatch(r'[0-9a-f]{40}',revision), 'Tam kaynak commit kimliği gerekli.')
    names=fixture.run(['git','ls-tree','-r','--name-only',revision,'--','src/Server/Infrastructure/Migrations']).splitlines()
    return sorted(Path(n).stem for n in names if re.fullmatch(r'\d{14}_[A-Za-z0-9]+\.cs',Path(n).name))


def release(image,revision):
    fixture.require(re.fullmatch(r'sha256:[0-9a-f]{64}',image), 'Etiket yerine tam yerel imaj kimliği gerekli.')
    fixture.require(re.fullmatch(r'[0-9a-f]{40}',revision), 'Tam kaynak commit kimliği gerekli.')
    fixture.run(['git','merge-base','--is-ancestor',MINIMUM_REVISION,revision])
    item=json.loads(fixture.run(['docker','image','inspect',image]))[0]
    fixture.require(item['Id']==image and (item['Config'].get('Labels') or {}).get('org.opencontainers.image.revision')==revision, 'İmaj kaynak etiketi eşleşmiyor.')
    fixture.require(item['Config']['User'] not in ('','0','root') and item['Config']['Entrypoint']==['dotnet','Server.dll'], 'Uygulama imajı/yetkisi uyumsuz.')
    result=dict(image=image,revision=revision,migrations=migrations(revision))
    fixture.require(result['migrations'], 'Migration envanteri boş.')
    return result


def keys(container):
    raw=recovery.binary(['docker','cp',container+':/app/keys/.','-'])
    result={}
    with tarfile.open(fileobj=io.BytesIO(raw)) as stream:
        for member in stream:
            if member.isdir():continue
            fixture.require(member.isfile() and re.fullmatch(r'key-[0-9a-f-]+\.xml',Path(member.name).name), 'Beklenmeyen anahtar dosyası.')
            result[Path(member.name).name]=hashlib.sha256(stream.extractfile(member).read()).hexdigest()
    fixture.require(result,'DP anahtarları boş.')
    return result


def rollback_allowed(history,current,target):
    successful=[step for step in history if step['outcome']=='success']
    return bool(successful and successful[-1]['mode']=='upgrade' and
        successful[-1]['requested']==current and successful[-1]['previous']==target)


def switch(instance,firm,expected_image,image,mode,catalog,package,key):
    fixture.require(Path.cwd().resolve()==fixture.ROOT and re.fullmatch(r'fr-synthetic-[a-z0-9][a-z0-9-]{0,24}',instance), 'Depo kökü ve sentetik kurulum gerekli.')
    fixture.require(firm==instance+'-a' and mode in ('upgrade','rollback'), 'Yalnız açık A firma hedefi/işlem modu kabul edilir.')
    root=recovery.local_path(fixture.ROOT/'.local/synthetic'/instance)
    catalog=recovery.local_path(catalog);package=recovery.local_path(package);key=recovery.local_path(key)
    with fixture.lock():
        manifest=json.loads((root/'manifest.json').read_text())
        fixture.require(manifest['state']=='ready' and manifest['config']['instance']==instance and manifest['names']==fixture.resource_names(instance), 'Hazır kaynak manifesti eşleşmiyor.')
        for name,expected in manifest['config']['source'].items():
            path=fixture.ROOT/name
            fixture.require(path.is_relative_to(fixture.ROOT) and '..' not in Path(name).parts,'Geçersiz kaynak referansı.')
            fixture.no_links(path);fixture.require(fixture.source_hash(path)==expected,'Kurulum/migration kaynağı değişmiş.')
        journal=root/'update.json'
        fixture.no_links(journal)
        state=json.loads(journal.read_text()) if journal.exists() else dict(status='ready',image=manifest['config']['images'][0],
            resources=manifest['resources'],composeHash=manifest['files']['compose.json'],catalogHash=fixture.file_hash(catalog),history=[])
        fixture.require(state['status']=='ready' and state['image']==expected_image, 'İşlem yarım veya beklenen imaj farklı; otomatik onarım yok.')
        fixture.require(state['catalogHash']==fixture.file_hash(catalog), 'Onaylı sürüm listesi değişmiş.')
        fixture.require(fixture.resource_state(manifest['names'],manifest['owner'])==state['resources'], 'Kurulum kaynakları/config değişmiş.')
        for name,expected in manifest['files'].items():
            path=root/name
            fixture.require(path.is_relative_to(root) and '..' not in Path(name).parts, 'Geçersiz kurulum yolu.')
            fixture.no_links(path)
            fixture.require(fixture.file_hash(path)==(state['composeHash'] if name=='compose.json' else expected), 'Kurulum dosyası değişmiş.')
        approved=json.loads(catalog.read_text())
        fixture.require(isinstance(approved,list) and len(approved)<=4 and len({r['image'] for r in approved})==len(approved), 'Sürüm listesi geçersiz.')
        releases={r['image']:r for r in approved}
        fixture.require(image in releases and expected_image in releases, 'İmaj açık izinli listede yok.')
        for requested in (releases[image],releases[expected_image]):
            fixture.require(release(requested['image'],requested['revision'])==requested, 'Sürüm/migration envanteri farklı.')
        current_migrations=recovery.sql(instance+'-db-a','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";').splitlines()
        fixture.require(releases[image]['migrations']==releases[expected_image]['migrations']==current_migrations, 'Şema geçişi gerekli; ayrı migration/uyum kabulü olmadan imaj değiştirilmez.')
        backup,files,env=recovery.read_package(package,key,firm)
        actual_env=recovery.env_bytes((root/'private/app-a.env').read_bytes())
        fixture.require(actual_env==env and all(actual_env.get(n,'false')=='false' for n in
            ('BookingVerification__Enabled','BookingSms__DispatchEnabled','AppointmentReminders__Enabled','Netgsm__Enabled','PublicBooking__AccessEnabled')), 'Firma/sır/gönderim config eşleşmesi yanlış.')
        before=recovery.snapshot(instance+'-db-a')
        # Bu dilim migration çalıştırmaz; veri değişebilen rollback'te de şema/yetki/anahtar aynı kalır.
        fixture.require(all(before[k]==backup['snapshot'][k] for k in before if k!='tables'), 'Yedek ile canlı şema/yetki/marker farklı.')
        before_keys=keys(instance+'-app-a')
        fixture.require(before_keys=={Path(n).name:hashlib.sha256(v).hexdigest() for n,v in files.items() if n.startswith('keys/')}, 'Yedek DP anahtarları farklı.')
        if image==expected_image:
            return dict(state='verified',image=image,restarted=False)
        if mode=='upgrade':
            fixture.require(backup['images'][0]==expected_image and before==backup['snapshot'], 'Yükseltmeden önce güncel mevcut sürüm yedeği gerekli.')
        else:
            fixture.require(rollback_allowed(state['history'],expected_image,image), 'Rollback yalnız son başarılı upgrade öncesindeki imaja yapılabilir.')
        compose=root/'compose.json';original=compose.read_bytes();config=json.loads(original)
        fixture.require(config['services']['app-a']['image']==expected_image,'Compose imajı farklı.')
        command=['docker','compose','-f',str(compose)]
        unaffected={n:h for n,h in state['resources'].items() if n!=instance+'-app-a'}
        protected=[n for n in manifest['names']['container'] if n!=instance+'-app-a']
        starts={v['Name']:v['State']['StartedAt'] for v in json.loads(fixture.run(['docker','inspect',*protected]))}
        def unchanged():
            current=fixture.resource_state(manifest['names'],manifest['owner'])
            fixture.require(all(current[n]==h for n,h in unaffected.items()), 'A dışındaki kaynak değişti.')
            fixture.require({v['Name']:v['State']['StartedAt'] for v in json.loads(fixture.run(['docker','inspect',*protected]))}==starts, 'A dışındaki konteyner yeniden başladı.')
            fixture.require(recovery.snapshot(instance+'-db-a')==before and keys(instance+'-app-a')==before_keys, 'DB/veri/yetki/anahtar değişti.')
            return current
        started=time.monotonic();attempt=dict(mode=mode,previous=expected_image,requested=image,startedUtc=time.time())
        state.update(status='changing',requestedImage=image,previousImage=expected_image);fixture.write_json(journal,state)
        try:
            fixture.run(command+['stop','app-a'])
            fixture.require(recovery.snapshot(instance+'-db-a')==before,'Yazıcı dururken veri değişti; otomatik geçiş yok.')
            config['services']['app-a']['image']=image;fixture.write_json(compose,config)
            fixture.run(command+['up','-d','--no-deps','--no-build','--pull','never','app-a'])
            recovery.ready(manifest['config']['ports'][0])
            resources=unchanged()
        except RuntimeError:
            state['status']='review';fixture.write_json(journal,state)
            try:
                fixture.run(command+['stop','app-a'])
                fixture.require(recovery.snapshot(instance+'-db-a')==before and keys(instance+'-app-a')==before_keys,'Aday veri değiştirdi; otomatik rollback güvenli değil.')
                compose.write_bytes(original)
                fixture.run(command+['up','-d','--no-deps','--no-build','--pull','never','app-a'])
                recovery.ready(manifest['config']['ports'][0]);resources=unchanged()
                attempt.update(outcome='failed_rolled_back',seconds=round(time.monotonic()-started,3))
                state.update(status='ready',image=expected_image,resources=resources,composeHash=fixture.file_hash(compose),history=state['history']+[attempt])
                fixture.write_json(journal,state)
            except RuntimeError as error:
                raise RuntimeError('Geçiş/geri dönüş durdu; review kaydı ve veri korunuyor. Otomatik DB restore/Down yok.') from error
            raise RuntimeError('Aday açılamadı; veri/anahtar korunarak önceki imaja dönüldü. İstenen geçiş başarısızdır.')
        attempt.update(outcome='success',seconds=round(time.monotonic()-started,3))
        state.update(status='ready',image=image,resources=resources,composeHash=fixture.file_hash(compose),history=state['history']+[attempt])
        fixture.write_json(journal,state)
        return dict(state='changed',image=image,mode=mode,seconds=attempt['seconds'],dataAndKeysPreserved=True,otherResourcesPreserved=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('instance','firm','expected-image','image','catalog','package','key'):parser.add_argument('--'+name,required=True)
    parser.add_argument('--mode',choices=['upgrade','rollback'],required=True)
    try:print(json.dumps(switch(**vars(parser.parse_args()))))
    except (RuntimeError,ValueError,TypeError,KeyError,OSError,tarfile.TarError) as error:
        print(str(error) if isinstance(error,RuntimeError) else 'Sentetik imaj geçişi başarısız; update.json/sağlık/veri durumunu inceleyin. Sır içeren ayrıntılar gizlendi.',file=sys.stderr);sys.exit(1)
