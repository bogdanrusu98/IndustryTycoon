"""Read-only FBX census, including compressed arrays and every mesh/rig/clip node."""
from pathlib import Path
import struct, zlib, json, hashlib

def inspect(path):
    data = path.read_bytes()
    version = struct.unpack_from('<I', data, 23)[0]
    fmt = '<QQQB' if version >= 7500 else '<IIIB'
    hs = struct.calcsize(fmt)
    nodes = []
    def walk(start, end, parent=''):
        p = start
        while p + hs <= end:
            stop, count, plen, nlen = struct.unpack_from(fmt, data, p)
            if not stop: break
            name = data[p+hs:p+hs+nlen].decode()
            q = p + hs + nlen
            child = q + plen
            values = []
            for _ in range(count):
                typ = chr(data[q]); q += 1
                if typ in 'SR':
                    size = struct.unpack_from('<I', data, q)[0]; q += 4
                    value = data[q:q+size]; q += size
                    value = value.decode('utf-8', 'replace').replace('\x00', '|') if typ == 'S' else {'bytes': size}
                elif typ in 'YCFDIL':
                    f = {'Y':'h','C':'?','F':'f','D':'d','I':'i','L':'q'}[typ]
                    value = struct.unpack_from('<'+f, data, q)[0]; q += struct.calcsize(f)
                else:
                    size, enc, length = struct.unpack_from('<III', data, q); q += 12
                    raw = data[q:q+length]; q += length
                    if enc: raw = zlib.decompress(raw)
                    f = {'f':'f','d':'d','i':'i','l':'q','b':'b','c':'b'}[typ]
                    arr = struct.unpack('<'+str(size)+f, raw)
                    value = {'count':size}
                    if name == 'Vertices':
                        value['min'] = [min(arr[i::3]) for i in range(3)]
                        value['max'] = [max(arr[i::3]) for i in range(3)]
                    if name == 'KeyValueFloat' and arr:
                        value.update(min=min(arr), max=max(arr))
                values.append(value)
            full = parent+'/'+name
            if name in ('Model','Geometry','Deformer','Material','Texture','Video','AnimationStack','AnimationLayer','AnimationCurveNode','Vertices','KeyValueFloat') or (name == 'P' and values and values[0] in ('Lcl Translation','Lcl Rotation','Lcl Scaling','UpAxis','FrontAxis','UnitScaleFactor')):
                nodes.append({'node':full, 'values':values})
            walk(child, stop, full); p = stop
    walk(27, len(data))
    return {'source':str(path), 'sha256':hashlib.sha256(data).hexdigest(), 'nodes':nodes}

if __name__ == '__main__':
    out = Path('Documentation/AnimatedAssetAudit'); out.mkdir(parents=True, exist_ok=True)
    results = [inspect(p) for p in sorted(Path('Meshy Import').rglob('*.fbx'))]
    (out/'source-census.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    for r in results:
        names = [n['values'][1:] for n in r['nodes'] if n['node'] in ('/Objects/Geometry','/Objects/AnimationStack')]
        print(r['source'], names)
