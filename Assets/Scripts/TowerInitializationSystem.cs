using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[BurstCompile]
public partial struct TowerInitializationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        // 아직 초기화되지 않은(버퍼가 비어있는) 타워를 찾습니다.
        foreach (var (tower, transform, bulletBuffer, vfxBuffer, entity) in SystemAPI.Query<RefRO<TowerData>, RefRO<LocalTransform>, DynamicBuffer<TowerBulletElement>, DynamicBuffer<TowerVFXElement>>().WithEntityAccess())
        {
            if (bulletBuffer.Length == 0 && tower.ValueRO.BulletPrefab != Entity.Null)
            {
                for (int i = 0; i < tower.ValueRO.PoolSize; i++)
                {
                    Entity bullet = ecb.Instantiate(tower.ValueRO.BulletPrefab);
                    
                    // 초기 위치는 타워 위치, 타이머는 2.0 이상으로 설정하여 렌더링되지 않게 함
                    ecb.SetComponent(bullet, LocalTransform.FromPosition(transform.ValueRO.Position));
                    ecb.SetComponent(bullet, new BulletData { Timer = 2.0f, Speed = tower.ValueRO.BulletSpeed });
                    
                    ecb.AppendToBuffer(entity, new TowerBulletElement { Value = bullet });
                }
            }

            if (vfxBuffer.Length == 0 && tower.ValueRO.ExplosionPrefab != Entity.Null)
            {
                for (int i = 0; i < tower.ValueRO.VFXPoolSize; i++)
                {
                    Entity vfx = ecb.Instantiate(tower.ValueRO.ExplosionPrefab);
                    
                    // 초기 위치는 타워 위치
                    ecb.SetComponent(vfx, LocalTransform.FromPosition(transform.ValueRO.Position));
                    
                    ecb.AppendToBuffer(entity, new TowerVFXElement { Value = vfx });
                }
            }
        }
    }
}
