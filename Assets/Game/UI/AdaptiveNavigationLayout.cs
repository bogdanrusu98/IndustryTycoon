using IndustryTycoon.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    /// <summary>Runs once after an unlock event, after availability presenters have finished.</summary>
    public sealed class AdaptiveNavigationLayout : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progression;
        [SerializeField] private Button[] buttons;
        private bool dirty;
        public void Configure(LumberCampProgressionService service,Button[] entries){progression=service;buttons=entries;}
        private void OnEnable(){if(progression!=null)progression.StateChanged+=Invalidate;Invalidate();}
        private void OnDisable(){if(progression!=null)progression.StateChanged-=Invalidate;}
        private void Invalidate()=>dirty=true;
        private void LateUpdate(){
            if(!dirty||buttons==null)return;dirty=false;
            int count=0;foreach(Button b in buttons)if(b!=null&&b.gameObject.activeSelf)count++;
            if(count==0)return;
            var root=(RectTransform)transform;
            root.anchorMin=root.anchorMax=new Vector2(.5f,0);root.pivot=new Vector2(.5f,0);
            float available=((RectTransform)root.parent).rect.width-48;
            root.sizeDelta=new Vector2(Mathf.Min(available,count*172f+16),144);
            int index=0;
            foreach(Button b in buttons){if(b==null||!b.gameObject.activeSelf)continue;
                var rect=(RectTransform)b.transform;rect.anchorMin=new Vector2(index/(float)count,0);rect.anchorMax=new Vector2((index+1)/(float)count,1);
                rect.offsetMin=new Vector2(4,8);rect.offsetMax=new Vector2(-4,-8);index++;
            }
        }
    }
}
