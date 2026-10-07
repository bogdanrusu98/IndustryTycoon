using System;
using System.Collections.Generic;
using System.Reflection;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Logistics;
using IndustryTycoon.Market;
using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using NUnit.Framework;
using UnityEngine;

namespace IndustryTycoon.EditorTests
{
    public sealed class LumberCampM16Tests
    {
        [Test] public void PricePipeline_StacksDemandAndFlowOnce() => TestPrices();
        [Test] public void Rotation_IsDeterministicPersistentCappedAndFiltersLocks() => TestRotation();
        [Test] public void Orders_AreDeterministicPremiumAndStateSafe() => TestOrders();
        [Test] public void OrderDelivery_ConsumesWarehouseAndPaysOnlyOnClaim() => TestOrderTransaction();
        [Test] public void FlowBonus_IsOrderedNonStackingExpiringAndPersistent() => TestFlow();
        [Test] public void Goals_AreMarketGated() => TestGoals();
        [Test] public void Empire21To25_IsSequentialOnceOnlyAndRestoresSilently() => TestEmpire();
        [Test] public void Schema7_MigratesExactlyOnceAndFreshResetClearsM16() => TestPersistence();

        public static void RunAllForValidator()
        {
            TestPrices(); TestRotation(); TestOrders(); TestOrderTransaction(); TestFlow();
            TestGoals(); TestEmpire(); TestPersistence();
        }

        private static void TestPrices()
        {
            Require(M16MarketRules.CalculateSaleValue(ResourceType.Wood, DemandTier.Normal, false) == 5
                    && M16MarketRules.CalculateSaleValue(ResourceType.Tool, DemandTier.High, false) == 115
                    && M16MarketRules.CalculateSaleValue(ResourceType.MachinePart, DemandTier.Boom, false) == 325
                    && M16MarketRules.CalculateSaleValue(ResourceType.MachinePart, DemandTier.Boom, true) == 341,
                "Demand/Flow price stacking did not use the canonical integer pipeline.");
            Require(M16MarketRules.GetBaseValue(ResourceType.MachinePart) > M16MarketRules.GetBaseValue(ResourceType.Tool),
                "Machine Parts are no longer the highest-value manufactured resource.");
        }

        private static void TestRotation()
        {
            long now = 1800000000L;
            var a = M16MarketSaveData.CreateFresh(now);
            var b = M16MarketSaveData.CreateFresh(now);
            a.seed = b.seed = 1234567; a.rotationIndex = b.rotationIndex = 9;
            var unlocks = new MarketUnlockContext((1 << (int)ResourceType.Wood) | (1 << (int)ResourceType.Plank)
                                                  | (1 << (int)ResourceType.IronBar));
            M16MarketRules.RotateDemand(a, unlocks); M16MarketRules.RotateDemand(b, unlocks);
            int high = 0, boom = 0;
            for (int i = 0; i < M16MarketSaveData.ResourceCount; i++)
            {
                Require(a.demand[i].tier == b.demand[i].tier, "Equal market state rerolled differently.");
                if (a.demand[i].tier == DemandTier.High) high++;
                if (a.demand[i].tier == DemandTier.Boom) boom++;
                if (!unlocks.IsUnlocked((ResourceType)i)) Require(a.demand[i].tier == DemandTier.Normal,
                    "Locked resource received elevated demand.");
            }
            Require(high <= 2 && boom <= 1, "Demand caps were exceeded.");
            a.nextRotationUtcUnixSeconds = now + 300;
            Require(M16MarketRules.Advance(a, now + 901, 300, unlocks)
                    && a.rotationIndex == 12 && !M16MarketRules.Advance(a, now + 901, 300, unlocks),
                "Offline rotation repeated or failed to consume the elapsed interval once.");
            Require(M16MarketSaveValidator.TryNormalize(a, now + 901, out var saved, out _)
                    && saved.rotationIndex == a.rotationIndex
                    && saved.nextRotationUtcUnixSeconds == a.nextRotationUtcUnixSeconds,
                "Save/load changed the deterministic market anchor.");
        }

