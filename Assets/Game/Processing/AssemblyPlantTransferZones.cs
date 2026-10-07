using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Processing
{
    [RequireComponent(typeof(Collider))]
    public sealed class AssemblyPlantInputZone : MonoBehaviour
    {
        [SerializeField] private AssemblyPlant plant;
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private Collider playerCollider;
        [SerializeField] private ResourceType resourceType = ResourceType.IronBar;
        private void OnTriggerStay(Collider other)
        {
            if (other == playerCollider) plant?.TryTransferInputFrom(carryStack, resourceType);
        }
    }

}
