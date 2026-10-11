"""Copy the audited character, Wood Log and Plank sources; pack URP metallic/smoothness.

Run from the repository root with Python + Pillow. Never modifies Meshy Import.
Unity's AnimatedProductionIntegration.Integrate imports and wires these files.
"""
from pathlib import Path
import hashlib
import json
import shutil
from PIL import Image, ImageOps

source = Path('Meshy Import')
production = Path('Assets/Art/Production')
manifest = []

def one(paths):
    paths = list(paths)
    if len(paths) != 1:
        raise ValueError(f'Expected exactly one audited source, found {paths}')
    return paths[0]

def copy(src, dest):
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, dest)
    manifest.append({'source': str(src), 'production': str(dest),
                     'sha256': hashlib.sha256(src.read_bytes()).hexdigest()})

def textures(folder, prefix, dest, family):
    base = one(folder.glob(prefix + '.png'))
    metallic = one(folder.glob(prefix + '_metallic.png'))
    roughness = one(folder.glob(prefix + '_roughness.png'))
    copy(base, dest / (family + '_BaseColor.png'))
    metal = Image.open(metallic).convert('L')
    rough = Image.open(roughness).convert('L')
    if metal.size != rough.size:
        raise ValueError('Metallic/roughness dimensions differ')
    packed = dest / (family + '_MetallicSmoothness.png')
    Image.merge('RGBA', (metal, metal, metal, ImageOps.invert(rough))).save(packed)
    manifest.append({'sources': [str(metallic), str(roughness)], 'production': str(packed),
                     'packing': 'RGB=metallic; A=255-roughness; linear import', 'source_size': metal.size})

for directory, family in [('character', 'Player'), ('worker', 'LumberWorker')]:
    folder = one(p for p in (source / directory).iterdir() if p.is_dir())
    dest = production / 'Characters' / family
    for original, name in [('Idle_3', 'Idle'), ('Walking', 'Walk'), ('Running', 'Run')]:
        copy(one(folder.glob('*_Animation_' + original + '_withSkin.fbx')), dest / (name + '.fbx'))
    textures(folder, '*texture_0', dest, family)

plank = one(source.glob('Meshy_AI_Mobile_Wooden_Plank_L_1011082921_texture_fbx/**/*.fbx'))
dest = production / 'Resources' / 'Plank'
copy(plank, dest / 'Plank.fbx')
textures(plank.parent, plank.stem, dest, 'Plank')
log = one(source.glob('Meshy_AI_Mobile_Game_Wood_Log_1011082912_texture_fbx/**/*.fbx'))
dest = production / 'Resources' / 'WoodLog'
copy(log, dest / 'WoodLog.fbx')
textures(log.parent, log.stem, dest, 'WoodLog')
out = Path('Documentation/AnimatedAssetAudit')
out.mkdir(parents=True, exist_ok=True)
(out / 'production-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
