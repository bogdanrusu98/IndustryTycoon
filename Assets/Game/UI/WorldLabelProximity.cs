using UnityEngine;

namespace IndustryTycoon.UI
{
    /// <summary>Bounds the information density without disabling any gameplay/presenter object.</summary>
    public sealed class WorldLabelProximity : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private TextMesh[] labels = System.Array.Empty<TextMesh>();
        private Renderer[][] renderers;
        private float nextRefresh;
        public void Configure(Transform target,TextMesh[] entries) { player=target;labels=entries; }
        private void Awake() {
            renderers=new Renderer[labels.Length][];
            for(int i=0;i<labels.Length;i++)renderers[i]=labels[i].GetComponentsInChildren<Renderer>(true);
        }
        private void LateUpdate() {
            if(player==null || Time.unscaledTime<nextRefresh)return;
            nextRefresh=Time.unscaledTime+.25f;
            // At most two distinct local destinations; stacked labels on one machine
            // no longer compete with the contextual panel and the main objective.
            for(int i=0;i<labels.Length;i++)
                foreach(Renderer r in renderers[i])if(r!=null)r.enabled=false;
            int first=Nearest(-1);
            Show(first);
            Show(Nearest(first));
        }
        private int Nearest(int exclude) {
            int best=-1;float distance=25f;
            for(int i=0;i<labels.Length;i++) {
                if(labels[i]==null||!labels[i].gameObject.activeInHierarchy||i==exclude)continue;
                Vector3 point=labels[i].transform.position;point.y=player.position.y;
                if(exclude>=0) {
                    Vector3 previous=labels[exclude].transform.position;previous.y=point.y;
                    if((point-previous).sqrMagnitude<6.25f)continue;
                }
                float d=(point-player.position).sqrMagnitude;
                if(d<distance){distance=d;best=i;}
            }
            return best;
        }
        private void Show(int index) {if(index>=0)foreach(Renderer r in renderers[index])if(r!=null)r.enabled=true;}
    }
}
