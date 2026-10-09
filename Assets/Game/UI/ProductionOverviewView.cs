using IndustryTycoon.Mining;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    /// <summary>Shared presentation for every advanced production building.</summary>
    public sealed class ProductionOverviewView : MonoBehaviour
    {
        private static readonly string[] BuildingIds =
        {
            "wood_processor", "iron_smelter", "tool_workshop", "assembly_plant"
        };

        [SerializeField] private GameObject panelRoot;
        [SerializeField] private IndustrialDistrictService district;
        [SerializeField] private LumberCampProgressionService progression;
        [SerializeField] private WoodProcessor processor;
        [SerializeField] private Smelter smelter;
        [SerializeField] private ToolWorkshop workshop;
        [SerializeField] private AssemblyPlant assemblyPlant;
        [SerializeField] private Button[] buildingTabs;
        [SerializeField] private Text titleText;
        [SerializeField] private Text recipeText;
        [SerializeField] private Text bufferText;
        [SerializeField] private Text statusText;
        [SerializeField] private Text upgradeText;
        [SerializeField] private Text specializationText;
        [SerializeField] private Image progressFill;
        [SerializeField] private Text progressText;
        [SerializeField] private Button speedButton;
        [SerializeField] private Button inputButton;
        [SerializeField] private Button outputButton;
        [SerializeField] private Button throughputButton;
        [SerializeField] private Button efficiencyButton;
        [SerializeField] private GameObject specializationConfirmation;
        [SerializeField] private Text confirmationText;
        [SerializeField] private Button confirmSpecializationButton;
        [SerializeField] private Button cancelSpecializationButton;

        private int _selected;
        private FactorySpecialization _pendingSpecialization;
        private float _cycleStartedAt;
        private bool _wasProcessing;
        private float _nextProgressRefresh;
        private UnityAction[] _buildingTabActions;

        public int SelectedBuildingIndex => _selected;

        private void OnEnable()
        {
            if (buildingTabs != null)
            {
                if (_buildingTabActions == null || _buildingTabActions.Length != buildingTabs.Length)
                    _buildingTabActions = new UnityAction[buildingTabs.Length];
                for (int i = 0; i < buildingTabs.Length; i++)
                {
                    int index = i;
                    _buildingTabActions[i] = () => SelectBuilding(index);
                    buildingTabs[i]?.onClick.AddListener(_buildingTabActions[i]);
                }
            }
            speedButton?.onClick.AddListener(BuySpeed);
            inputButton?.onClick.AddListener(BuyInput);
            outputButton?.onClick.AddListener(BuyOutput);
            throughputButton?.onClick.AddListener(ConfirmThroughput);
            efficiencyButton?.onClick.AddListener(ConfirmEfficiency);
            confirmSpecializationButton?.onClick.AddListener(ApplySpecialization);
            cancelSpecializationButton?.onClick.AddListener(CancelSpecialization);
            Subscribe(true);
            Refresh();
        }

        private void OnDisable()
        {
            if (buildingTabs != null && _buildingTabActions != null)
                for (int i = 0; i < buildingTabs.Length && i < _buildingTabActions.Length; i++)
                    if (_buildingTabActions[i] != null)
                        buildingTabs[i]?.onClick.RemoveListener(_buildingTabActions[i]);
            speedButton?.onClick.RemoveListener(BuySpeed);
            inputButton?.onClick.RemoveListener(BuyInput);
            outputButton?.onClick.RemoveListener(BuyOutput);
            throughputButton?.onClick.RemoveListener(ConfirmThroughput);
            efficiencyButton?.onClick.RemoveListener(ConfirmEfficiency);
            confirmSpecializationButton?.onClick.RemoveListener(ApplySpecialization);
            cancelSpecializationButton?.onClick.RemoveListener(CancelSpecialization);
            Subscribe(false);
        }

        private void Update()
        {
            if (panelRoot == null || !panelRoot.activeInHierarchy
                || Time.unscaledTime < _nextProgressRefresh) return;
            _nextProgressRefresh = Time.unscaledTime + 0.1f;
            RefreshProgress();
        }

        public void SelectBuilding(int index)
        {
            _selected = Mathf.Clamp(index, 0, BuildingIds.Length - 1);
            _cycleStartedAt = Time.unscaledTime;
            _wasProcessing = false;
            Refresh();
        }

        public void Refresh()
        {
            string id = BuildingIds[_selected];
            bool unlocked = IsUnlocked(_selected);
            ProductionStatus status = district != null
                ? district.GetProductionStatus(id)
                : ProductionStatus.LockedInactive;
            if (titleText != null) titleText.text = BuildingName(_selected);
            if (recipeText != null) recipeText.text = Recipe(_selected);
            if (bufferText != null) bufferText.text = unlocked ? Buffers(_selected) : "LOCKED — PROGRESS TO UNLOCK";
            if (statusText != null)
            {
                statusText.text = "STATUS: " + StatusLabel(status);
                statusText.color = status == ProductionStatus.Running
                    ? new Color(0.25f, 0.9f, 0.76f)
                    : status == ProductionStatus.LockedInactive
                        ? new Color(0.62f, 0.66f, 0.7f)
                        : new Color(1f, 0.67f, 0.23f);
            }

            int speed = Level(ProductionUpgradeCategory.Speed);
            int input = Level(ProductionUpgradeCategory.InputCapacity);
            int output = Level(ProductionUpgradeCategory.OutputCapacity);
            if (upgradeText != null)
                upgradeText.text = $"UPGRADES  SPEED {speed}/3  •  INPUT {input}/3  •  OUTPUT {output}/3";
            SetUpgradeButton(speedButton, ProductionUpgradeCategory.Speed, unlocked);
            SetUpgradeButton(inputButton, ProductionUpgradeCategory.InputCapacity, unlocked);
            SetUpgradeButton(outputButton, ProductionUpgradeCategory.OutputCapacity, unlocked);

            FactorySpecialization specialization = Specialization();
            if (specializationText != null)
                specializationText.text = specialization == FactorySpecialization.None
                    ? "SPECIALIZATION: NOT CHOSEN (PERMANENT)"
                    : "SPECIALIZATION: " + specialization.ToString().ToUpperInvariant();
            bool mayChoose = unlocked && specialization == FactorySpecialization.None;
            if (throughputButton != null) throughputButton.interactable = mayChoose;
            if (efficiencyButton != null) efficiencyButton.interactable = mayChoose;
            specializationConfirmation?.SetActive(false);
            RefreshProgress();
        }

        private void RefreshProgress()
        {
            bool processing = IsProcessing();
            if (processing && !_wasProcessing) _cycleStartedAt = Time.unscaledTime;
            _wasProcessing = processing;
            float duration = Mathf.Max(0.02f, Duration());
            float value = processing ? Mathf.Clamp01((Time.unscaledTime - _cycleStartedAt) / duration) : 0f;
            if (progressFill != null) progressFill.fillAmount = value;
            if (progressText != null)
                progressText.text = processing ? $"CYCLE {Mathf.RoundToInt(value * 100f)}%" : "CYCLE IDLE";
        }

        private void BuySpeed() => Buy(ProductionUpgradeCategory.Speed);
        private void BuyInput() => Buy(ProductionUpgradeCategory.InputCapacity);
        private void BuyOutput() => Buy(ProductionUpgradeCategory.OutputCapacity);
        private void Buy(ProductionUpgradeCategory category)
        {
            district?.TryPurchaseProductionUpgrade(BuildingIds[_selected], category);
            Refresh();
        }

        private void ConfirmThroughput() => OpenSpecializationConfirmation(FactorySpecialization.Throughput);
        private void ConfirmEfficiency() => OpenSpecializationConfirmation(FactorySpecialization.Efficiency);

        private void OpenSpecializationConfirmation(FactorySpecialization specialization)
        {
            if (Specialization() != FactorySpecialization.None) return;
            _pendingSpecialization = specialization;
            if (confirmationText != null)
                confirmationText.text = "PERMANENT CHOICE\n" + BuildingName(_selected) + " → "
                                        + specialization.ToString().ToUpperInvariant()
                                        + "\nTHIS CANNOT BE CHANGED.";
            specializationConfirmation?.SetActive(true);
        }

        private void ApplySpecialization()
        {
            district?.TryPurchaseSpecialization(BuildingIds[_selected], _pendingSpecialization);
            CancelSpecialization();
            Refresh();
        }

        private void CancelSpecialization()
        {
            _pendingSpecialization = FactorySpecialization.None;
            specializationConfirmation?.SetActive(false);
        }

        private void SetUpgradeButton(Button button, ProductionUpgradeCategory category, bool unlocked)
        {
            if (button == null) return;
            int cost = district != null ? district.GetProductionUpgradeCost(BuildingIds[_selected], category) : int.MaxValue;
            button.interactable = unlocked && cost != int.MaxValue;
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.text = category == ProductionUpgradeCategory.InputCapacity
                ? "INPUT " + Cost(cost)
                : category == ProductionUpgradeCategory.OutputCapacity
                    ? "OUTPUT " + Cost(cost)
                    : "SPEED " + Cost(cost);
        }

        private bool IsUnlocked(int index)
        {
            if (progression == null) return false;
            switch (index)
            {
                case 0: return progression.GetFlag(ProgressFlagId.ProcessorUnlocked);
                case 1: return progression.GetFlag(ProgressFlagId.SmelterUnlocked);
                case 2: return progression.GetMetric(ProgressMetricId.ToolWorkshopUnlocked) > 0;
                default: return progression.GetMetric(ProgressMetricId.AssemblyPlantUnlocked) > 0;
            }
        }

        private int Level(ProductionUpgradeCategory category)
        {
            IProductionUpgradeable target = CurrentUpgradeable();
            if (target == null) return 0;
            return category == ProductionUpgradeCategory.Speed ? target.SpeedUpgradeLevel
                : category == ProductionUpgradeCategory.InputCapacity ? target.InputCapacityUpgradeLevel
                : target.OutputCapacityUpgradeLevel;
        }

        private FactorySpecialization Specialization()
        {
            IFactorySpecializable target = CurrentSpecializable();
            return target?.Specialization ?? FactorySpecialization.None;
        }

        private IProductionUpgradeable CurrentUpgradeable()
        {
            switch (_selected)
            {
                case 0: return processor;
                case 1: return smelter;
                case 2: return workshop;
                default: return assemblyPlant;
            }
        }

        private IFactorySpecializable CurrentSpecializable() => CurrentUpgradeable() as IFactorySpecializable;

        private bool IsProcessing()
        {
            switch (_selected)
            {
                case 0: return processor != null && processor.IsProcessing;
                case 1: return smelter != null && smelter.IsProcessing;
                case 2: return workshop != null && workshop.IsProcessing;
                default: return assemblyPlant != null && assemblyPlant.IsProcessing;
            }
        }

        private float Duration()
        {
            switch (_selected)
            {
                case 0: return processor != null ? processor.ProcessingDuration : 1f;
                case 1: return smelter != null ? smelter.ProcessingDuration : 1f;
                case 2: return workshop != null ? workshop.ProcessingDuration : 1f;
                default: return assemblyPlant != null ? assemblyPlant.ProcessingDuration : 1f;
            }
        }

        private string Buffers(int index)
        {
            switch (index)
            {
                case 0: return processor == null ? "UNAVAILABLE" : $"INPUT  WOOD {processor.InputWood}/{processor.InputCapacity}\nOUTPUT  PLANK {processor.OutputPlanks}/{processor.OutputCapacity}";
                case 1: return smelter == null ? "UNAVAILABLE" : $"INPUT  IRON ORE {smelter.InputOre}/{smelter.InputCapacity}\nOUTPUT  IRON BAR {smelter.OutputBars}/{smelter.OutputCapacity}";
                case 2: return workshop == null ? "UNAVAILABLE" : $"INPUT  PLANK {workshop.InputPlanks}/{workshop.PlankInputCapacity}  •  BAR {workshop.InputIronBars}/{workshop.IronBarInputCapacity}\nOUTPUT  TOOL {workshop.OutputTools}/{workshop.OutputCapacity}";
                default: return assemblyPlant == null ? "UNAVAILABLE" : $"INPUT  BAR {assemblyPlant.InputIronBars}/{assemblyPlant.IronBarInputCapacity}  •  TOOL {assemblyPlant.InputTools}/{assemblyPlant.ToolInputCapacity}\nOUTPUT  MACHINE PART {assemblyPlant.OutputMachineParts}/{assemblyPlant.OutputCapacity}";
            }
        }

        private void Subscribe(bool subscribe)
        {
            if (district != null) { if (subscribe) district.StateChanged += Refresh; else district.StateChanged -= Refresh; }
            if (progression != null) { if (subscribe) progression.StateChanged += Refresh; else progression.StateChanged -= Refresh; }
            if (processor != null) { if (subscribe) processor.ProcessingChanged += HandleProcessing; else processor.ProcessingChanged -= HandleProcessing; }
            if (smelter != null) { if (subscribe) smelter.ProcessingChanged += HandleProcessing; else smelter.ProcessingChanged -= HandleProcessing; }
            if (assemblyPlant != null) { if (subscribe) assemblyPlant.StateChanged += Refresh; else assemblyPlant.StateChanged -= Refresh; }
        }

        private void HandleProcessing(bool _) => Refresh();
        private static string Cost(int cost) => cost == int.MaxValue ? "MAX" : "$" + cost;
        private static string BuildingName(int index) => index == 0 ? "PROCESSOR" : index == 1 ? "SMELTER" : index == 2 ? "TOOL WORKSHOP" : "ASSEMBLY PLANT";
        private static string Recipe(int index) => index == 0 ? "1 WOOD  →  1 PLANK" : index == 1 ? "2 IRON ORE  →  1 IRON BAR" : index == 2 ? "2 PLANK + 1 IRON BAR  →  1 TOOL" : "2 IRON BAR + 1 TOOL  →  1 MACHINE PART";
        private static string StatusLabel(ProductionStatus status) => status == ProductionStatus.Running ? "RUNNING" : status == ProductionStatus.MissingInput ? "WAITING FOR INPUT" : status == ProductionStatus.OutputFull ? "OUTPUT FULL" : status == ProductionStatus.WaitingForLogistics ? "WAITING FOR ROUTE" : "LOCKED";
    }
}
