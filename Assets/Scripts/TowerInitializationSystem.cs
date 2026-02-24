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
        foreach (var (stats, transform, bulletBuffer, vfxBuffer, bulletPool, vfxPool, entity) in 
            SystemAPI.Query<RefRW<TowerStats>, RefRO<LocalTransform>, DynamicBuffer<TowerBulletElement>, DynamicBuffer<TowerVFXElement>, RefRW<TowerBulletPool>, RefRW<TowerVFXPool>>().WithEntityAccess())
        {
            if (bulletBuffer.Length == 0 && bulletPool.ValueRO.BulletPrefab != Entity.Null)
            {
                // 초기화 시점의 Transform.Rotation을 LogicalRotation으로 저장
                stats.ValueRW.LogicalRotation = transform.ValueRO.Rotation;

                float initialBulletSpeed = 10f;
                bool isHitType = SystemAPI.HasComponent<AttackHitData>(entity);

                if (SystemAPI.HasComponent<AttackDirectData>(entity)) initialBulletSpeed = SystemAPI.GetComponent<AttackDirectData>(entity).Speed;
                else if (SystemAPI.HasComponent<AttackProjectileData>(entity)) initialBulletSpeed = SystemAPI.GetComponent<AttackProjectileData>(entity).Speed;

                for (int i = 0; i < bulletPool.ValueRO.PoolSize; i++)
                {
                    Entity bullet = ecb.Instantiate(bulletPool.ValueRO.BulletPrefab);
                    
                    // 초기 위치는 타워 위치, 타이머는 2.0 이상으로 설정하여 렌더링되지 않게 함
                    ecb.SetComponent(bullet, LocalTransform.FromPosition(transform.ValueRO.Position));
                    
                    if (!isHitType)
                    {
                        ecb.SetComponent(bullet, new BulletData { Timer = 2.0f, Speed = initialBulletSpeed });
                    }
                    
                    ecb.AppendToBuffer(entity, new TowerBulletElement { Value = bullet });
                }
            }

            if (vfxBuffer.Length == 0 && vfxPool.ValueRO.ExplosionPrefab != Entity.Null)
            {
                // 스케일 설정 및 저장
                float scale = 1.0f;
                if (SystemAPI.HasComponent<AoEHitAttack>(entity))
                {
                    scale = SystemAPI.GetComponent<AoEHitAttack>(entity).AoERadius * 2.0f;
                }
                vfxPool.ValueRW.VFXScale = scale;
                
                for (int i = 0; i < vfxPool.ValueRO.PoolSize; i++)
                {
                    Entity vfx = ecb.Instantiate(vfxPool.ValueRO.ExplosionPrefab);
                    
                    // 초기 위치는 타워 위치, 스케일은 1로 고정하며 VFX 프로퍼티 설정을 위한 데이터 추가
                    ecb.SetComponent(vfx, LocalTransform.FromPosition(transform.ValueRO.Position));
                    ecb.AddComponent(vfx, new VFXScaleProperty { Value = scale });
                    
                    ecb.AppendToBuffer(entity, new TowerVFXElement { Value = vfx });
                }
            }
        }
    }
}
