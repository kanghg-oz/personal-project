using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

public class MonsterAuthoring : MonoBehaviour
{
    public int HP = 5;
    public float Speed = 1f;
    public int DamageToPlayer = 1;
    public int GoldReward = 10;

    public class MonsterBaker : Baker<MonsterAuthoring>
    {
        public override void Bake(MonsterAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new MonsterData
            {
                HP = authoring.HP,
                MaxHP = authoring.HP,
                Speed = authoring.Speed,
                DamageToPlayer = authoring.DamageToPlayer,
                GoldReward = authoring.GoldReward,
                CurrentTargetPos = float3.zero,
                HasTarget = false,
                Offset = float3.zero
            });

            AddComponent(entity, new MonsterMaterialProperties
            {
                LastHitTime = -100f,
                HPRatio = 1.0f,
                Unused1 = 0f,
                Unused2 = 0f
            });
        }
    }
}

public struct MonsterData : IComponentData
{
    public int HP;
    public int MaxHP;
    public float Speed;
    public int DamageToPlayer;
    public int GoldReward;

    // 이동 관련 상태 필드 통합
    public float3 CurrentTargetPos;
    public float3 Offset;
    public bool HasTarget;
}

[MaterialProperty("_HitTimeRatio")]
public struct MonsterMaterialProperties : IComponentData
{
    public float LastHitTime; // x: 피격 시각
    public float HPRatio;     // y: 체력 비율
    public float Unused1;     // z: 미사용
    public float Unused2;     // w: 미사용
}