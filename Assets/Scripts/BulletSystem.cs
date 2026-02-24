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
    private EntityQuery _monsterQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _monsterQuery = SystemAPI.QueryBuilder().WithAll<MonsterData, LocalTransform>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float dt = SystemAPI.Time.DeltaTime;
        float currentTime = (float)SystemAPI.Time.ElapsedTime;

        // 몬스터 정보 수집 (데미지 판정용)
        var monsterEntities = _monsterQuery.ToEntityArray(Allocator.TempJob);
        var monsterTransforms = _monsterQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
        var monsterPositions = new NativeArray<MonsterPosInfo>(monsterTransforms.Length, Allocator.TempJob);
        for (int i = 0; i < monsterTransforms.Length; i++)
        {
            monsterPositions[i] = new MonsterPosInfo
            {
                Position = monsterTransforms[i].Position,
                Entity = monsterEntities[i]
            };
        }
        monsterPositions.Sort(new MonsterXComparer());

        var monsterLookup = SystemAPI.GetComponentLookup<MonsterData>(false);
        var vfxPoolLookup = SystemAPI.GetComponentLookup<TowerVFXPool>(false);

        // 1. Single Hit Damage Handling
        foreach (var (hitRequest, hitEntity) in SystemAPI.Query<RefRO<SingleDamageRequest>>().WithEntityAccess())
        {
            ApplySingleDamage(ref monsterLookup, hitRequest.ValueRO.TargetEntity, hitRequest.ValueRO.Damage);
            TriggerExplosionVFX(ref state, hitRequest.ValueRO.TowerEntity, hitRequest.ValueRO.ImpactPosition, ecb, vfxPoolLookup);
            ecb.RemoveComponent<SingleDamageRequest>(hitEntity);
        }

        // 2. AoE Hit Damage Handling
        foreach (var (hitRequest, hitEntity) in SystemAPI.Query<RefRO<AoEDamageRequest>>().WithEntityAccess())
        {
            ApplyAoEDamage(hitRequest.ValueRO.TargetPosition, hitRequest.ValueRO.Radius, hitRequest.ValueRO.Damage, monsterPositions, monsterLookup);
            TriggerExplosionVFX(ref state, hitRequest.ValueRO.TowerEntity, hitRequest.ValueRO.TargetPosition, ecb, vfxPoolLookup);
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
                    ApplyAoEDamage(bullet.ValueRO.EndPos, bullet.ValueRO.AoERadius, bullet.ValueRO.Damage, monsterPositions, monsterLookup);
                }
                else 
                {
                    ApplySingleDamage(ref monsterLookup, bullet.ValueRO.TargetEntity, bullet.ValueRO.Damage);
                }
                TriggerExplosionVFX(ref state, bullet.ValueRO.TowerEntity, bullet.ValueRO.EndPos, ecb, vfxPoolLookup);
                bullet.ValueRW.Timer = 2.0f;
            }
        }

        monsterEntities.Dispose();
        monsterTransforms.Dispose();
        monsterPositions.Dispose();
    }

    [BurstCompile]
    private void TriggerExplosionVFX(ref SystemState state, Entity towerEntity, float3 position, EntityCommandBuffer ecb, ComponentLookup<TowerVFXPool> vfxPoolLookup)
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
    private void ApplyAoEDamage(float3 center, float radius, int damage, NativeArray<MonsterPosInfo> monsterPositions, ComponentLookup<MonsterData> monsterLookup)
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

    struct MonsterPosInfo
    {
        public float3 Position;
        public Entity Entity;
    }

    struct MonsterXComparer : System.Collections.Generic.IComparer<MonsterPosInfo>
    {
        public int Compare(MonsterPosInfo x, MonsterPosInfo y) => x.Position.x.CompareTo(y.Position.x);
    }
}