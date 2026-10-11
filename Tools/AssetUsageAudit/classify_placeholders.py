from pathlib import Path
import csv,collections
runtime=Path('Logs/AssetUsageAudit/renderers-runtime.txt').read_text(encoding='utf-8-sig').splitlines();static=Path('Logs/AssetUsageAudit/renderers-after.txt').read_text(encoding='utf-8-sig').splitlines();paths={x.split('|')[0] for x in static};src=static+[x for x in runtime if x.split('|')[0] not in paths];states={x.split('|')[0]:'active=True' in x for x in runtime};rows=[]
for line in src:
 if 'mesh=Library' not in line or 'enabled=False' in line:continue
 path=line.split('|')[0];leaf=path.rsplit('/',1)[-1];active=states.get(path,'active=True' in line)
 cat=None;why=None
 if 'Crafted Worker VisualRoot' in path:cat='Character';why='21-source census: no human, rig or character parts; authored helmet/uniform assembly retained.'
 elif 'Wood Visual' in path or '/Visual/Bark' in path or '/Visual/Cut End' in path:cat='Wood';why='21-source census: no log export or log mesh child; crate is a different resource.'
 elif 'Plank Visual' in path:cat='Plank';why='21-source census: no plank export/child; saw table is one complete machine mesh.'
 elif 'Machine Part Visual' in path:cat='Machine part';why='21-source census: no recipe-output machine part; source 04 is the mounted boring head, not the crafted output.'
 elif leaf in ('Bills','Band') or ('Cash' in path and ('Token' in path or 'Visual' in path)):cat='Cash';why='21-source census: no currency mesh; cash feedback must retain its identity.'
 elif '/Auto Feeder Visual/' in path:cat='Auto feeder';why='No standalone spanning conveyor or roller export; source 20 is an inseparable roofed saw station, unsuitable for this animated transfer route.'
 elif leaf in ('Drill Base','Drill Tower','Drill Arm'):cat='Drill structure';why='Only source 04 drill bit exists; no mast, chassis or arm export/child.'
 elif path.startswith('Wood Stockpile/') and leaf in ('Stockpile Platform','Left Rail','Right Rail'):cat='Wood stockpile rack';why='No log storage rack export; ore hopper and finished-goods crate have different gameplay identities.'
 elif leaf.startswith('Pickaxe'):cat='Pickaxe';why='No full-size mining pickaxe export; tiny tools fused inside source 18 toolbox cannot serve this prop.'
 elif leaf.startswith('Pine '):cat='Trees';why='No tree/trunk/foliage export in the 21-model census.'
 else:cat='DESIGN / FEEDBACK';why='Purpose-built floor, route, interaction marker, label backing, indicator, progress bar or process effect; not a replacement for an exported entity.'
 rows.append([path,active,cat,why])
with Path('Logs/AssetUsageAudit/remaining-primitives.tsv').open('w',encoding='utf-8',newline='') as f:csv.writer(f,delimiter='\t').writerows([['hierarchy','activeInDevelopedFixture','category','proof/reason']]+rows)
print(collections.Counter(r[2] for r in rows));print('unclassified potential:',sorted(set(r[0] for r in rows if r[2]=='DESIGN / FEEDBACK' and not any(x in r[0] for x in ['Area','Marker','Progress','Backdrop','Upgrade','Avenue','footprint','entry stripe','Ground','meadow','Boundary','Floor','Furnace Heat','Indicator','Platform','Input','Output']))))
