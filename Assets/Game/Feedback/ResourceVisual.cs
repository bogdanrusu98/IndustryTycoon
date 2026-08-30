using IndustryTycoon.Core;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class ResourceVisual : MonoBehaviour
    {
        [SerializeField] private GameObject woodRoot;
        [SerializeField] private GameObject plankRoot;
        [SerializeField] private GameObject crateRoot;
        [SerializeField] private GameObject ironOreRoot;
        [SerializeField] private GameObject ironBarRoot;
        [SerializeField] private ResourceType displayedResourceType = ResourceType.Wood;

        public ResourceType DisplayedResourceType => displayedResourceType;
        public GameObject WoodRoot => woodRoot;
        public GameObject PlankRoot => plankRoot;
        public GameObject CrateRoot => crateRoot;
        public GameObject IronOreRoot => ironOreRoot;
        public GameObject IronBarRoot => ironBarRoot;

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

        public void Configure(
            GameObject woodVisualRoot,
            GameObject plankVisualRoot,
            GameObject crateVisualRoot,
            ResourceType initialResourceType = ResourceType.Wood)
        {
            woodRoot = woodVisualRoot;
            plankRoot = plankVisualRoot;
            crateRoot = crateVisualRoot;
            Show(initialResourceType);
        }

        public void Configure(
            GameObject woodVisualRoot,
            GameObject plankVisualRoot,
            GameObject crateVisualRoot,
            GameObject ironOreVisualRoot,
            GameObject ironBarVisualRoot,
            ResourceType initialResourceType = ResourceType.Wood)
        {
            woodRoot = woodVisualRoot;
            plankRoot = plankVisualRoot;
            crateRoot = crateVisualRoot;
            ironOreRoot = ironOreVisualRoot;
            ironBarRoot = ironBarVisualRoot;
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

            if (crateRoot != null)
            {
                crateRoot.SetActive(displayedResourceType == ResourceType.Crate);
            }

            if (ironOreRoot != null)
            {
                ironOreRoot.SetActive(displayedResourceType == ResourceType.IronOre);
            }

            if (ironBarRoot != null)
            {
                ironBarRoot.SetActive(displayedResourceType == ResourceType.IronBar);
            }
        }

        private void OnValidate()
        {
            ApplyDisplayedType();
        }
    }
}
