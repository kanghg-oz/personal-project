using Unity.Entities;
using Unity.Transforms;
using UnityEngine.VFX;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class TowerVFXSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // 최적화: 풀링 시점에 추가한 VFXScaleProperty를 딱 한 번만 _Scale 프로퍼티에 적용
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        
        foreach (var (scaleProp, entity) in SystemAPI.Query<RefRO<VFXScaleProperty>>().WithEntityAccess())
        {
            if (EntityManager.HasComponent<VisualEffect>(entity))
            {
                var vfx = EntityManager.GetComponentObject<VisualEffect>(entity);
                vfx.SetFloat("_Scale", scaleProp.ValueRO.Value);
                ecb.RemoveComponent<VFXScaleProperty>(entity);
            }
        }
        
        ecb.Playback(EntityManager);
        ecb.Dispose();

        foreach (var (requests, muzzleRequests) in SystemAPI.Query<
            DynamicBuffer<VFXPlayRequest>,
            DynamicBuffer<MuzzleVFXPlayRequest>>())
        {
            // 폭발 VFX 처리
            for (int i = 0; i < requests.Length; i++)
            {
                Entity vfxEntity = requests[i].VfxEntity;

                // Play VisualEffect
                if (EntityManager.HasComponent<VisualEffect>(vfxEntity))
                {
                    var localTransform = EntityManager.GetComponentData<LocalTransform>(vfxEntity);
                    var vfx = EntityManager.GetComponentObject<VisualEffect>(vfxEntity);
                    vfx.transform.position = localTransform.Position; // Sync GameObject transform immediately
                    vfx.Play();
                }
            }
            requests.Clear();

            // 발사 VFX 처리 (Direct 타입)
            for (int i = 0; i < muzzleRequests.Length; i++)
            {
                Entity muzzleEntity = muzzleRequests[i].VfxEntity;

                // Play VisualEffect
                if (EntityManager.HasComponent<VisualEffect>(muzzleEntity))
                {
                    var localTransform = EntityManager.GetComponentData<LocalTransform>(muzzleEntity);
                    var vfx = EntityManager.GetComponentObject<VisualEffect>(muzzleEntity);
                    vfx.transform.position = localTransform.Position; // Sync GameObject transform immediately
                    vfx.Play();
                }
            }
            muzzleRequests.Clear();
        }
    }
}
