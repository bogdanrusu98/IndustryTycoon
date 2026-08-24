using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class WoodAutoFeederTransferVisual : MonoBehaviour
    {
        private WoodAutoFeederFeedback _owner;
        private uint _generation;
        private bool _isLeased;

        public bool IsLeased => _isLeased;
        public uint Generation => _generation;

        public void Lease(WoodAutoFeederFeedback owner, uint generation)
        {
            _owner = owner;
            _generation = generation;
            _isLeased = owner != null && generation != 0;
            gameObject.SetActive(_isLeased);
        }

        public void ReleaseToPool()
        {
            _isLeased = false;
            _generation = 0;
            _owner = null;
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (!_isLeased || _owner == null)
            {
                return;
            }

            WoodAutoFeederFeedback owner = _owner;
            uint generation = _generation;
            _isLeased = false;
            _generation = 0;
            _owner = null;
            owner.HandleTransferVisualDisabled(this, generation);
        }
    }
}
