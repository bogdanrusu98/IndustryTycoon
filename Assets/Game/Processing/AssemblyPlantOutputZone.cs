using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Processing
{
    [RequireComponent(typeof(Collider))]
    public sealed class AssemblyPlantOutputZone : MonoBehaviour
    {
        [SerializeField] private AssemblyPlant plant;
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private Collider playerCollider;

        private void OnTriggerStay(Collider other)
        {
            if (other == playerCollider)
            {
                plant?.TryTransferOutputTo(carryStack);
            }
        }
    }
}
