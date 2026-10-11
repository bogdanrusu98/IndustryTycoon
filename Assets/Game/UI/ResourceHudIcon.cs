using IndustryTycoon.Core;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    [RequireComponent(typeof(Image))]
    public sealed class ResourceHudIcon : MonoBehaviour
    {
        // Baked directly from the seven ResourceVisual roots, in ResourceType order.
        [SerializeField] private Sprite[] resources;
        public void Configure(Sprite[] sprites) { resources = sprites; Show(ResourceType.Wood); }
        public void Show(ResourceType type)
        {
            Image image=GetComponent<Image>();
            int index=(int)type;
            image.sprite=resources!=null && index>=0 && index<resources.Length ? resources[index] : null;
            image.enabled=image.sprite!=null;
            image.preserveAspect=true;
            image.color=Color.white;
            image.raycastTarget=false;
        }
        public static string Label(ResourceType type)
        {
            switch(type)
            {
                case ResourceType.IronOre:return "Iron ore";
                case ResourceType.IronBar:return "Iron bar";
                case ResourceType.MachinePart:return "Machine part";
                default:return type.ToString();
            }
        }
    }
}
