using System.Text;
using IndustryTycoon.Core;
using IndustryTycoon.Market;
using UnityEngine;

namespace IndustryTycoon.UI
{
    public sealed class MarketPlanningView : MonoBehaviour
    {
        [SerializeField] private MarketPlanningService market;
        [SerializeField] private bool visible = true;
        private readonly StringBuilder _text = new StringBuilder(256);

        private void OnGUI()
        {
            if (!visible || market == null || !market.IsMarketUnlocked) return;
            GUILayout.BeginArea(new Rect(Screen.width - 330f, 92f, 315f, 440f), GUI.skin.box);
            GUILayout.Label("MARKET DEMAND & ORDERS");
            for (int i = 0; i < M16MarketSaveData.ResourceCount; i++)
            {
                ResourceType type = (ResourceType)i;
                DemandTier tier = market.GetDemandTier(type);
                if (tier == DemandTier.Normal) continue;
                GUILayout.Label($"{type}: {tier} x{M16MarketRules.GetDemandBasisPoints(tier) / 10000f:0.00}");
            }
            GUILayout.Label(market.IsFlowBonusActive ? "Industrial Flow: +5% Machine Part" : "Industrial Flow: inactive");
            var orders = market.State?.orders;
            if (orders != null)
            {
                for (int i = 0; i < orders.Length; i++)
                {
                    var order = orders[i];
                    if (order == null) continue;
                    _text.Clear();
                    _text.Append(order.resourceType).Append(" ").Append(order.quantity)
                        .Append("  $").Append(order.rewardCash).Append("  ").Append(order.state)
                        .Append("  Stock ").Append(market.GetAvailableStock(order.resourceType));
                    GUILayout.Label(_text.ToString());
                    if (order.state == ProductionOrderState.Available && GUILayout.Button($"Activate order {i + 1}"))
                        market.TryActivateOrder(i);
                    else if (order.state == ProductionOrderState.Active && GUILayout.Button("Deliver from Warehouse"))
                        market.TryDeliverActiveOrder();
                    else if (order.state == ProductionOrderState.Completed && GUILayout.Button("Claim reward"))
                        market.TryClaimOrder(i);
                }
            }
            GUILayout.EndArea();
        }
    }
}
