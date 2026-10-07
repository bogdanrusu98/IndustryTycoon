using System;
using System.Collections.Generic;
using IndustryTycoon.Processing;
namespace IndustryTycoon.Persistence
{
 public enum M15StrategyContractKind { EfficiencyCycles=0, UpgradedRouteThroughput=1, SpecializedFactories=2, ClearOutputBottleneck=3 }
 [Serializable] public sealed class M15FactoryRecord { public string buildingId; public FactorySpecialization specialization; public int efficiencyCompletedRemainder; public long completedSpecializedCycles; }
 [Serializable] public sealed class M15FactorySaveData
 {
  public int industrialDistrictLevel=1; public bool optimizationObjectiveCompleted; public long upgradedRouteUnitsTransported; public M15FactoryRecord[] factories;
  public static M15FactorySaveData CreateFresh()=>new M15FactorySaveData{factories=new[]{New("wood_processor"),New("iron_smelter"),New("tool_workshop"),New("assembly_plant")}};
  private static M15FactoryRecord New(string id)=>new M15FactoryRecord{buildingId=id};
  public M15FactoryRecord GetFactory(string id){if(factories!=null)for(int i=0;i<factories.Length;i++)if(factories[i]!=null&&string.Equals(factories[i].buildingId,id,StringComparison.Ordinal))return factories[i];return null;}
 }
 public static class M15FactoryRules
 {
  public const int MaximumEmpireLevel=20;
  public static bool TryChoose(M15FactorySaveData data,string id,FactorySpecialization choice){var r=data?.GetFactory(id);if(r==null||choice==FactorySpecialization.None||r.specialization!=FactorySpecialization.None||!M15FactoryTuning.TryGet(id,out _))return false;r.specialization=choice;RecalculateDistrictLevel(data);return true;}
  public static bool CompleteRecipe(M15FactoryRecord r,out bool conserve){conserve=false;if(r==null||r.specialization==FactorySpecialization.None)return false;r.completedSpecializedCycles=Math.Min(long.MaxValue,r.completedSpecializedCycles+1);if(r.specialization==FactorySpecialization.Efficiency&&++r.efficiencyCompletedRemainder>=5){r.efficiencyCompletedRemainder=0;conserve=true;}return true;}
  public static bool CompleteRecipes(M15FactoryRecord r,int count,out int conserved){conserved=0;if(r==null||count<=0||r.specialization==FactorySpecialization.None)return false;r.completedSpecializedCycles=Math.Min(long.MaxValue,r.completedSpecializedCycles+(long)count);if(r.specialization==FactorySpecialization.Efficiency){long total=r.efficiencyCompletedRemainder+(long)count;conserved=(int)Math.Min(int.MaxValue,total/5L);r.efficiencyCompletedRemainder=(int)(total%5L);}return true;}
  public static int GetEmpireLevel(M15FactorySaveData d){if(d?.factories==null)return 15;int c=GetSpecializedFactoryCount(d);int l=c>0?16:15;if(c>=2)l=17;if(l>=17&&d.industrialDistrictLevel>=2)l=18;if(l>=18&&d.optimizationObjectiveCompleted)l=19;if(l>=19&&d.industrialDistrictLevel>=3&&d.upgradedRouteUnitsTransported>=50)l=20;return l;}
  public static int GetSpecializedFactoryCount(M15FactorySaveData d){int count=0;if(d?.factories==null)return count;for(int i=0;i<d.factories.Length;i++)if(d.factories[i]!=null&&d.factories[i].specialization!=FactorySpecialization.None)count++;return count;}
  public static long GetEfficiencyCycleCount(M15FactorySaveData d){long count=0;if(d?.factories==null)return count;for(int i=0;i<d.factories.Length;i++){var r=d.factories[i];if(r==null||r.specialization!=FactorySpecialization.Efficiency)continue;count=count>long.MaxValue-r.completedSpecializedCycles?long.MaxValue:count+r.completedSpecializedCycles;}return count;}
  public static long GetSpecializedCycleCount(M15FactorySaveData d){long count=0;if(d?.factories==null)return count;for(int i=0;i<d.factories.Length;i++){var r=d.factories[i];if(r==null||r.specialization==FactorySpecialization.None)continue;count=count>long.MaxValue-r.completedSpecializedCycles?long.MaxValue:count+r.completedSpecializedCycles;}return count;}
 public static void RecalculateDistrictLevel(M15FactorySaveData d){if(d==null)return;int c=0,a=0;for(int i=0;i<d.factories.Length;i++){var r=d.factories[i];if(r.specialization!=FactorySpecialization.None)c++;if(r.completedSpecializedCycles>=25)a++;}d.industrialDistrictLevel=c==0?1:a>=2&&c>=3?3:2;}
  public static bool IsContractEligible(M15FactorySaveData d,M15StrategyContractKind kind)
  { if(d?.factories==null)return false;int specialized=GetSpecializedFactoryCount(d),efficient=0;for(int i=0;i<d.factories.Length;i++)if(d.factories[i]!=null&&d.factories[i].specialization==FactorySpecialization.Efficiency)efficient++;switch(kind){case M15StrategyContractKind.EfficiencyCycles:return efficient>0;case M15StrategyContractKind.UpgradedRouteThroughput:return d.industrialDistrictLevel>=2;case M15StrategyContractKind.SpecializedFactories:return specialized>=2&&specialized<d.factories.Length;case M15StrategyContractKind.ClearOutputBottleneck:return specialized>0;default:return false;} }
 }
 public static class M15FactorySaveValidator
 {
  public static bool TryNormalize(M15FactorySaveData source,out M15FactorySaveData normalized,out string failure){normalized=null;failure=null;if(source?.factories==null||source.factories.Length!=4){failure="M15 factory state has an invalid canonical shape.";return false;}normalized=M15FactorySaveData.CreateFresh();var seen=new HashSet<string>(StringComparer.Ordinal);for(int i=0;i<source.factories.Length;i++){var s=source.factories[i];if(s==null||!M15FactoryTuning.TryGet(s.buildingId,out _)||!seen.Add(s.buildingId)||(int)s.specialization<0||(int)s.specialization>2){failure="M15 factory IDs or specialization values are invalid.";normalized=null;return false;}var t=normalized.GetFactory(s.buildingId);t.specialization=s.specialization;t.efficiencyCompletedRemainder=s.specialization==FactorySpecialization.Efficiency?Math.Max(0,Math.Min(4,s.efficiencyCompletedRemainder)):0;t.completedSpecializedCycles=Math.Max(0,s.completedSpecializedCycles);}normalized.optimizationObjectiveCompleted=source.optimizationObjectiveCompleted;normalized.upgradedRouteUnitsTransported=Math.Max(0,source.upgradedRouteUnitsTransported);M15FactoryRules.RecalculateDistrictLevel(normalized);return true;}
 }
}
