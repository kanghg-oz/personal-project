using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class RangeDefaultAuthoring : MonoBehaviour
{
    public float MaxRange = 5f;

    public class RangeDefaultBaker : Baker<RangeDefaultAuthoring>
    {
        public override void Bake(RangeDefaultAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new TowerRangeDefault { MaxRange = authoring.MaxRange });
        }
    }
}
