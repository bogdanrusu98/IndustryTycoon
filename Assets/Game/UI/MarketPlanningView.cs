using System.Text;
using IndustryTycoon.Core;
using IndustryTycoon.Market;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class MarketPlanningView : MonoBehaviour
    {
        [SerializeField] private MarketPlanningService market;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text demandText;
        [SerializeField] private Text[] orderTexts;
        [SerializeField] private Button[] orderButtons;
        private readonly StringBuilder _builder = new StringBuilder(384);

        public GameObject PanelRoot => panelRoot;

        private void OnEnable()
        {
            if (market != null) market.StateChanged += Refresh;
            if (orderButtons != null)
                for (int i = 0; i < orderButtons.Length; i++)
                {
                    int index = i;
                    orderButtons[i]?.onClick.AddListener(() => HandleOrder(index));
                }
            Refresh();
        }

        private void OnDisable()
        {
            if (market != null) market.StateChanged -= Refresh;
        }

        public void Refresh()
        {
            bool unlocked = market != null && market.IsMarketUnlocked;
            if (demandText != null)
            {
                _builder.Clear();
                if (!unlocked) _builder.Append("MARKET UNLOCKS AT EMPIRE 20");
                else
                {
                    _builder.Append(market.IsFlowBonusActive ? "INDUSTRIAL FLOW  +5% MACHINE PART\n" : "INDUSTRIAL FLOW  INACTIVE\n");
                    for (int i = 0; i < M16MarketSaveData.ResourceCount; i++)
                    {
                        ResourceType type = (ResourceType)i;
                        DemandTier tier = market.GetDemandTier(type);
                        if (tier == DemandTier.Normal) continue;
                        _builder.Append(type.ToString().ToUpperInvariant()).Append("  ")
                            .Append(tier.ToString().ToUpperInvariant()).Append("  x")
                            .Append(M16MarketRules.GetDemandBasisPoints(tier) / 10000f).Append("\n");
                    }
                }
                demandText.text = _builder.ToString().TrimEnd();
            }

            M16ProductionOrderSaveRecord[] orders = market?.State?.orders;
            int count = orderTexts != null ? orderTexts.Length : 0;
            for (int i = 0; i < count; i++)
            {
                M16ProductionOrderSaveRecord order = orders != null && i < orders.Length ? orders[i] : null;
                Text text = orderTexts[i];
                Button button = orderButtons != null && i < orderButtons.Length ? orderButtons[i] : null;
                if (text != null)
                    text.text = order == null || !unlocked ? "ORDER LOCKED" : $"{order.resourceType.ToString().ToUpperInvariant()}  {order.quantity}\nWAREHOUSE {market.GetAvailableStock(order.resourceType)}  •  REWARD ${order.rewardCash}\n{order.state.ToString().ToUpperInvariant()}";
                if (button != null)
                {
                    button.interactable = unlocked && order != null && order.state != ProductionOrderState.Claimed;
                    Text label = button.GetComponentInChildren<Text>(true);
                    if (label != null) label.text = order == null ? "LOCKED" : order.state == ProductionOrderState.Available ? "ACTIVATE" : order.state == ProductionOrderState.Active ? "DELIVER" : order.state == ProductionOrderState.Completed ? "CLAIM" : "CLAIMED";
                }
            }
        }

        private void HandleOrder(int index)
        {
            M16ProductionOrderSaveRecord[] orders = market?.State?.orders;
            if (orders == null || index < 0 || index >= orders.Length || orders[index] == null) return;
            ProductionOrderState state = orders[index].state;
            if (state == ProductionOrderState.Available) market.TryActivateOrder(index);
            else if (state == ProductionOrderState.Active) market.TryDeliverActiveOrder();
            else if (state == ProductionOrderState.Completed) market.TryClaimOrder(index);
            Refresh();
        }
    }
}
