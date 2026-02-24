using Unity.Entities;
using Unity.Mathematics;
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