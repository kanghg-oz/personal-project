using Unity.Entities;
using UnityEngine;
using UnityEngine.VFX;

public class VFXAuthoring : MonoBehaviour
{
    public float Duration = 1.0f;

    public class VFXBaker : Baker<VFXAuthoring>
    {
        public override void Bake(VFXAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
        }
    }
}
