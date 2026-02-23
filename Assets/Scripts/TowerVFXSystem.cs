using Unity.Entities;
using Unity.Transforms;
using UnityEngine.VFX;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class TowerVFXSystem : SystemBase
{
    protected override void OnUpdate()
    {
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
