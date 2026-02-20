using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class MonsterAuthoring : MonoBehaviour
{
    public float HP = 5f;
    public float Speed = 1f;

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
                CurrentTargetPos = float3.zero,
                HasTarget = false,
                Offset = float3.zero
            });
        }
    }
}

public struct MonsterData : IComponentData
{
    public float HP;
    public float MaxHP;
    public float Speed;

    // 이동 관련 상태 필드 통합
    public float3 CurrentTargetPos;
    public float3 Offset;
    public bool HasTarget;
}