        private static void TestOrders()
        {
            var state = M16MarketSaveData.CreateFresh(1800000000L); state.seed = 42;
            var unlocks = new MarketUnlockContext((1 << (int)ResourceType.Wood) | (1 << (int)ResourceType.Tool));
            M16MarketRules.RefillOrders(state, unlocks);
            for (int i = 0; i < state.orders.Length; i++)
            {
                var order = state.orders[i];
                Require(unlocks.IsUnlocked(order.resourceType), "Order used a locked resource.");
                int baseTotal = M16MarketRules.GetBaseValue(order.resourceType) * order.quantity;
                Require(order.rewardCash * 100 >= baseTotal * 115 && order.rewardCash * 100 <= baseTotal * 131,
                    "Order reward is outside the 15-30% target (allowing integer rounding). ");
            }
            Require(M16MarketRules.TryActivateOrder(state, 0)
                    && !M16MarketRules.TryActivateOrder(state, 1)
                    && !M16MarketRules.TryTransitionOrder(state.orders[0], ProductionOrderState.Completed, ProductionOrderState.Claimed)
                    && M16MarketRules.TryTransitionOrder(state.orders[0], ProductionOrderState.Active, ProductionOrderState.Completed)
                    && M16MarketRules.TryTransitionOrder(state.orders[0], ProductionOrderState.Completed, ProductionOrderState.Claimed)
                    && !M16MarketRules.TryTransitionOrder(state.orders[0], ProductionOrderState.Completed, ProductionOrderState.Claimed),
                "Production Order activation/completion/claim was not exactly-once.");
            Require(state.orders[0].rewardCash > 0 && state.orders[0].state == ProductionOrderState.Claimed,
                "Order delivery state could be paid as a normal sale and order simultaneously.");
        }

        private static void TestFlow()
        {
            var state = M16MarketSaveData.CreateFresh(1800000000L);
            Require(!M16MarketRules.RecordFlowProduction(state, IndustrialFlowStep.Tool, 1800000010L, 180, 180, out _),
                "Flow accepted Tool without authoritative Iron Bar activity.");
            Require(M16MarketRules.RecordFlowProduction(state, IndustrialFlowStep.IronBar, 1800000010L, 180, 180, out _)
                    && M16MarketRules.RecordFlowProduction(state, IndustrialFlowStep.Tool, 1800000020L, 180, 180, out _)
                    && M16MarketRules.RecordFlowProduction(state, IndustrialFlowStep.MachinePart, 1800000030L, 180, 180, out bool first)
                    && first && state.flowBonusExpiresUtc == 1800000210L,
                "Legitimate Industrial Flow chain did not trigger.");
            Require(M16MarketRules.RecordFlowProduction(state, IndustrialFlowStep.MachinePart, 1800000040L, 180, 180, out bool stacked)
                    && !stacked && state.flowBonusExpiresUtc == 1800000220L,
                "Flow Bonus stacked or refreshed non-deterministically.");
            Require(M16MarketSaveValidator.TryNormalize(state, 1800000040L, out var copy, out _)
                    && copy.flowBonusExpiresUtc == state.flowBonusExpiresUtc
                    && 1800000220L >= copy.flowBonusExpiresUtc,
                "Flow expiry did not persist deterministically.");
        }

