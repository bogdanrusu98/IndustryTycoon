using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace IndustryTycoon.Editor
{
    /// <summary>Reproducible scene-cost comparison and collider-aware route checks.</summary>
    public static class ModernVisualVerification
    {
        public static void RunSourceAndMetrics()
        {
            ModernAssetRecoveryAudit.Run();
            RunMetrics();
        }

        public static void RunMetrics()
        {
            const string temporary="Assets/ModernBaselineAudit.unity";
            try {
                File.Copy("Logs/ModernVisual/baseline.unity",temporary,true);
                AssetDatabase.ImportAsset(temporary,ImportAssetOptions.ForceSynchronousImport);
                Metrics(EditorSceneManager.OpenScene(temporary),"baseline-metrics.txt");
                Metrics(EditorSceneManager.OpenScene("Assets/Game/Scenes/Prototype_LumberCamp.unity"),"final-metrics.txt");
            } finally {AssetDatabase.DeleteAsset(temporary);}
        }

        private static void Metrics(Scene scene,string filename)
        {
            var objects=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var renderers=objects.SelectMany(t=>t.GetComponents<Renderer>()).ToArray();
            var meshes=renderers.Where(r=>r.enabled&&r.gameObject.activeInHierarchy)
                .Select(r=>r.GetComponent<MeshFilter>()).Where(f=>f!=null)
                .Select(f=>f.sharedMesh).Where(m=>m!=null).ToArray();
            var materials=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
            var textures=materials.SelectMany(m=>m.GetTexturePropertyNames().Select(m.GetTexture)).Where(t=>t!=null).Distinct().ToArray();
            File.WriteAllLines("Logs/ModernVisual/"+filename,new[]{
                "Static authored scene, same editor/platform; not device FPS",
                "objects="+objects.Length,"renderers="+renderers.Length,
                "active-renderers="+renderers.Count(r=>r.enabled&&r.gameObject.activeInHierarchy),
                "active-vertices="+meshes.Sum(m=>(long)m.vertexCount),
                "active-triangles="+meshes.Sum(m=>(long)m.triangles.Length/3),
                "materials="+materials.Length,"textures="+textures.Length,
                "texture-memory-bytes="+textures.Sum(Profiler.GetRuntimeMemorySizeLong)});
        }

        public static void RuntimeRoutes(string folder)
        {
            var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var player=all.First(t=>t.name=="Player").GetComponent<CharacterController>();
            Vector3 original=player.transform.position;player.enabled=false;Physics.SyncTransforms();
            const float step=.5f;const int width=117,height=129;
            var blocked=new bool[width*height];
            Func<int,Vector3> point=index=>new Vector3(-29+(index%width)*step,0,-17+(index/width)*step);
            for(int i=0;i<blocked.Length;i++) {
                Vector3 p=point(i);
                blocked[i]=Physics.CheckCapsule(p+Vector3.up*(player.radius+.12f),p+Vector3.up*(player.height-player.radius),player.radius,~0,QueryTriggerInteraction.Ignore);
            }
            Func<Vector3,int> nearest=p=>Enumerable.Range(0,blocked.Length).Where(i=>!blocked[i]).OrderBy(i=>(point(i)-new Vector3(p.x,0,p.z)).sqrMagnitude).First();
            int start=nearest(new Vector3(0,0,-1));
            int[] previous=Enumerable.Repeat(-1,blocked.Length).ToArray();
            var queue=new Queue<int>();queue.Enqueue(start);previous[start]=start;
            while(queue.Count>0) {
                int i=queue.Dequeue(),x=i%width,z=i/width;
                foreach(int next in new[]{x>0?i-1:-1,x<width-1?i+1:-1,z>0?i-width:-1,z<height-1?i+width:-1}) {
                    if(next<0||blocked[next]||previous[next]>=0)continue;
                    previous[next]=i;queue.Enqueue(next);
                }
            }
            var rows=new List<string>{"Developed fixture; 0.5m cardinal grid, actual CharacterController capsule clearance, trigger colliders ignored.",
                "Lengths are conservative grid walking bounds; no diagonals. Movement speed unchanged at 5.5m/s."};
            try {
                foreach(string name in new[]{"Wood Stockpile","Processor Input Zone","Packing Station","Ore Storage","Iron Smelter","Central Warehouse","Assembly Plant"}) {
                    Transform destination=all.First(t=>t.name==name);int end=nearest(destination.position);
                    if(previous[end]<0)throw new InvalidOperationException("Unreachable destination: "+name);
                    var path=new List<int>();for(int i=end;i!=start;i=previous[i])path.Add(i);path.Add(start);path.Reverse();
                    player.transform.position=point(start)+Vector3.up*.05f;player.enabled=true;
                    foreach(int node in path.Skip(1)) {
                        Vector3 delta=point(node)-player.transform.position;delta.y=0;player.Move(delta);
                        Vector3 error=point(node)-player.transform.position;error.y=0;
                        if(error.magnitude>.2f)throw new InvalidOperationException("Controller blocked on audited path to "+name);
                    }
                    player.enabled=false;
                    float distance=(path.Count-1)*step;
                    rows.Add($"{name}|destination={destination.position}|grid-walk={distance:F1}m|time<={distance/5.5f:F2}s|controller=PASS");
                }
            } finally {player.transform.position=original;player.enabled=true;}
            File.WriteAllLines(folder+"/navigation-audit.txt",rows);
        }
    }
}
