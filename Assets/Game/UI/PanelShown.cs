using UnityEngine;
using UnityEngine.Events;

namespace IndustryTycoon.UI
{
    /// <summary>Refreshes event-driven presenters when a previously hidden panel opens.</summary>
    public sealed class PanelShown : MonoBehaviour
    {
        public UnityEvent shown = new UnityEvent();
        private void OnEnable() => shown.Invoke();
    }
}
