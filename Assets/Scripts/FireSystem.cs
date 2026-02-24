using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct FireSystem : ISystem
{
    private EntityQuery _monsterQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _monsterQuery = SystemAPI.QueryBuilder().WithAll<MonsterData, LocalTransform>().Build();
        state.RequireForUpdate(_monsterQuery);
        state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (_monsterQuery.IsEmpty) return;

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

        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float dt = SystemAPI.Time.DeltaTime;
        float currentTime = (float)SystemAPI.Time.ElapsedTime;

        var monsterLookup = SystemAPI.GetComponentLookup<MonsterData>(false);
        var hitDataLookup = SystemAPI.GetComponentLookup<AttackHitData>(true);
        var aoeHitLookup = SystemAPI.GetComponentLookup<AoEHitAttack>(true);
        var directDataLookup = SystemAPI.GetComponentLookup<AttackDirectData>(true);
        var projDataLookup = SystemAPI.GetComponentLookup<AttackProjectileData>(true);
        var vfxPoolLookup = SystemAPI.GetComponentLookup<TowerVFXPool>(false);
        var rangeDefaultLookup = SystemAPI.GetComponentLookup<TowerRangeDefault>(true);
        var rangeSectorLookup = SystemAPI.GetComponentLookup<TowerRangeSector>(true);
        var rangeAnnulusLookup = SystemAPI.GetComponentLookup<TowerRangeAnnulus>(true);
        var rangeOffsetLookup = SystemAPI.GetComponentLookup<TowerRangeOffset>(true);

        // 1. Default Range Towers
        foreach (var (stats, range, transform, towerEntity) in 
            SystemAPI.Query<RefRW<TowerStats>, RefRO<TowerRangeDefault>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            stats.ValueRW.AttackTimer -= dt;
            if (stats.ValueRO.AttackTimer > 0) continue;

            if (FindTargetDefault(transform.ValueRO.Position, range.ValueRO.MaxRange, monsterPositions, out var tPos, out var tEnt))
            {
                ExecuteFire(ref state, stats, transform, towerEntity, tPos, tEnt, ecb, currentTime, monsterPositions,
                    hitDataLookup, aoeHitLookup, directDataLookup, projDataLookup, vfxPoolLookup, 
                    rangeDefaultLookup, rangeSectorLookup, rangeAnnulusLookup, rangeOffsetLookup, monsterLookup);
            }
        }

        // 2. Sector Range Towers
        foreach (var (stats, range, transform, towerEntity) in 
            SystemAPI.Query<RefRW<TowerStats>, RefRO<TowerRangeSector>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            stats.ValueRW.AttackTimer -= dt;
            if (stats.ValueRO.AttackTimer > 0) continue;

            if (FindTargetSector(transform.ValueRO.Position, stats.ValueRO.LogicalRotation, range.ValueRO.MaxRange, range.ValueRO.Angle, monsterPositions, out var tPos, out var tEnt))
            {
                ExecuteFire(ref state, stats, transform, towerEntity, tPos, tEnt, ecb, currentTime, monsterPositions,
                    hitDataLookup, aoeHitLookup, directDataLookup, projDataLookup, vfxPoolLookup, 
                    rangeDefaultLookup, rangeSectorLookup, rangeAnnulusLookup, rangeOffsetLookup, monsterLookup);
            }
        }

        // 3. Annulus Range Towers
        foreach (var (stats, range, transform, towerEntity) in 
            SystemAPI.Query<RefRW<TowerStats>, RefRO<TowerRangeAnnulus>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            stats.ValueRW.AttackTimer -= dt;
            if (stats.ValueRO.AttackTimer > 0) continue;

            if (FindTargetAnnulus(transform.ValueRO.Position, range.ValueRO.MaxRange, range.ValueRO.MinRange, monsterPositions, out var tPos, out var tEnt))
            {
                ExecuteFire(ref state, stats, transform, towerEntity, tPos, tEnt, ecb, currentTime, monsterPositions,
                    hitDataLookup, aoeHitLookup, directDataLookup, projDataLookup, vfxPoolLookup, 
                    rangeDefaultLookup, rangeSectorLookup, rangeAnnulusLookup, rangeOffsetLookup, monsterLookup);
            }
        }

        // 4. Offset Range Towers
        foreach (var (stats, range, transform, towerEntity) in 
            SystemAPI.Query<RefRW<TowerStats>, RefRO<TowerRangeOffset>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            stats.ValueRW.AttackTimer -= dt;
            if (stats.ValueRO.AttackTimer > 0) continue;

            if (FindTargetOffset(transform.ValueRO.Position, stats.ValueRO.LogicalRotation, range.ValueRO.AttackRadius, range.ValueRO.Offset, monsterPositions, out var tPos, out var tEnt))
            {
                ExecuteFire(ref state, stats, transform, towerEntity, tPos, tEnt, ecb, currentTime, monsterPositions,
                    hitDataLookup, aoeHitLookup, directDataLookup, projDataLookup, vfxPoolLookup, 
                    rangeDefaultLookup, rangeSectorLookup, rangeAnnulusLookup, rangeOffsetLookup, monsterLookup);
            }
        }

        foreach (var (bullet, bulletEntity) in SystemAPI.Query<RefRW<BulletData>>().WithEntityAccess())
        {
            bullet.ValueRW.Timer += dt * bullet.ValueRO.Speed;
            if (bullet.ValueRO.Timer >= 1.0f && bullet.ValueRO.Timer < 2.0f)
            {
                ApplyBulletDamage(ref state, bullet.ValueRO, monsterPositions, ecb, monsterLookup, vfxPoolLookup);
                bullet.ValueRW.Timer = 2.0f;
            }
        }

        monsterEntities.Dispose();
        monsterTransforms.Dispose();
        monsterPositions.Dispose();
    }

    [BurstCompile]
    private void ExecuteFire(ref SystemState state, RefRW<TowerStats> stats, RefRW<LocalTransform> transform, 
        Entity towerEntity, float3 targetPos, Entity targetEntity, EntityCommandBuffer ecb, float currentTime, NativeArray<MonsterPosInfo> monsterPositions,
        ComponentLookup<AttackHitData> hitDataLookup, ComponentLookup<AoEHitAttack> aoeHitLookup, ComponentLookup<AttackDirectData> directDataLookup,
        ComponentLookup<AttackProjectileData> projDataLookup, ComponentLookup<TowerVFXPool> vfxPoolLookup,
        ComponentLookup<TowerRangeDefault> rangeDefaultLookup, ComponentLookup<TowerRangeSector> rangeSectorLookup,
        ComponentLookup<TowerRangeAnnulus> rangeAnnulusLookup, ComponentLookup<TowerRangeOffset> rangeOffsetLookup,
        ComponentLookup<MonsterData> monsterLookup)
    {
        var fireTime = SystemAPI.GetComponentRW<TowerFireTime>(towerEntity);
        var bulletPool = SystemAPI.GetComponentRW<TowerBulletPool>(towerEntity);
        var vfxPool = vfxPoolLookup[towerEntity];
        var bulletBuffer = SystemAPI.GetBuffer<TowerBulletElement>(towerEntity);
        var vfxBuffer = SystemAPI.GetBuffer<TowerVFXElement>(towerEntity);
        
        float3 towerPos = transform.ValueRO.Position;

        // Rotation
        float3 direction = targetPos - towerPos;
        direction.y = 0f;
        quaternion lookRot = quaternion.identity;
        if (math.lengthsq(direction) > 0f)
        {
            lookRot = quaternion.LookRotationSafe(math.normalize(direction), math.up());
            if (stats.ValueRO.Rotationable) transform.ValueRW.Rotation = lookRot;
        }

        if (hitDataLookup.HasComponent(towerEntity))
        {
            if (aoeHitLookup.HasComponent(towerEntity))
            {
                float radius = aoeHitLookup[towerEntity].AoERadius;
                ApplyAoEDamage(ref state, targetPos, radius, stats.ValueRO.Damage, monsterPositions, ecb, monsterLookup);
            }
            else
            {
                ApplySingleDamage(ref monsterLookup, targetEntity, stats.ValueRO.Damage, ecb);
            }

            if (bulletBuffer.Length > 0)
            {
                Entity muzzleEntity = bulletBuffer[bulletPool.ValueRW.CurrentIndex % bulletBuffer.Length].Value;
                bulletPool.ValueRW.CurrentIndex++;

                ecb.SetComponent(muzzleEntity, LocalTransform.FromPosition(towerPos));
                ecb.AppendToBuffer(towerEntity, new MuzzleVFXPlayRequest { VfxEntity = muzzleEntity });
            }

            if (vfxBuffer.Length > 0)
            {
                Entity vfxEntity = vfxBuffer[vfxPool.CurrentIndex % vfxBuffer.Length].Value;
                vfxPool.CurrentIndex++;
                vfxPoolLookup[towerEntity] = vfxPool;

                ecb.SetComponent(vfxEntity, LocalTransform.FromPosition(targetPos));
                ecb.AppendToBuffer(towerEntity, new VFXPlayRequest { VfxEntity = vfxEntity });
            }
        }
        else if (directDataLookup.HasComponent(towerEntity) || projDataLookup.HasComponent(towerEntity))
        {
            if (bulletBuffer.Length > 0)
            {
                Entity bulletEntity = bulletBuffer[bulletPool.ValueRW.CurrentIndex % bulletBuffer.Length].Value;
                bulletPool.ValueRW.CurrentIndex++;

                ecb.SetComponent(bulletEntity, LocalTransform.FromPositionRotation(towerPos, lookRot));
                
                float distance = math.distance(new float2(towerPos.x, towerPos.z), new float2(targetPos.x, targetPos.z));
                float bulletSpeed = 10f;
                float bulletHeight = 0f;
                TowerAttackType attackType = TowerAttackType.Projectile;

                if (directDataLookup.HasComponent(towerEntity))
                {
                    var directData = directDataLookup[towerEntity];
                    float maxRange = GetMaxRange(towerEntity, rangeDefaultLookup, rangeSectorLookup, rangeAnnulusLookup, rangeOffsetLookup);
                    float safeDistance = math.max(distance, 0.1f);
                    bulletSpeed = directData.Speed * (maxRange / safeDistance);
                    attackType = TowerAttackType.Direct;
                }
                else
                {
                    var projData = projDataLookup[towerEntity];
                    bulletSpeed = projData.Speed;
                    bulletHeight = projData.Height;
                }

                bool isAoe = aoeHitLookup.HasComponent(towerEntity);
                float aoeRadius = 0f;
                if (isAoe) aoeRadius = aoeHitLookup[towerEntity].AoERadius;

                ecb.SetComponent(bulletEntity, new BulletData
                {
                    StartPos = towerPos,
                    EndPos = targetPos,
                    Speed = bulletSpeed,
                    Timer = 0f,
                    Damage = stats.ValueRO.Damage,
                    TargetEntity = targetEntity,
                    TowerEntity = towerEntity,
                    AttackType = attackType,
                    IsAoe = isAoe,
                    AoERadius = aoeRadius
                });
                
                ecb.SetComponent(bulletEntity, new BulletAnimation
                {
                    LastFireTime = currentTime,
                    Speed = bulletSpeed,
                    Length = distance,
                    Height = bulletHeight
                });
            }
        }

        fireTime.ValueRW.Value = currentTime;
        stats.ValueRW.AttackTimer = 1f / stats.ValueRO.AttackSpeed;
    }

    private float GetMaxRange(Entity tower, 
        ComponentLookup<TowerRangeDefault> rangeDefaultLookup, ComponentLookup<TowerRangeSector> rangeSectorLookup,
        ComponentLookup<TowerRangeAnnulus> rangeAnnulusLookup, ComponentLookup<TowerRangeOffset> rangeOffsetLookup)
    {
        if (rangeDefaultLookup.HasComponent(tower)) return rangeDefaultLookup[tower].MaxRange;
        if (rangeSectorLookup.HasComponent(tower)) return rangeSectorLookup[tower].MaxRange;
        if (rangeAnnulusLookup.HasComponent(tower)) return rangeAnnulusLookup[tower].MaxRange;
        if (rangeOffsetLookup.HasComponent(tower))
        {
            var r = rangeOffsetLookup[tower];
            return r.MaxRange + r.AttackRadius;
        }
        return 5f;
    }

    private bool FindTargetDefault(float3 towerPos, float maxRange, NativeArray<MonsterPosInfo> monsterPositions, out float3 targetPos, out Entity targetEntity)
    {
        targetPos = float3.zero; targetEntity = Entity.Null;
        float2 towerPos2D = new float2(towerPos.x, towerPos.z);
        float maxRangeSq = maxRange * maxRange;

        int startIdx = BinarySearchX(monsterPositions, towerPos.x);
        float nearestDistSq = maxRangeSq;
        bool found = false;

        for (int i = startIdx; i < monsterPositions.Length; i++)
        {
            float dx = monsterPositions[i].Position.x - towerPos.x;
            if (dx * dx > maxRangeSq) break;
            float dSq = math.distancesq(towerPos2D, new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z));
            if (dSq <= nearestDistSq) { nearestDistSq = dSq; targetPos = monsterPositions[i].Position; targetEntity = monsterPositions[i].Entity; found = true; }
        }
        for (int i = startIdx - 1; i >= 0; i--)
        {
            float dx = towerPos.x - monsterPositions[i].Position.x;
            if (dx * dx > maxRangeSq) break;
            float dSq = math.distancesq(towerPos2D, new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z));
            if (dSq <= nearestDistSq) { nearestDistSq = dSq; targetPos = monsterPositions[i].Position; targetEntity = monsterPositions[i].Entity; found = true; }
        }
        return found;
    }

    private bool FindTargetSector(float3 towerPos, quaternion logicalRot, float maxRange, float angle, NativeArray<MonsterPosInfo> monsterPositions, out float3 targetPos, out Entity targetEntity)
    {
        targetPos = float3.zero; targetEntity = Entity.Null;
        float3 forward = math.forward(logicalRot);
        float halfAngleRad = math.radians(angle) * 0.5f;
        float2 towerPos2D = new float2(towerPos.x, towerPos.z);
        float2 forward2D = math.normalize(new float2(forward.x, forward.z));
        float maxRangeSq = maxRange * maxRange;

        int startIdx = BinarySearchX(monsterPositions, towerPos.x);
        float nearestDistSq = maxRangeSq;
        bool found = false;

        for (int i = startIdx; i < monsterPositions.Length; i++)
        {
            float dx = monsterPositions[i].Position.x - towerPos.x;
            if (dx * dx > maxRangeSq) break;
            float2 mPos2D = new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z);
            float dSq = math.distancesq(towerPos2D, mPos2D);
            if (dSq <= nearestDistSq)
            {
                float2 dir = math.normalize(mPos2D - towerPos2D);
                if (math.acos(math.clamp(math.dot(forward2D, dir), -1f, 1f)) <= halfAngleRad)
                { nearestDistSq = dSq; targetPos = monsterPositions[i].Position; targetEntity = monsterPositions[i].Entity; found = true; }
            }
        }
        for (int i = startIdx - 1; i >= 0; i--)
        {
            float dx = towerPos.x - monsterPositions[i].Position.x;
            if (dx * dx > maxRangeSq) break;
            float2 mPos2D = new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z);
            float dSq = math.distancesq(towerPos2D, mPos2D);
            if (dSq <= nearestDistSq)
            {
                float2 dir = math.normalize(mPos2D - towerPos2D);
                if (math.acos(math.clamp(math.dot(forward2D, dir), -1f, 1f)) <= halfAngleRad)
                { nearestDistSq = dSq; targetPos = monsterPositions[i].Position; targetEntity = monsterPositions[i].Entity; found = true; }
            }
        }
        return found;
    }

    private bool FindTargetAnnulus(float3 towerPos, float maxRange, float minRange, NativeArray<MonsterPosInfo> monsterPositions, out float3 targetPos, out Entity targetEntity)
    {
        targetPos = float3.zero; targetEntity = Entity.Null;
        float2 towerPos2D = new float2(towerPos.x, towerPos.z);
        float maxRangeSq = maxRange * maxRange;
        float minRangeSq = minRange * minRange;

        int startIdx = BinarySearchX(monsterPositions, towerPos.x);
        float nearestDistSq = maxRangeSq;
        bool found = false;

        for (int i = startIdx; i < monsterPositions.Length; i++)
        {
            float dx = monsterPositions[i].Position.x - towerPos.x;
            if (dx * dx > maxRangeSq) break;
            float dSq = math.distancesq(towerPos2D, new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z));
            if (dSq >= minRangeSq && dSq <= nearestDistSq) { nearestDistSq = dSq; targetPos = monsterPositions[i].Position; targetEntity = monsterPositions[i].Entity; found = true; }
        }
        for (int i = startIdx - 1; i >= 0; i--)
        {
            float dx = towerPos.x - monsterPositions[i].Position.x;
            if (dx * dx > maxRangeSq) break;
            float dSq = math.distancesq(towerPos2D, new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z));
            if (dSq >= minRangeSq && dSq <= nearestDistSq) { nearestDistSq = dSq; targetPos = monsterPositions[i].Position; targetEntity = monsterPositions[i].Entity; found = true; }
        }
        return found;
    }

    private bool FindTargetOffset(float3 towerPos, quaternion logicalRot, float targetingRadius, float3 offset, NativeArray<MonsterPosInfo> monsterPositions, out float3 targetPos, out Entity targetEntity)
    {
        targetPos = float3.zero; targetEntity = Entity.Null;
        float3 worldOffset = math.rotate(logicalRot, offset);
        float3 attackCenter = towerPos + new float3(worldOffset.x, 0, worldOffset.z);
        float2 center2D = new float2(attackCenter.x, attackCenter.z);
        float radiusSq = targetingRadius * targetingRadius;

        int startIdx = BinarySearchX(monsterPositions, attackCenter.x);
        float nearestDistSq = radiusSq;
        bool found = false;

        for (int i = startIdx; i < monsterPositions.Length; i++)
        {
            float dx = monsterPositions[i].Position.x - attackCenter.x;
            if (dx * dx > radiusSq) break;
            float dSq = math.distancesq(center2D, new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z));
            if (dSq <= nearestDistSq) { nearestDistSq = dSq; targetPos = monsterPositions[i].Position; targetEntity = monsterPositions[i].Entity; found = true; }
        }
        for (int i = startIdx - 1; i >= 0; i--)
        {
            float dx = attackCenter.x - monsterPositions[i].Position.x;
            if (dx * dx > radiusSq) break;
            float dSq = math.distancesq(center2D, new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z));
            if (dSq <= nearestDistSq) { nearestDistSq = dSq; targetPos = monsterPositions[i].Position; targetEntity = monsterPositions[i].Entity; found = true; }
        }
        return found;
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

    private void ApplySingleDamage(ref ComponentLookup<MonsterData> monsterLookup, Entity target, int damage, EntityCommandBuffer ecb)
    {
        if (monsterLookup.HasComponent(target))
        {
            var data = monsterLookup[target];
            if (data.HP <= 0) return; // Already dead/dying
            
            data.HP -= damage;
            monsterLookup[target] = data; // Immediate update for other bullets in this frame
        }
    }

    private void ApplyAoEDamage(ref SystemState state, float3 center, float radius, int damage, NativeArray<MonsterPosInfo> monsterPositions, EntityCommandBuffer ecb, ComponentLookup<MonsterData> monsterLookup)
    {
        float radiusSq = radius * radius;
        float2 center2D = new float2(center.x, center.z);
        int startIdx = BinarySearchX(monsterPositions, center.x);

        for (int i = startIdx; i < monsterPositions.Length; i++)
        {
            float dx = monsterPositions[i].Position.x - center.x;
            if (dx * dx > radiusSq) break;
            if (math.distancesq(center2D, new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z)) <= radiusSq)
                ApplySingleDamage(ref monsterLookup, monsterPositions[i].Entity, damage, ecb);
        }
        for (int i = startIdx - 1; i >= 0; i--)
        {
            float dx = center.x - monsterPositions[i].Position.x;
            if (dx * dx > radiusSq) break;
            if (math.distancesq(center2D, new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z)) <= radiusSq)
                ApplySingleDamage(ref monsterLookup, monsterPositions[i].Entity, damage, ecb);
        }
    }

    private void ApplyBulletDamage(ref SystemState state, BulletData bullet, NativeArray<MonsterPosInfo> monsters, EntityCommandBuffer ecb, ComponentLookup<MonsterData> monsterLookup, ComponentLookup<TowerVFXPool> vfxPoolLookup)
    {
        if (bullet.IsAoe) ApplyAoEDamage(ref state, bullet.EndPos, bullet.AoERadius, bullet.Damage, monsters, ecb, monsterLookup);
        else ApplySingleDamage(ref monsterLookup, bullet.TargetEntity, bullet.Damage, ecb);

        if (bullet.TowerEntity != Entity.Null && vfxPoolLookup.HasComponent(bullet.TowerEntity))
        {
            var vfxPool = vfxPoolLookup[bullet.TowerEntity];
            var vfxBuffer = SystemAPI.GetBuffer<TowerVFXElement>(bullet.TowerEntity);
            if (vfxBuffer.IsCreated && vfxBuffer.Length > 0)
            {
                Entity vfxEntity = vfxBuffer[vfxPool.CurrentIndex % vfxBuffer.Length].Value;
                vfxPool.CurrentIndex++;
                vfxPoolLookup[bullet.TowerEntity] = vfxPool;
                
                ecb.SetComponent(vfxEntity, LocalTransform.FromPosition(bullet.EndPos));
                ecb.AppendToBuffer(bullet.TowerEntity, new VFXPlayRequest { VfxEntity = vfxEntity });
            }
        }
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