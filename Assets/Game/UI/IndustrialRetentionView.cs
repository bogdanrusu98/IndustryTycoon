using System.Text;
using IndustryTycoon.Core;
using IndustryTycoon.Logistics;
using IndustryTycoon.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class IndustrialRetentionView : MonoBehaviour
    {
        [SerializeField] private CentralWarehouse warehouse;
        [SerializeField] private IndustrialLogisticsNetwork logisticsNetwork;
        [SerializeField] private RetentionGoalService goalService;
        [SerializeField] private LumberCampProgressionService progressionService;

        [Header("Portrait Panels")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button toggleButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Text warehouseText;
        [SerializeField] private Text routesText;
        [SerializeField] private Text goalsText;
        [SerializeField] private Text empireText;
        [SerializeField] private Button firstRouteToggleButton;
        [SerializeField] private Button secondRouteToggleButton;
        [SerializeField] private Button thirdRouteToggleButton;
        [SerializeField] private Button claimDailyButton;
        [SerializeField] private Button claimWeeklyButton;

        private void OnEnable()
        {
            toggleButton?.onClick.AddListener(Toggle);
            closeButton?.onClick.AddListener(Close);
            firstRouteToggleButton?.onClick.AddListener(ToggleFirstRoute);
            secondRouteToggleButton?.onClick.AddListener(ToggleSecondRoute);
            thirdRouteToggleButton?.onClick.AddListener(ToggleThirdRoute);
            claimDailyButton?.onClick.AddListener(ClaimFirstDaily);
            claimWeeklyButton?.onClick.AddListener(ClaimFirstWeekly);
            Subscribe(true);
            Refresh();
        }

        private void OnDisable()
        {
            toggleButton?.onClick.RemoveListener(Toggle);
            closeButton?.onClick.RemoveListener(Close);
            firstRouteToggleButton?.onClick.RemoveListener(ToggleFirstRoute);
            secondRouteToggleButton?.onClick.RemoveListener(ToggleSecondRoute);
            thirdRouteToggleButton?.onClick.RemoveListener(ToggleThirdRoute);
            claimDailyButton?.onClick.RemoveListener(ClaimFirstDaily);
            claimWeeklyButton?.onClick.RemoveListener(ClaimFirstWeekly);
            Subscribe(false);
        }

        public void Toggle()
        {
            panelRoot?.SetActive(!panelRoot.activeSelf);
            Refresh();
        }

        public void Close()
        {
            panelRoot?.SetActive(false);
        }

        public void Refresh()
        {
            RefreshWarehouse();
            RefreshRoutes();
            RefreshGoals();
            if (empireText != null)
            {
                int level = progressionService != null
                    ? progressionService.EmpireLevel
                    : 1;
                empireText.text = level >= 10
                    ? "EMPIRE 10 — INDUSTRIAL DISTRICT READY"
                    : $"EMPIRE {level} / 10";
            }
        }

        private void RefreshWarehouse()
        {
            if (warehouseText == null || warehouse == null)
            {
                return;
            }

            var builder = new StringBuilder(256);
            builder.Append("CENTRAL WAREHOUSE");
            for (int i = 0; i < 6; i++)
            {
                ResourceType resource = (ResourceType)i;
                builder.Append('\n')
                    .Append(resource)
                    .Append(": ")
                    .Append(warehouse.GetAmount(resource))
                    .Append(" / ")
                    .Append(warehouse.GetCapacity(resource));
            }

            warehouseText.text = builder.ToString();
        }

        private void RefreshRoutes()
        {
            if (routesText == null)
            {
                return;
            }

            var builder = new StringBuilder(256);
            builder.Append("INDUSTRIAL ROUTES");
            IndustrialLogisticsRoute[] routes = logisticsNetwork?.Routes;
            if (routes != null)
            {
                for (int i = 0; i < routes.Length; i++)
                {
                    IndustrialLogisticsRoute route = routes[i];
                    if (route == null)
                    {
                        continue;
                    }

                    builder.Append('\n')
                        .Append(route.SourceEndpointId)
                        .Append(" → ")
                        .Append(route.ResourceType)
                        .Append(" → ")
                        .Append(route.DestinationEndpointId)
                        .Append(" [")
                        .Append(route.State)
                        .Append(']');
                }
            }

            routesText.text = builder.ToString();
        }

        private void RefreshGoals()
        {
            if (goalsText == null)
            {
                return;
            }

            var builder = new StringBuilder(512);
            AppendPeriod(builder, "DAILY", goalService?.Daily);
            builder.Append("\n\n");
            AppendPeriod(builder, "WEEKLY", goalService?.Weekly);
            goalsText.text = builder.ToString();
        }

        private static void AppendPeriod(
            StringBuilder builder,
            string title,
            M13GoalPeriodSaveData period)
        {
            builder.Append(title);
            if (period?.goals == null)
            {
                builder.Append(" — UNAVAILABLE");
                return;
            }

            for (int i = 0; i < period.goals.Length; i++)
            {
                M13GoalSaveRecord goal = period.goals[i];
                if (goal == null)
                {
                    continue;
                }

                builder.Append('\n')
                    .Append(goal.claimed ? "[CLAIMED] " : goal.completed ? "[READY] " : "")
                    .Append(goal.type)
                    .Append(' ')
                    .Append(goal.type == RetentionGoalType.EarnCash
                        ? "Cash"
                        : goal.resourceType.ToString())
                    .Append(" — ")
                    .Append(goal.progress)
                    .Append(" / ")
                    .Append(goal.target)
                    .Append(" — $")
                    .Append(goal.rewardCash);
            }
        }

        private void ToggleFirstRoute() => ToggleRoute(0);
        private void ToggleSecondRoute() => ToggleRoute(1);
        private void ToggleThirdRoute() => ToggleRoute(2);

        private void ToggleRoute(int index)
        {
            IndustrialLogisticsRoute[] routes = logisticsNetwork?.Routes;
            if (routes == null || index < 0 || index >= routes.Length || routes[index] == null)
            {
                return;
            }

            routes[index].SetRouteEnabled(!routes[index].RouteEnabled);
            Refresh();
        }

        private void ClaimFirstDaily()
        {
            ClaimFirstReady(RetentionGoalPeriod.Daily, goalService?.Daily);
        }

        private void ClaimFirstWeekly()
        {
            ClaimFirstReady(RetentionGoalPeriod.Weekly, goalService?.Weekly);
        }

        private void ClaimFirstReady(
            RetentionGoalPeriod kind,
            M13GoalPeriodSaveData period)
        {
            if (period?.goals == null)
            {
                return;
            }

            for (int i = 0; i < period.goals.Length; i++)
            {
                M13GoalSaveRecord goal = period.goals[i];
                if (goal != null && goal.completed && !goal.claimed)
                {
                    goalService?.TryClaim(kind, i);
                    break;
                }
            }

            Refresh();
        }

        private void Subscribe(bool subscribe)
        {
            if (warehouse != null)
            {
                if (subscribe) warehouse.StateChanged += Refresh;
                else warehouse.StateChanged -= Refresh;
            }

            if (logisticsNetwork != null)
            {
                if (subscribe) logisticsNetwork.StateChanged += Refresh;
                else logisticsNetwork.StateChanged -= Refresh;
            }

            if (goalService != null)
            {
                if (subscribe) goalService.StateChanged += Refresh;
                else goalService.StateChanged -= Refresh;
            }

            if (progressionService != null)
            {
                if (subscribe) progressionService.StateChanged += Refresh;
                else progressionService.StateChanged -= Refresh;
            }
        }
    }
}
