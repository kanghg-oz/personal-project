using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class RangeAnnulusAuthoring : MonoBehaviour
{
    public float MaxRange = 5f;
    public float MinRange = 2f;

    public class RangeAnnulusBaker : Baker<RangeAnnulusAuthoring>
    {
        public override void Bake(RangeAnnulusAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new TowerRangeAnnulus { MaxRange = authoring.MaxRange, MinRange = authoring.MinRange });
        }
    }
}
