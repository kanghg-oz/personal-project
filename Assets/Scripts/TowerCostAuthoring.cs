using Unity.Entities;
using UnityEngine;
using System.Collections.Generic;
using System;

[Serializable]
public struct UpgradeStep
{
    public int Cost;
    public float Value;
}

public class TowerCostAuthoring : MonoBehaviour
{
    public int BuildCost = 50;
    
    [Header("Damage Upgrades")]
    public List<UpgradeStep> DamageUpgrades = new List<UpgradeStep>();
    
    [Header("Range Upgrades")]
    public List<UpgradeStep> RangeUpgrades = new List<UpgradeStep>();

    public class TowerCostBaker : Baker<TowerCostAuthoring>
    {
        public override void Bake(TowerCostAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent(entity, new TowerCost
            {
                BuildCost = authoring.BuildCost,
                DamageUpgradeLevel = 0,
                RangeUpgradeLevel = 0
            });

            var damageBuffer = AddBuffer<DamageUpgradeStepElement>(entity);
            foreach (var step in authoring.DamageUpgrades)
            {
                damageBuffer.Add(new DamageUpgradeStepElement { Cost = step.Cost, Value = step.Value });
            }

            var rangeBuffer = AddBuffer<RangeUpgradeStepElement>(entity);
            foreach (var step in authoring.RangeUpgrades)
            {
                rangeBuffer.Add(new RangeUpgradeStepElement { Cost = step.Cost, Value = step.Value });
            }
        }
    }
}

[InternalBufferCapacity(10)]
public struct DamageUpgradeStepElement : IBufferElementData
{
    public int Cost;
    public float Value;
}

[InternalBufferCapacity(10)]
public struct RangeUpgradeStepElement : IBufferElementData
{
    public int Cost;
    public float Value;
}

