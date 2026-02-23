using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class RangeOffsetAuthoring : MonoBehaviour
{
    public float MaxRange = 3f;      // Offset이 이동 가능한 최대 거리 (Movement Limit)
    public float AttackRadius = 2f;  // Offset 위치에서 타겟을 찾는 탐색 반경 (Targeting radius around offset)
    public Vector3 Offset;

    public class RangeOffsetBaker : Baker<RangeOffsetAuthoring>
    {
        public override void Bake(RangeOffsetAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new TowerRangeOffset 
            { 
                MaxRange = authoring.MaxRange, 
                AttackRadius = authoring.AttackRadius,
                Offset = authoring.Offset
            });
        }
    }
}
