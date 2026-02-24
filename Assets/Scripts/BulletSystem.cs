using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FireSystem))]
[BurstCompile]
public partial struct BulletSystem : ISystem
{
    private ComponentLookup<MonsterData> _monsterLookup;
    private ComponentLookup<TowerVFXPool> _vfxPoolLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MonsterSpatialSingleton>();
        _monsterLookup = state.GetComponentLookup<MonsterData>(false);
        _vfxPoolLookup = state.GetComponentLookup<TowerVFXPool>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _monsterLookup.Update(ref state);
        _vfxPoolLookup.Update(ref state);

        var spatialSingleton = SystemAPI.GetSingleton<MonsterSpatialSingleton>();
        var monsterPositions = spatialSingleton.SortedMonsters.AsArray();

        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float dt = SystemAPI.Time.DeltaTime;
        float currentTime = (float)SystemAPI.Time.ElapsedTime;

        // 1. Single Hit Damage Handling
        foreach (var (hitRequest, hitEntity) in SystemAPI.Query<RefRO<SingleDamageRequest>>().WithEntityAccess())
        {
            ApplySingleDamage(ref _monsterLookup, hitRequest.ValueRO.TargetEntity, hitRequest.ValueRO.Damage);
            TriggerExplosionVFX(ref state, hitRequest.ValueRO.TowerEntity, hitRequest.ValueRO.ImpactPosition, ecb, ref _vfxPoolLookup);
            ecb.RemoveComponent<SingleDamageRequest>(hitEntity);
        }

        // 2. AoE Hit Damage Handling
        foreach (var (hitRequest, hitEntity) in SystemAPI.Query<RefRO<AoEDamageRequest>>().WithEntityAccess())
        {
            ApplyAoEDamage(hitRequest.ValueRO.TargetPosition, hitRequest.ValueRO.Radius, hitRequest.ValueRO.Damage, monsterPositions, ref _monsterLookup);
            TriggerExplosionVFX(ref state, hitRequest.ValueRO.TowerEntity, hitRequest.ValueRO.TargetPosition, ecb, ref _vfxPoolLookup);
            ecb.RemoveComponent<AoEDamageRequest>(hitEntity);
        }

        // 3. Moving Bullet Updates (Direct, Projectile)
        foreach (var (bullet, bulletEntity) in SystemAPI.Query<RefRW<BulletData>>().WithEntityAccess())
        {
            if (bullet.ValueRO.Timer >= 2.0f) continue;

            bullet.ValueRW.Timer += dt * bullet.ValueRO.Speed;

            if (bullet.ValueRO.Timer >= 1.0f)
            {
                if (bullet.ValueRO.IsAoe) 
                {
                    ApplyAoEDamage(bullet.ValueRO.EndPos, bullet.ValueRO.AoERadius, bullet.ValueRO.Damage, monsterPositions, ref _monsterLookup);
                }
                else 
                {
                    ApplySingleDamage(ref _monsterLookup, bullet.ValueRO.TargetEntity, bullet.ValueRO.Damage);
                }
                TriggerExplosionVFX(ref state, bullet.ValueRO.TowerEntity, bullet.ValueRO.EndPos, ecb, ref _vfxPoolLookup);
                bullet.ValueRW.Timer = 2.0f;
            }
        }
    }

    [BurstCompile]
    private void TriggerExplosionVFX(ref SystemState state, Entity towerEntity, float3 position, EntityCommandBuffer ecb, ref ComponentLookup<TowerVFXPool> vfxPoolLookup)
    {
        if (towerEntity != Entity.Null && vfxPoolLookup.HasComponent(towerEntity))
        {
            var vfxPool = vfxPoolLookup[towerEntity];
            var vfxBuffer = SystemAPI.GetBuffer<TowerVFXElement>(towerEntity);
            if (vfxBuffer.Length > 0)
            {
                Entity vfxEntity = vfxBuffer[vfxPool.CurrentIndex % vfxBuffer.Length].Value;
                vfxPool.CurrentIndex++;
                vfxPoolLookup[towerEntity] = vfxPool;
                
                ecb.SetComponent(vfxEntity, LocalTransform.FromPosition(position));
                ecb.AppendToBuffer(towerEntity, new VFXPlayRequest { VfxEntity = vfxEntity });
            }
        }
    }

    [BurstCompile]
    private void ApplySingleDamage(ref ComponentLookup<MonsterData> monsterLookup, Entity target, int damage)
    {
        if (monsterLookup.HasComponent(target))
        {
            var data = monsterLookup[target];
            if (data.HP <= 0) return;
            
            data.HP -= damage;
            monsterLookup[target] = data;
        }
    }

    [BurstCompile]
    private void ApplyAoEDamage(float3 center, float radius, int damage, NativeArray<MonsterPosInfo> monsterPositions, ref ComponentLookup<MonsterData> monsterLookup)
    {
        float radiusSq = radius * radius;
        float2 center2D = new float2(center.x, center.z);
        int startIdx = BinarySearchX(monsterPositions, center.x);

        for (int i = startIdx; i < monsterPositions.Length; i++)
        {
            float dx = monsterPositions[i].Position.x - center.x;
            if (dx * dx > radiusSq) break;
            if (math.distancesq(center2D, new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z)) <= radiusSq)
                ApplySingleDamage(ref monsterLookup, monsterPositions[i].Entity, damage);
        }
        for (int i = startIdx - 1; i >= 0; i--)
        {
            float dx = center.x - monsterPositions[i].Position.x;
            if (dx * dx > radiusSq) break;
            if (math.distancesq(center2D, new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z)) <= radiusSq)
                ApplySingleDamage(ref monsterLookup, monsterPositions[i].Entity, damage);
        }
    }

    private int BinarySearchX(NativeArray<MonsterPosInfo> monsterPositions, float x)
    {
        int low = 0, high = monsterPositions.Length - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (monsterPositions[mid].Position.x < x) low = mid + 1;
            else high = mid - 1;
        }
        return low;
    }
}