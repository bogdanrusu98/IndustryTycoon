using System.Collections;
using System.Collections.Generic;
using IndustryTycoon.Core;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace IndustryTycoon.ResourceSystem
{
    public sealed class WoodSpawner : MonoBehaviour
    {
        [Header("Resource")]
        [SerializeField] private ResourcePickup woodPrefab;

        [Header("Spawn Area")]
        [SerializeField] private Vector2 spawnArea = new Vector2(14f, 16f);
        [SerializeField, Min(0f)] private float spawnHeight = 0.32f;
        [SerializeField, Min(0.1f)] private float spawnInterval = 1.25f;
        [SerializeField, Min(0.1f)] private float productionRateMultiplier = 1f;

        [Header("Pool")]
        [SerializeField, Min(1)] private int initialActiveCount = 8;
        [SerializeField, Min(1)] private int prewarmCount = 12;
        [SerializeField, Min(1)] private int maximumActiveCount = 24;

        private ObjectPool<ResourcePickup> _pool;
        private WaitForSeconds _spawnWait;
        private Coroutine _spawnCoroutine;
        private readonly List<ResourcePickup> _activePickups = new List<ResourcePickup>(32);
        private int _activeCount;
        private bool _hasStarted;
        private bool _isShuttingDown;

        public event System.Action<int> WoodProduced;

        public int ActiveCount => _activeCount;
        public int ActiveRegistryCount => _activePickups.Count;
        public ResourcePickup WoodPrefab => woodPrefab;
        public float BaseSpawnInterval => spawnInterval;
        public float ProductionRateMultiplier => productionRateMultiplier;
        public float EffectiveSpawnInterval => spawnInterval / Mathf.Max(0.1f, productionRateMultiplier);

        public void ConfigurePrefab(ResourcePickup prefab)
        {
            woodPrefab = prefab;
        }

        public void SetProductionRateMultiplier(float multiplier)
        {
            float clampedMultiplier = Mathf.Max(0.1f, multiplier);
            if (Mathf.Approximately(productionRateMultiplier, clampedMultiplier))
            {
                return;
            }

            bool shouldRestartRoutine = _spawnCoroutine != null;
            StopSpawnRoutine();
            productionRateMultiplier = clampedMultiplier;
            RebuildSpawnWait();

            if (shouldRestartRoutine && isActiveAndEnabled)
            {
                StartSpawnRoutine();
            }
        }

        public bool TryClaimNearestAvailable(
            Vector3 origin,
            Object claimant,
            out ResourceClaimHandle claim)
        {
            claim = default;
            if (claimant == null)
            {
                return false;
            }

            ResourcePickup nearest = null;
            float nearestDistanceSquared = float.PositiveInfinity;
            for (int i = 0; i < _activePickups.Count; i++)
            {
                ResourcePickup pickup = _activePickups[i];
                if (pickup == null
                    || !pickup.IsAvailable
                    || pickup.IsClaimed
                    || pickup.ResourceType != ResourceType.Wood
                    || pickup.Amount != 1)
                {
                    continue;
                }

                Vector3 offset = pickup.transform.position - origin;
                offset.y = 0f;
                float distanceSquared = offset.sqrMagnitude;
                if (distanceSquared >= nearestDistanceSquared)
                {
                    continue;
                }

                nearest = pickup;
                nearestDistanceSquared = distanceSquared;
            }

            return nearest != null
                   && nearest.TryClaim(claimant, ResourceClaimPriority.Worker, out claim);
        }

        private void Awake()
        {
            if (woodPrefab == null)
            {
                Debug.LogError("WoodSpawner requires a ResourcePickup prefab.", this);
                enabled = false;
                return;
            }

            RebuildSpawnWait();
            _pool = new ObjectPool<ResourcePickup>(
                CreatePooledItem,
                OnTakeFromPool,
                OnReturnToPool,
                OnDestroyPooledItem,
                true,
                prewarmCount,
                maximumActiveCount);

            PrewarmPool();
        }

        private void Start()
        {
            if (_pool == null)
            {
                return;
            }

            int spawnCount = Mathf.Min(initialActiveCount, maximumActiveCount);
            for (int i = 0; i < spawnCount; i++)
            {
                // Initial loose Wood is bootstrap world inventory, not production.
                // Counting it would fabricate lifetime progress on every scene load.
                SpawnOne(false);
            }

            _hasStarted = true;
            StartSpawnRoutine();
        }

        private void OnEnable()
        {
            if (_hasStarted)
            {
                StartSpawnRoutine();
            }
        }

        private void OnDisable()
        {
            StopSpawnRoutine();
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            StopSpawnRoutine();
            _activePickups.Clear();
            _activeCount = 0;
            _pool?.Clear();
        }

        private void StartSpawnRoutine()
        {
            if (_pool != null && _spawnCoroutine == null)
            {
                _spawnCoroutine = StartCoroutine(SpawnRoutine());
            }
        }

        private void StopSpawnRoutine()
        {
            if (_spawnCoroutine == null)
            {
                return;
            }

            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }

        private IEnumerator SpawnRoutine()
        {
            while (true)
            {
                yield return _spawnWait;
                if (_activeCount < maximumActiveCount)
                {
                    SpawnOne(true);
                }
            }
        }

        private void RebuildSpawnWait()
        {
            _spawnWait = new WaitForSeconds(EffectiveSpawnInterval);
        }

        private void PrewarmPool()
        {
            int count = Mathf.Min(prewarmCount, maximumActiveCount);
            var warmedItems = new List<ResourcePickup>(count);
            for (int i = 0; i < count; i++)
            {
                warmedItems.Add(_pool.Get());
            }

            for (int i = 0; i < warmedItems.Count; i++)
            {
                _pool.Release(warmedItems[i]);
            }
        }

        private void SpawnOne(bool countAsProduction)
        {
            ResourcePickup pickup = _pool.Get();
            Vector2 randomPoint = new Vector2(
                Random.Range(-spawnArea.x * 0.5f, spawnArea.x * 0.5f),
                Random.Range(-spawnArea.y * 0.5f, spawnArea.y * 0.5f));
            Vector3 spawnPosition = transform.position
                                    + new Vector3(randomPoint.x, spawnHeight, randomPoint.y);
            Quaternion spawnRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            pickup.PrepareForSpawn(spawnPosition, spawnRotation);
            _activePickups.Add(pickup);
            _activeCount = _activePickups.Count;
            if (countAsProduction)
            {
                WoodProduced?.Invoke(pickup.Amount);
            }
        }

        private ResourcePickup CreatePooledItem()
        {
            ResourcePickup instance = Instantiate(woodPrefab, transform);
            instance.name = "Wood (Pooled)";
            instance.SetReleaseAction(ReturnToPool);
            instance.gameObject.SetActive(false);
            return instance;
        }

        private static void OnTakeFromPool(ResourcePickup pickup)
        {
            pickup.gameObject.SetActive(true);
        }

        private static void OnReturnToPool(ResourcePickup pickup)
        {
            pickup.MarkReleased();
            pickup.gameObject.SetActive(false);
        }

        private static void OnDestroyPooledItem(ResourcePickup pickup)
        {
            if (pickup != null)
            {
                Destroy(pickup.gameObject);
            }
        }

        private void ReturnToPool(ResourcePickup pickup)
        {
            if (_isShuttingDown || _pool == null || pickup == null)
            {
                return;
            }

            UnregisterActive(pickup);
            _activeCount = _activePickups.Count;
            _pool.Release(pickup);
        }

        private void UnregisterActive(ResourcePickup pickup)
        {
            for (int i = 0; i < _activePickups.Count; i++)
            {
                if (_activePickups[i] != pickup)
                {
                    continue;
                }

                int lastIndex = _activePickups.Count - 1;
                _activePickups[i] = _activePickups[lastIndex];
                _activePickups.RemoveAt(lastIndex);
                return;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.45f, 0.25f, 0.08f, 0.7f);
            Vector3 center = transform.position + (Vector3.up * spawnHeight);
            Gizmos.DrawWireCube(center, new Vector3(spawnArea.x, 0.1f, spawnArea.y));
        }

        private void OnValidate()
        {
            spawnArea.x = Mathf.Max(0.1f, spawnArea.x);
            spawnArea.y = Mathf.Max(0.1f, spawnArea.y);
            spawnHeight = Mathf.Max(0f, spawnHeight);
            spawnInterval = Mathf.Max(0.1f, spawnInterval);
            productionRateMultiplier = Mathf.Max(0.1f, productionRateMultiplier);
            maximumActiveCount = Mathf.Max(1, maximumActiveCount);
            initialActiveCount = Mathf.Clamp(initialActiveCount, 1, maximumActiveCount);
            prewarmCount = Mathf.Clamp(prewarmCount, 1, maximumActiveCount);
        }
    }
}
