using IndustryTycoon.Core;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class ResourceVisual : MonoBehaviour
    {
        [SerializeField] private GameObject woodRoot;
        [SerializeField] private GameObject plankRoot;
        [SerializeField] private ResourceType displayedResourceType = ResourceType.Wood;

        public ResourceType DisplayedResourceType => displayedResourceType;
        public GameObject WoodRoot => woodRoot;
        public GameObject PlankRoot => plankRoot;

        private void Awake()
        {
            ApplyDisplayedType();
        }

        public void Configure(
            GameObject woodVisualRoot,
            GameObject plankVisualRoot,
            ResourceType initialResourceType = ResourceType.Wood)
        {
            woodRoot = woodVisualRoot;
            plankRoot = plankVisualRoot;
            Show(initialResourceType);
        }

        public void Show(ResourceType resourceType)
        {
            displayedResourceType = resourceType;
            ApplyDisplayedType();
        }

        private void ApplyDisplayedType()
        {
            if (woodRoot != null)
            {
                woodRoot.SetActive(displayedResourceType == ResourceType.Wood);
            }

            if (plankRoot != null)
            {
                plankRoot.SetActive(displayedResourceType == ResourceType.Plank);
            }
        }

        private void OnValidate()
        {
            ApplyDisplayedType();
        }
    }
}
