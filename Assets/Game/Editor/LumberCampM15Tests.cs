using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;
using NUnit.Framework;
using UnityEngine;

namespace IndustryTycoon.EditorTests
{
    public sealed class LumberCampM15Tests
    {
        [Test] public void Specialization_IsMutuallyExclusive_AndUnlocksDistrictTwo()
        {
            var data=M15FactorySaveData.CreateFresh();
            Assert.IsTrue(M15FactoryRules.TryChoose(data,"wood_processor",FactorySpecialization.Throughput));
            Assert.IsFalse(M15FactoryRules.TryChoose(data,"wood_processor",FactorySpecialization.Efficiency));
            Assert.AreEqual(2,data.industrialDistrictLevel); Assert.AreEqual(16,M15FactoryRules.GetEmpireLevel(data));
        }

        [Test] public void Timing_StacksUpgradeSpecializationAndDistrictDeterministically()
        {
            Assert.That(M15FactoryTuning.CalculateDuration(10f,2,FactorySpecialization.Throughput,"wood_processor",2),Is.EqualTo(5.32f).Within(.001f));
            Assert.That(M15FactoryTuning.CalculateRouteInterval(10f,2,3),Is.EqualTo(5.76f).Within(.001f));
        }

        [Test] public void Efficiency_ConservesExactlyOnceEveryFiveCompletedRecipes()
        {
            var r=new M15FactoryRecord{buildingId="wood_processor",specialization=FactorySpecialization.Efficiency};
            for(int i=0;i<4;i++){Assert.IsTrue(M15FactoryRules.CompleteRecipe(r,out bool c));Assert.IsFalse(c);}
            M15FactoryRules.CompleteRecipe(r,out bool conserved); Assert.IsTrue(conserved); Assert.AreEqual(0,r.efficiencyCompletedRemainder);
        }

        [Test] public void Validator_PreservesMidCounter_AndResetIsClean()
        {
            var d=M15FactorySaveData.CreateFresh();var r=d.GetFactory("iron_smelter");r.specialization=FactorySpecialization.Efficiency;r.efficiencyCompletedRemainder=3;
            Assert.IsTrue(M15FactorySaveValidator.TryNormalize(d,out var n,out _));Assert.AreEqual(3,n.GetFactory("iron_smelter").efficiencyCompletedRemainder);
            Assert.AreEqual(1,M15FactorySaveData.CreateFresh().industrialDistrictLevel);
        }

        [TestCase(false,true,false,true,true,false,ProductionStatus.LockedInactive)]
        [TestCase(true,true,false,false,true,false,ProductionStatus.MissingInput)]
        [TestCase(true,true,false,true,false,false,ProductionStatus.OutputFull)]
        [TestCase(true,true,false,true,false,true,ProductionStatus.WaitingForLogistics)]
        [TestCase(true,true,true,false,false,false,ProductionStatus.Running)]
        public void BottleneckStatus_IsDerived(bool unlocked,bool active,bool running,bool input,bool space,bool logistics,ProductionStatus expected)
        { Assert.AreEqual(expected,ProductionStatusRules.Derive(unlocked,active,running,input,space,logistics)); }

        [Test] public void V6_MigratesToV7_Idempotently()
        {
            var d=M9SaveData.CreateFresh(1000);d.version=6;string json=JsonUtility.ToJson(d);
            var migrated=M9SaveCodec.Decode(json,M9SaveValidationSettings.CreateDefault(),1000);
            Assert.IsTrue(migrated.IsSuccess,migrated.Diagnostic);Assert.IsTrue(migrated.WasMigrated);Assert.AreEqual(7,migrated.Data.version);
            Assert.IsTrue(M9SaveCodec.TryEncode(migrated.Data,M9SaveValidationSettings.CreateDefault(),1000,out string current,out _,out _));
            var decoded=M9SaveCodec.Decode(current,M9SaveValidationSettings.CreateDefault(),1000);Assert.IsTrue(decoded.IsSuccess);Assert.IsFalse(decoded.WasMigrated);
        }

        [Test] public void EmpireLevels_AreSequentialAndNonSkipping()
        {
            var d=M15FactorySaveData.CreateFresh();Assert.AreEqual(15,M15FactoryRules.GetEmpireLevel(d));
            M15FactoryRules.TryChoose(d,"wood_processor",FactorySpecialization.Throughput);Assert.AreEqual(16,M15FactoryRules.GetEmpireLevel(d));
            M15FactoryRules.TryChoose(d,"iron_smelter",FactorySpecialization.Efficiency);Assert.AreEqual(18,M15FactoryRules.GetEmpireLevel(d));
            d.optimizationObjectiveCompleted=true;Assert.AreEqual(19,M15FactoryRules.GetEmpireLevel(d));
            d.industrialDistrictLevel=3;d.upgradedRouteUnitsTransported=50;Assert.AreEqual(20,M15FactoryRules.GetEmpireLevel(d));
        }
    }
}
