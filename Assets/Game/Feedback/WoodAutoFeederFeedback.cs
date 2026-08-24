using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Logistics;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class WoodAutoFeederFeedback : MonoBehaviour
    {
        private sealed class TransferVisualSlot
        {
            public GameObject Visual;
            public ResourceVisual ResourceVisual;
            public WoodAutoFeederTransferVisual Lease;
            public Vector3 BaseScale;
        }

        [Header("References")]
        [SerializeField] private WoodAutoFeeder autoFeeder;
        [SerializeField] private Transform transferVisualRoot;
        [SerializeField] private GameObject woodVisualPrefab;
        [SerializeField] private Transform routeStart;
        [SerializeField] private Transform routeControl;
        [SerializeField] private Transform routeEnd;
        [SerializeField] private TextMesh statusText;
        [SerializeField] private Renderer statusIndicator;
        [SerializeField] private Material idleMaterial;
        [SerializeField] private Material movingMaterial;
        [SerializeField] private Material destinationFullMaterial;
        [SerializeField] private Transform sourceRoller;
        [SerializeField] private Transform destinationRoller;

        [Header("Capped Transfer Visuals")]
        [SerializeField, Range(1, 4)] private int visualPoolSize = 2;
        [SerializeField, Range(0.25f, 1.25f)] private float transferVisualScale = 0.72f;
        [SerializeField, Min(1f)] private float rollerSpeed = 260f;

        private readonly List<TransferVisualSlot> _visualPool =
            new List<TransferVisualSlot>(2);
        private TransferVisualSlot _activeVisual;
        private uint _activeGeneration;
        private WoodAutoFeederState _displayedState = WoodAutoFeederState.Disabled;

        public int ConfiguredVisualPoolSize => visualPoolSize;
        public int VisualPoolCount => _visualPool.Count;
        public int ActiveVisualCount => _activeVisual != null ? 1 : 0;
        public uint ActiveVisualGeneration => _activeGeneration;
        public float TransferVisualScale => transferVisualScale;
        public float RollerSpeed => rollerSpeed;
        public WoodAutoFeederState DisplayedState => _displayedState;

        private void Awake()
        {
            EnsureVisualPool();
            RefreshState(autoFeeder != null
                ? autoFeeder.State
                : WoodAutoFeederState.Disabled);
        }

        private void OnEnable()
        {
            EnsureVisualPool();
            if (autoFeeder != null)
            {
                autoFeeder.StateChanged += HandleFeederStateChanged;
                RefreshState(autoFeeder.State);
                autoFeeder.TryStartTransfer();
            }
        }

        private void OnDisable()
        {
            if (autoFeeder != null)
            {
                autoFeeder.StateChanged -= HandleFeederStateChanged;
            }

            if (_activeVisual != null)
            {
                uint generation = _activeGeneration;
                TransferVisualSlot slot = _activeVisual;
                _activeVisual = null;
                _activeGeneration = 0;
                slot.Lease.ReleaseToPool();
                autoFeeder?.HandleTransferVisualDisabled(generation);
            }

            RefreshState(WoodAutoFeederState.Disabled);
        }

        private void Update()
        {
            if (_displayedState != WoodAutoFeederState.Moving)
            {
                return;
            }

            float rotation = rollerSpeed * Time.deltaTime;
            if (sourceRoller != null)
            {
                sourceRoller.Rotate(Vector3.right, rotation, Space.Self);
            }

            if (destinationRoller != null)
            {
                destinationRoller.Rotate(Vector3.right, rotation, Space.Self);
            }
        }

        public bool TryBeginTransfer(uint generation)
        {
            if (!isActiveAndEnabled || generation == 0 || _activeVisual != null)
            {
                return false;
            }

            EnsureVisualPool();
            TransferVisualSlot slot = FindAvailableSlot();
            if (slot == null)
            {
                return false;
            }

            _activeVisual = slot;
            _activeGeneration = generation;
            slot.ResourceVisual?.Show(ResourceType.Wood);
            slot.Visual.transform.localScale = slot.BaseScale;
            slot.Lease.Lease(this, generation);
            SetTransferProgress(generation, 0f);
            return true;
        }

        public bool SetTransferProgress(uint generation, float normalizedProgress)
        {
            if (_activeVisual == null
                || generation == 0
                || generation != _activeGeneration
                || routeStart == null
                || routeControl == null
                || routeEnd == null)
            {
                return false;
            }

            float progress = FeedbackTween.EaseInOutCubic(
                Mathf.Clamp01(normalizedProgress));
            Vector3 startToControl = Vector3.Lerp(
                routeStart.position,
                routeControl.position,
                progress);
            Vector3 controlToEnd = Vector3.Lerp(
                routeControl.position,
                routeEnd.position,
                progress);
            Vector3 position = Vector3.Lerp(startToControl, controlToEnd, progress);

            const float DirectionSample = 0.015f;
            float nextProgress = Mathf.Min(1f, progress + DirectionSample);
            Vector3 nextStartToControl = Vector3.Lerp(
                routeStart.position,
                routeControl.position,
                nextProgress);
            Vector3 nextControlToEnd = Vector3.Lerp(
                routeControl.position,
                routeEnd.position,
                nextProgress);
            Vector3 nextPosition = Vector3.Lerp(
                nextStartToControl,
                nextControlToEnd,
                nextProgress);

            Transform visualTransform = _activeVisual.Visual.transform;
            visualTransform.position = position;
            Vector3 direction = nextPosition - position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                visualTransform.rotation = Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up);
            }

            return true;
        }

        public bool ReleaseTransferVisual(uint generation)
        {
            if (_activeVisual == null
                || generation == 0
                || generation != _activeGeneration)
            {
                return false;
            }

            TransferVisualSlot slot = _activeVisual;
            _activeVisual = null;
            _activeGeneration = 0;
            slot.Lease.ReleaseToPool();
            slot.Visual.transform.localScale = slot.BaseScale;
            return true;
        }

        internal void HandleTransferVisualDisabled(
            WoodAutoFeederTransferVisual visual,
            uint generation)
        {
            if (_activeVisual == null
                || _activeVisual.Lease != visual
                || generation == 0
                || generation != _activeGeneration)
            {
                return;
            }

            _activeVisual = null;
            _activeGeneration = 0;
            autoFeeder?.HandleTransferVisualDisabled(generation);
        }

        private void EnsureVisualPool()
        {
            if (woodVisualPrefab == null)
            {
                return;
            }

            Transform parent = transferVisualRoot != null
                ? transferVisualRoot
                : transform;
            while (_visualPool.Count < visualPoolSize)
            {
                GameObject visual = Instantiate(woodVisualPrefab, parent);
                visual.name = $"Auto Feeder Wood {_visualPool.Count + 1:00}";
                WoodAutoFeederTransferVisual lease =
                    visual.GetComponent<WoodAutoFeederTransferVisual>();
                if (lease == null)
                {
                    lease = visual.AddComponent<WoodAutoFeederTransferVisual>();
                }

                Vector3 baseScale = visual.transform.localScale * transferVisualScale;
                visual.transform.localScale = baseScale;
                lease.ReleaseToPool();
                _visualPool.Add(new TransferVisualSlot
                {
                    Visual = visual,
                    ResourceVisual = visual.GetComponent<ResourceVisual>(),
                    Lease = lease,
                    BaseScale = baseScale
                });
            }
        }

        private TransferVisualSlot FindAvailableSlot()
        {
            for (int i = 0; i < _visualPool.Count; i++)
            {
                TransferVisualSlot slot = _visualPool[i];
                if (!slot.Lease.IsLeased && !slot.Visual.activeSelf)
                {
                    return slot;
                }
            }

            return null;
        }

        private void HandleFeederStateChanged(WoodAutoFeederState state)
        {
            RefreshState(state);
        }

        private void RefreshState(WoodAutoFeederState state)
        {
            _displayedState = state;
            if (statusText != null)
            {
                switch (state)
                {
                    case WoodAutoFeederState.Moving:
                        statusText.text = "AUTO FEEDER  MOVING";
                        break;
                    case WoodAutoFeederState.WaitingForWood:
                        statusText.text = "AUTO FEEDER  WAITING FOR WOOD";
                        break;
                    case WoodAutoFeederState.DestinationFull:
                        statusText.text = "AUTO FEEDER  PROCESSOR FULL";
                        break;
                    case WoodAutoFeederState.Idle:
                        statusText.text = "AUTO FEEDER  READY";
                        break;
                    default:
                        statusText.text = "AUTO FEEDER  OFFLINE";
                        break;
                }
            }

            if (statusIndicator == null)
            {
                return;
            }

            Material stateMaterial = state == WoodAutoFeederState.Moving
                ? movingMaterial
                : state == WoodAutoFeederState.DestinationFull
                    ? destinationFullMaterial
                    : idleMaterial;
            if (stateMaterial != null)
            {
                statusIndicator.sharedMaterial = stateMaterial;
            }
        }

        private void OnValidate()
        {
            visualPoolSize = Mathf.Clamp(visualPoolSize, 1, 4);
            transferVisualScale = Mathf.Clamp(transferVisualScale, 0.25f, 1.25f);
            rollerSpeed = Mathf.Max(1f, rollerSpeed);
        }
    }
}
