using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class RangeSectorAuthoring : MonoBehaviour
{
    public float MaxRange = 5f;
    public float Angle = 90f;

    public class RangeSectorBaker : Baker<RangeSectorAuthoring>
    {
        public override void Bake(RangeSectorAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new TowerRangeSector { MaxRange = authoring.MaxRange, Angle = authoring.Angle });
        }
    }
}
