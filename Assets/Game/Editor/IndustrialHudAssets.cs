using System.IO;
using System.Linq;
using IndustryTycoon.Feedback;
using UnityEditor;
using UnityEngine;

namespace IndustryTycoon.Editor
{
    public static class IndustrialHudAssets
    {
        private const string Folder="Assets/Game/UI/Art/IndustrialHud";
        public static Sprite Emblem(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Folder+"/"+name+".png");
        public static Sprite Round => AssetDatabase.LoadAssetAtPath<Sprite>(Folder+"/Rounded.png");
        public static Sprite[] Resources => Enumerable.Range(0,7).Select(i=>AssetDatabase.LoadAssetAtPath<Sprite>(Folder+"/Resource"+i+".png")).ToArray();
        public static void Ensure(bool force = false)
        {
            foreach(string name in new[]{"Coin","Star","Flag"}) Import(Folder+"/"+name+".png",Vector4.zero);
            if(!force && Round!=null && Resources.All(s=>s!=null))return;
            Directory.CreateDirectory(Folder);
            Texture2D rounded=new Texture2D(128,128,TextureFormat.RGBA32,false);
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                Vector2 q=new Vector2(Mathf.Abs(x-63.5f)-35.5f,Mathf.Abs(y-63.5f)-35.5f);
                float distance=new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude-28;
                float shade=Mathf.Lerp(.87f,1f,y/127f);
                rounded.SetPixel(x,y,new Color(shade,shade,shade,Mathf.Clamp01(.5f-distance)));
            }
            File.WriteAllBytes(Folder+"/Rounded.png",rounded.EncodeToPNG());
            Object.DestroyImmediate(rounded);
            Import(Folder+"/Rounded.png",new Vector4(32,32,32,32));
            ResourceVisual visual=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/WoodCarryVisual.prefab").GetComponent<ResourceVisual>();
            GameObject[] roots={visual.WoodRoot,visual.PlankRoot,visual.CrateRoot,visual.IronOreRoot,visual.IronBarRoot,visual.ToolRoot,visual.MachinePartRoot};
            for(int i=0;i<roots.Length;i++)Bake(roots[i],Folder+"/Resource"+i+".png");
        }
        private static void Bake(GameObject source,string path)
        {
            var preview=new PreviewRenderUtility();
            try
            {
                GameObject instance=new GameObject("Resource icon studio");
                GameObject model=Object.Instantiate(source,instance.transform);
                model.SetActive(true);
                if(path.EndsWith("Resource0.png") || path.EndsWith("Resource1.png"))
                {
                    // A bundle of the actual carried model reads at phone scale.
                    model.transform.localPosition=new Vector3(-.12f,-.10f,0);
                    var second=Object.Instantiate(model,instance.transform);
                    second.transform.localPosition=new Vector3(.14f,-.10f,.06f);
                    var third=Object.Instantiate(model,instance.transform);
                    third.transform.localPosition=new Vector3(0,.13f,.03f);
                }
                instance.SetActive(true);
                preview.AddSingleGO(instance);
                Renderer[] renderers=instance.GetComponentsInChildren<Renderer>();
                Bounds bounds=renderers[0].bounds;
                foreach(Renderer renderer in renderers)bounds.Encapsulate(renderer.bounds);
                Camera camera=preview.camera;
                camera.orthographic=true;
                camera.orthographicSize=bounds.extents.magnitude*.94f;
                camera.nearClipPlane=.01f;camera.farClipPlane=100;
                camera.transform.position=bounds.center+new Vector3(1.2f,1.3f,-2f).normalized*10;
                camera.transform.LookAt(bounds.center);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                preview.lights[0].intensity=1.4f;preview.lights[0].transform.rotation=Quaternion.Euler(40,35,0);
                preview.lights[1].intensity=.8f;
                preview.ambientColor=new Color(.6f,.6f,.6f);
                // Static previews discard alpha. Recover it from two renders of the
                // same geometry over black/white mattes (also preserves dark materials).
                Texture2D black=RenderMatte(preview,Color.black);
                Texture2D white=RenderMatte(preview,Color.white);
                Texture2D pixels=new Texture2D(256,256,TextureFormat.RGBA32,false);
                Color[] dark=black.GetPixels(),light=white.GetPixels();
                for(int p=0;p<dark.Length;p++)
                {
                    float alpha=1f-Mathf.Max(light[p].r-dark[p].r,Mathf.Max(light[p].g-dark[p].g,light[p].b-dark[p].b));
                    dark[p]=alpha>.005f?new Color(dark[p].r/alpha,dark[p].g/alpha,dark[p].b/alpha,alpha):Color.clear;
                }
                pixels.SetPixels(dark);pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
                Object.DestroyImmediate(black);Object.DestroyImmediate(white);
                Object.DestroyImmediate(pixels);
                Import(path,Vector4.zero);
            }
            finally { preview.Cleanup(); }
        }
        private static Texture2D RenderMatte(PreviewRenderUtility preview,Color background)
        {
            preview.camera.backgroundColor=background;
            preview.BeginStaticPreview(new Rect(0,0,256,256));
            preview.Render(true);
            return preview.EndStaticPreview();
        }
        private static void Import(string path,Vector4 border)
        {
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=100;importer.spriteBorder=border;
            importer.maxTextureSize=256;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
