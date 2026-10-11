from pathlib import Path
from PIL import Image,ImageDraw
import hashlib,csv
root=Path('.')
out=root/'Logs/AssetUsageAudit/Sources';out.mkdir(parents=True,exist_ok=True)
prod={}
for p in (root/'Assets/Art/Production').rglob('*'):
 if p.is_file() and p.suffix.lower() in ('.fbx','.png'):
  prod.setdefault(hashlib.sha256(p.read_bytes()).hexdigest().upper(),[]).append(p.as_posix())
files=sorted((root/'Meshy Import').rglob('*'),key=lambda p:str(p).lower())
rows=[];pngs=[]
for p in files:
 if not p.is_file(): continue
 h=hashlib.sha256(p.read_bytes()).hexdigest().upper()
 matches=';'.join(prod.get(h,[]))
 if p.suffix.lower()=='.png':
  im=Image.open(p);im.load();stats=f'{im.width}x{im.height} {im.mode} extrema={im.getextrema()}'
  reason='USED: byte-matched production texture' if matches else ('DUPLICATE export maps / redundant rock alternative' if '1f9936f8' in str(p) or '29d69076' in str(p) else 'UNUSED auxiliary map: opaque mobile URP uses base color/normal with uniform low smoothness; metallic/roughness/emission not bound')
  pngs.append((p,im.copy(),matches,reason));rows.append([str(p),h,stats,matches,reason])
 else: rows.append([str(p),h,str(p.stat().st_size)+' bytes',matches,'MODEL: inspect geometry and runtime mapping'])
with (out/'files.tsv').open('w',encoding='utf-8',newline='') as f:csv.writer(f,delimiter='\t').writerows([['source','sha256','decoded','production match','decision']]+rows)
for page in range((len(pngs)+29)//30):
 sheet=Image.new('RGB',(1500,1800),'#eee9dc');d=ImageDraw.Draw(sheet)
 for j,(p,im,matches,reason) in enumerate(pngs[page*30:(page+1)*30]):
  x=j%5*300;y=j//5*300;im.thumbnail((282,235));sheet.paste(im,(x+(300-im.width)//2,y))
  d.text((x+6,y+238),f'{page*30+j:02d} '+p.name[:37],fill='black');d.text((x+6,y+254),str(p.parent.name)[:38],fill='black');d.text((x+6,y+270),'PRODUCTION' if matches else 'AUXILIARY / ALTERNATE',fill='black')
 sheet.save(out/f'textures-{page}.jpg',quality=90)
print(f'{len(rows)} files; {len(pngs)} images decoded; {sum(bool(r[3]) for r in rows)} source files byte-match production')
