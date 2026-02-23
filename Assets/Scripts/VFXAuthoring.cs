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
            
            // Unity.Rendering.VisualEffectCompanionBaker가 이미 VisualEffect를 추가하므로
            // 여기서 중복으로 AddComponentObject를 호출하지 않습니다.
            // 대신 Duration 정보만 저장하는 컴포넌트를 추가할 수 있습니다. (필요한 경우)
        }
    }
}
