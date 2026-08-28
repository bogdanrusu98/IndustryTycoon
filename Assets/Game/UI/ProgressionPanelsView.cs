using System.Text;
using IndustryTycoon.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class ProgressionPanelsView : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progressionService;

        [Header("Navigation")]
        [SerializeField] private Button contractTabButton;
        [SerializeField] private Button achievementsTabButton;
        [SerializeField] private GameObject contractPanelRoot;
        [SerializeField] private GameObject achievementsPanelRoot;

        [Header("Contract")]
        [SerializeField] private Text contractDescriptionText;
        [SerializeField] private Text contractProgressText;
        [SerializeField] private Text contractRewardText;
        [SerializeField] private Text contractStateText;
        [SerializeField] private Button claimButton;

        [Header("Achievements")]
        [SerializeField] private Text achievementsListText;

        public LumberCampProgressionService ProgressionService => progressionService;
        public Button ContractTabButton => contractTabButton;
        public Button AchievementsTabButton => achievementsTabButton;
        public GameObject ContractPanelRoot => contractPanelRoot;
        public GameObject AchievementsPanelRoot => achievementsPanelRoot;
        public Text ContractDescriptionText => contractDescriptionText;
        public Text ContractProgressText => contractProgressText;
        public Text ContractRewardText => contractRewardText;
        public Text ContractStateText => contractStateText;
        public Button ClaimButton => claimButton;
        public Text AchievementsListText => achievementsListText;

        private void OnEnable()
        {
            if (progressionService != null)
            {
                progressionService.StateChanged += Refresh;
            }

            contractTabButton?.onClick.AddListener(ToggleContractPanel);
            achievementsTabButton?.onClick.AddListener(ToggleAchievementsPanel);
            claimButton?.onClick.AddListener(ClaimActiveContract);
            Refresh();
        }

        private void OnDisable()
        {
            if (progressionService != null)
            {
                progressionService.StateChanged -= Refresh;
            }

            contractTabButton?.onClick.RemoveListener(ToggleContractPanel);
            achievementsTabButton?.onClick.RemoveListener(ToggleAchievementsPanel);
            claimButton?.onClick.RemoveListener(ClaimActiveContract);
        }

        public void ShowContractPanel()
        {
            if (contractPanelRoot != null)
            {
                contractPanelRoot.SetActive(true);
            }

            if (achievementsPanelRoot != null)
            {
                achievementsPanelRoot.SetActive(false);
            }

            Refresh();
        }

        public void ToggleContractPanel()
        {
            bool shouldOpen = contractPanelRoot != null
                              && !contractPanelRoot.activeSelf;
            contractPanelRoot?.SetActive(shouldOpen);
            achievementsPanelRoot?.SetActive(false);
            Refresh();
        }

        public void ShowAchievementsPanel()
        {
            if (contractPanelRoot != null)
            {
                contractPanelRoot.SetActive(false);
            }

            if (achievementsPanelRoot != null)
            {
                achievementsPanelRoot.SetActive(true);
            }

            Refresh();
        }

        public void ToggleAchievementsPanel()
        {
            bool shouldOpen = achievementsPanelRoot != null
                              && !achievementsPanelRoot.activeSelf;
            contractPanelRoot?.SetActive(false);
            achievementsPanelRoot?.SetActive(shouldOpen);
            Refresh();
        }

        public void Refresh()
        {
            RefreshContract();
            RefreshAchievements();
        }

        private void ClaimActiveContract()
        {
            progressionService?.TryClaimActiveContract();
            Refresh();
        }

        private void RefreshContract()
        {
            if (progressionService == null
                || !progressionService.HasActiveContract)
            {
                SetText(contractDescriptionText, "ALL CONTRACTS COMPLETE");
                SetText(contractProgressText, "NO ACTIVE CONTRACT");
                SetText(contractRewardText, string.Empty);
                SetText(contractStateText, "COMPLETED");
                if (claimButton != null)
                {
                    claimButton.gameObject.SetActive(false);
                }

                return;
            }

            LumberCampContractDefinition definition =
                LumberCampProgressionCatalog.GetContract(
                    progressionService.ActiveContractIndex);
            progressionService.GetActiveContractProgress(
                out long current,
                out long target);
            SetText(contractDescriptionText, definition.Description);
            SetText(contractProgressText, $"{current} / {target}");
            SetText(contractRewardText, $"REWARD: ${definition.RewardCash}");

            ContractProgressState state = progressionService.ActiveContractState;
            SetText(
                contractStateText,
                state == ContractProgressState.CompletedUnclaimed
                    ? "READY TO CLAIM"
                    : "ACTIVE");
            if (claimButton != null)
            {
                bool canClaim = state == ContractProgressState.CompletedUnclaimed;
                claimButton.gameObject.SetActive(canClaim);
                claimButton.interactable = canClaim;
            }
        }

        private void RefreshAchievements()
        {
            if (achievementsListText == null || progressionService == null)
            {
                return;
            }

            var builder = new StringBuilder(2048);
            for (int i = 0;
                 i < LumberCampProgressionCatalog.AchievementCount;
                 i++)
            {
                LumberCampAchievementDefinition definition =
                    LumberCampProgressionCatalog.GetAchievement(i);
                progressionService.GetAchievementProgress(
                    i,
                    out long current,
                    out long target);
                bool unlocked = progressionService.IsAchievementUnlocked(i);
                bool rewarded = progressionService.IsAchievementRewarded(i);
                string state = rewarded
                    ? "COMPLETED"
                    : unlocked
                        ? "UNLOCKED"
                        : "LOCKED";

                builder.Append('[').Append(state).Append("] ")
                    .Append(definition.Name)
                    .Append('\n')
                    .Append(definition.Requirement)
                    .Append(" — ")
                    .Append(current)
                    .Append(" / ")
                    .Append(target)
                    .Append(" — $")
                    .Append(definition.RewardCash);
                if (i + 1 < LumberCampProgressionCatalog.AchievementCount)
                {
                    builder.Append("\n\n");
                }
            }

            achievementsListText.text = builder.ToString();
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value ?? string.Empty;
            }
        }
    }
}