        private static void TestOrderTransaction()
        {
            GameObject root = new GameObject("M16 order transaction test");
            try
            {
                var warehouse = root.AddComponent<CentralWarehouse>();
                var wallet = root.AddComponent<Wallet>();
                var progression = root.AddComponent<LumberCampProgressionService>();
                var market = root.AddComponent<MarketPlanningService>();
                SetField(market, "warehouse", warehouse);
                SetField(market, "wallet", wallet);
                SetField(market, "progressionService", progression);
                int[] amounts = new int[M16MarketSaveData.ResourceCount];
                int[] capacities = new int[M16MarketSaveData.ResourceCount];
                for (int i = 0; i < capacities.Length; i++) capacities[i] = 60;
                amounts[(int)ResourceType.Tool] = 5;
                Require(warehouse.RestoreStableState(amounts, capacities), "Warehouse fixture was invalid.");
                var state = M16MarketSaveData.CreateFresh(1800000000L);
                state.orders[0] = new M16ProductionOrderSaveRecord
                {
                    id = 1, resourceType = ResourceType.Tool, quantity = 5,
                    rewardCash = 625, state = ProductionOrderState.Active
                };
                market.RestorePersistentState(state);
                Require(market.TryDeliverActiveOrder()
                        && warehouse.GetAmount(ResourceType.Tool) == 0
                        && wallet.Balance == 0
                        && market.State.orders[0].state == ProductionOrderState.Completed,
                    "Order delivery did not atomically consume stock without normal-sale Cash.");
                Require(market.TryClaimOrder(0) && wallet.Balance == 625
                        && !market.TryClaimOrder(0) && wallet.Balance == 625,
                    "Order claim reward was duplicated or paid before claim.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestGoals()
        {
            bool foundMarket = false;
            for (long day = 21000; day < 21020; day++)
            {
                var model = new RetentionGoalModel(M13RetentionSaveData.CreateFresh(day * 86400L), _ => 0, _ => true, null);
                model.Evaluate(day * 86400L, new GoalUnlockContext(true, true, true, true, true, true, true, true));
                foreach (var goal in model.Daily.goals)
                    foundMarket |= goal.type >= RetentionGoalType.CompleteProductionOrder;
            }
            Require(foundMarket, "Market-unlocked Daily/Weekly candidate pool never produced an M16 goal.");
            var locked = new RetentionGoalModel(M13RetentionSaveData.CreateFresh(22000L * 86400L), _ => 0, _ => true, null);
            locked.Evaluate(22000L * 86400L, new GoalUnlockContext(true, true, true, true, true, true, true, false));
            foreach (var goal in locked.Daily.goals)
                Require(goal.type < RetentionGoalType.CompleteProductionOrder, "M16 goal generated before Market unlock.");
        }

        private static void TestEmpire()
        {
            M10ProgressionSaveData progression = M10ProgressionSaveData.CreateFresh();
            progression.flags[(int)ProgressFlagId.LumberCampCompleted].value = true;
            foreach (ProgressMetricId metric in new[] { ProgressMetricId.DrillUnlocked, ProgressMetricId.ToolWorkshopUnlocked,
                         ProgressMetricId.ToolsProduced, ProgressMetricId.WarehouseUnlocked, ProgressMetricId.IndustrialRouteOperational,
                         ProgressMetricId.DailyGoalsCompleted, ProgressMetricId.CrossIndustryLogisticsContractsCompleted,
                         ProgressMetricId.IndustrialDistrictReady, ProgressMetricId.IndustrialDistrictEntered,
                         ProgressMetricId.AssemblyPlantUnlocked, ProgressMetricId.MachinePartsProduced,
                         ProgressMetricId.ProductionUpgradesPurchased, ProgressMetricId.AdvancedManufacturingContractsCompleted })
                progression.metrics[(int)metric].value = 1;
            var model = new LumberCampProgressionModel(progression, _ => true);
            var industrial = M14IndustrialSaveData.CreateFresh();
            var factory = M15FactorySaveData.CreateFresh();
            M15FactoryRules.TryChoose(factory, "wood_processor", FactorySpecialization.Throughput);
            M15FactoryRules.TryChoose(factory, "iron_smelter", FactorySpecialization.Efficiency);
            M15FactoryRules.TryChoose(factory, "tool_workshop", FactorySpecialization.Throughput);
            factory.optimizationObjectiveCompleted = true;
            factory.upgradedRouteUnitsTransported = 50;
            factory.GetFactory("wood_processor").completedSpecializedCycles = 25;
            factory.GetFactory("iron_smelter").completedSpecializedCycles = 25;
            M15FactoryRules.RecalculateDistrictLevel(factory);
            model.UpdateIndustrialContext(industrial, factory, false);
            var events = new List<int>(); model.EmpireLevelAdvanced += events.Add;
            model.RecordMarketUnlocked(); model.RecordProductionOrderCompleted(false);
            model.RecordMarketSale(DemandTier.High, 1); model.RecordFlowBonusTriggered();
            model.RecordProductionOrderCompleted(true);
            Require(model.EmpireLevel == 25 && string.Join(",", events) == "21,22,23,24,25",
                "Empire 21-25 was not sequential and once-only: " + string.Join(",", events));
            var restored = new LumberCampProgressionModel(model.CapturePersistentState(), _ => true);
            restored.UpdateIndustrialContext(industrial, factory, false);
            var replay = new List<int>(); restored.EmpireLevelAdvanced += replay.Add; restored.EvaluateAll();
            Require(restored.EmpireLevel == 25 && replay.Count == 0, "Empire 21-25 replayed on load.");
        }

        private static void TestPersistence()
        {
            long now = 1800000000L;
            M9SaveData legacy = M9SaveData.CreateFresh(now); legacy.version = M9SaveSchema.Version7;
            Array.Resize(ref legacy.progression.metrics, 39); legacy.market = null;
            string json = JsonUtility.ToJson(legacy);
            M9SaveDecodeResult migrated = M9SaveCodec.Decode(json, M9SaveValidationSettings.CreateDefault(), now);
            Require(migrated.IsSuccess && migrated.WasMigrated && migrated.Data.version == M9SaveSchema.Version8
                    && migrated.Data.market != null && migrated.Data.progression.metrics.Length == 47,
                "v7 -> v8 migration was not complete and canonical.");
            string v8 = JsonUtility.ToJson(migrated.Data);
            M9SaveDecodeResult reloaded = M9SaveCodec.Decode(v8, M9SaveValidationSettings.CreateDefault(), now);
            Require(reloaded.IsSuccess && !reloaded.WasMigrated, "v8 save migrated more than once.");
            M9SaveData reset = M9SaveData.CreateFresh(now);
            Require(reset.market.rotationIndex == 0 && reset.market.orderSequence == 0,
                "Fresh/reset save retained M16 state.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Missing test field: " + name);
            field.SetValue(target, value);
        }
    }
}
