"""Release stopped, owned CI fixture containers/networks between acceptances; retain every volume/file."""
import json
import os
from pathlib import Path
import re
import sys

sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'deploy'))
import initialize_synthetic as fixture


def retire():
    fixture.require(os.environ.get('GITHUB_ACTIONS')=='true' and Path.cwd().resolve()==fixture.ROOT,'Yalnız GitHub CI depo kökü kabul edilir.')
    current=fixture.inventory();count=0
    # Dış firma ağına bağlı hedefler önce kaldırılır; source down ağı kullanımda bulmamalı.
    for parent in ('operator-recovery-targets','recovery-targets','synthetic'):
        folder=fixture.ROOT/'.local'/parent
        if not folder.exists():continue
        fixture.no_links(folder)
        for root in sorted(folder.iterdir()):
            fixture.no_links(root)
            if not root.is_dir() or not re.fullmatch(r'fr-(?:synthetic|restore|oprestore)-(?:[a-z0-9-]*-)?ci(?:-[a-z0-9-]+)?',root.name):continue
            m=json.loads((root/'manifest.json').read_text());config=json.loads((root/'compose.json').read_text())
            files={str(p.relative_to(root)):fixture.file_hash(p) for p in root.rglob('*') if p.is_file()}
            fixture.require(config['name']==root.name and re.fullmatch(r'[0-9a-f]{48}',m['owner']),'CI proje/sahiplik kaydı farklı.')
            fixture.require(set(m['names'])=={'container','volume','network'},'Kaynak envanteri farklı.')
            for kind,names in m['names'].items():
                fixture.require(len(names)<=100 and all(re.fullmatch(re.escape(root.name)+r'-[a-z0-9_-]+',n) for n in names),'Yabancı CI kaynak adı.')
                for name in set(names)&current[kind]:
                    item=json.loads(fixture.run(['docker',kind,'inspect',name]))[0]
                    labels=item['Config']['Labels'] if kind=='container' else item['Labels']
                    fixture.require(labels.get(fixture.LABEL)==m['owner'],'CI kaynağının sahibi farklı.')
                    if kind=='container':fixture.require(not item['State']['Running'],'Çalışan CI kaynağı emekliye ayrılmaz.')
            project=set(fixture.run(['docker','container','ls','-a','--filter','label=com.docker.compose.project='+root.name,'--format','{{.Names}}']).splitlines())
            fixture.require(project<=set(m['names']['container']),'Proje içinde kayıtsız konteyner var.')
            for network in config['networks'].values():
                fixture.require(network.get('external') is True or network['name'] in m['names']['network'],'Yabancı Compose ağı.')
            fixture.run(['docker','compose','-f',str(root/'compose.json'),'down'])
            fixture.require(files=={str(p.relative_to(root)):fixture.file_hash(p) for p in root.rglob('*') if p.is_file()},'CI özel dosyası değişti.')
            for name in set(m['names']['volume'])&current['volume']:
                item=json.loads(fixture.run(['docker','volume','inspect',name]))[0]
                fixture.require(item['Labels'].get(fixture.LABEL)==m['owner'],'CI volume korunmadı.')
            count+=1
    print(f'CI: {count} kapalı sahipli fixture emekliye ayrıldı; volume ve özel dosyalar korunuyor.')


if __name__=='__main__':
    try:retire()
    except (RuntimeError,ValueError,TypeError,KeyError,OSError):
        print('CI fixture emekliliği reddedildi; kaynaklar korunuyor, özel ayrıntılar gizlendi.',file=sys.stderr);sys.exit(1)
