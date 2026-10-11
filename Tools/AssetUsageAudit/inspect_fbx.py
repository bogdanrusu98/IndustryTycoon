from pathlib import Path
import struct,hashlib,json,io
from PIL import Image,ImageDraw
embedded=[]
out=Path("Logs/AssetUsageAudit/Sources/Embedded");out.mkdir(exist_ok=True)
rows=[]
def props(b,p,n):
 out=[]
 for _ in range(n):
  typ=chr(b[p]);p+=1
  if typ in 'SR':
   ln=struct.unpack_from('<I',b,p)[0];p+=4;v=b[p:p+ln];p+=ln
   if typ=='R' and (v.startswith(b'\x89PNG') or v.startswith(b'\xff\xd8')):
    im=Image.open(io.BytesIO(v));im.load();idx=len(embedded);im.save(globals()['out']/f'{idx:02d}.png');rgb=im.convert('RGB');metadata={'bytes':ln,'sha256':hashlib.sha256(v).hexdigest(),'pixelsha256':hashlib.sha256(rgb.tobytes()).hexdigest(),'dimensions':im.size,'image':f'Embedded/{idx:02d}.png','source':str(f)};embedded.append(metadata);out.append(metadata)
   else:out.append(v.decode('utf-8','replace').replace('\x00','\\0') if typ=='S' else {'bytes':ln,'sha256':hashlib.sha256(v).hexdigest()})
  elif typ in 'YCFDIL':
   fm={'Y':'h','C':'?','F':'f','D':'d','I':'i','L':'q'}[typ];v=struct.unpack_from('<'+fm,b,p)[0];p+=struct.calcsize(fm);out.append(v)
  elif typ in 'fdlibc':
   n,enc,ln=struct.unpack_from('<III',b,p);p+=12+ln;out.append({'array':typ,'count':n})
  else:raise ValueError(typ)
 return out
for f in sorted(Path('Meshy Import').rglob('*.fbx'),key=lambda x:str(x).lower()):
 b=f.read_bytes();version=struct.unpack_from('<I',b,23)[0];fmt='<QQQB' if version>=7500 else '<IIIB';hs=struct.calcsize(fmt)
 def walk(start,end,path=''):
  p=start
  while p+hs<=end:
   stop,n,plen,nlen=struct.unpack_from(fmt,b,p)
   if stop==0:break
   name=b[p+hs:p+hs+nlen].decode('utf-8','replace');pp=p+hs+nlen;vals=props(b,pp,n);full=path+'/'+name
   if any(part in full.split('/') for part in ('Material','Texture','Video')):rows.append({'source':str(f),'node':full,'properties':vals})
   walk(pp+plen,stop,full);p=stop
 walk(27,len(b))
Path('Logs/AssetUsageAudit/Sources/fbx-material-texture-nodes.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
from collections import Counter
print(Counter(r['node'] for r in rows if r['node'].count('/')==2))
print('embedded contents',sum(r['node'].endswith('/Content') for r in rows))

external={}
for png in Path('Meshy Import').rglob('*.png'):
 im=Image.open(png).convert('RGB');h=hashlib.sha256(im.tobytes()).hexdigest();external.setdefault(h,[]).append(str(png))
for e in embedded:e['pixelIdenticalExternal']=external.get(e['pixelsha256'],[])
Path('Logs/AssetUsageAudit/Sources/embedded-textures.json').write_text(json.dumps(embedded,indent=2),encoding='utf-8')
print('Embedded pixel identical:',sum(bool(e['pixelIdenticalExternal']) for e in embedded),'/',len(embedded))
for page in range((len(embedded)+29)//30):
 sheet=Image.new('RGB',(1500,1800),'#eee9dc');d=ImageDraw.Draw(sheet)
 for j,e in enumerate(embedded[page*30:(page+1)*30]):
  im=Image.open(Path('Logs/AssetUsageAudit/Sources')/e['image']);im.thumbnail((285,245));x=j%5*300;y=j//5*300;sheet.paste(im,(x,y));d.text((x+4,y+250),e['image']+' '+str(e['dimensions']),fill='black');d.text((x+4,y+267),'PIXEL DUPLICATE' if e['pixelIdenticalExternal'] else 'EMBEDDED ALTERNATE',fill='black')
 sheet.save(Path('Logs/AssetUsageAudit/Sources')/f'embedded-{page}.jpg',quality=90)

from pathlib import Path
from PIL import Image,ImageChops,ImageStat
import json
base=Path('Logs/AssetUsageAudit/Sources');items=json.loads((base/'embedded-textures.json').read_text(encoding='utf-8'))
for e in items:
 im=Image.open(base/e['image']).convert('RGB').resize((128,128));candidates=[]
 for p in Path(e['source']).parent.glob('*.png'):
  other=Image.open(p).convert('RGB').resize((128,128));error=sum(ImageStat.Stat(ImageChops.difference(im,other)).mean)/3;candidates.append((error,str(p)))
 e['closestExternalMae128'],e['closestExternal']=min(candidates)
 e['decision']='DUPLICATE / UNUSED embedded representation: external PNG of same atlas selected; production materials use explicit external references' if e['closestExternalMae128']<8 else 'REVIEW'
(base/'embedded-textures.json').write_text(json.dumps(items,indent=2),encoding='utf-8')
print('maximum mean absolute channel error',max(e['closestExternalMae128'] for e in items));print('unexplained',sum(e['decision']=='REVIEW' for e in items))
