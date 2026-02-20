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
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var monsterQuery = SystemAPI.QueryBuilder().WithAll<MonsterData, LocalTransform>().Build();
        if (monsterQuery.IsEmpty) return;

        var monsterEntities = monsterQuery.ToEntityArray(Allocator.TempJob);
        var monsterTransforms = monsterQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

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

        foreach (var (tower, transform, fireTime) in SystemAPI.Query<RefRW<TowerData>, RefRW<LocalTransform>, RefRW<TowerFireTime>>())
        {
            tower.ValueRW.AttackTimer -= dt;

            if (tower.ValueRO.AttackTimer <= 0)
            {
                float3 towerPos = transform.ValueRO.Position;
                float3 targetPos = float3.zero;
                bool found = FindNearestMonsterInRange(towerPos, tower.ValueRO.MaxRange, monsterPositions, out targetPos);

                if (found)
                {
                    float3 direction = targetPos - towerPos;
                    direction.y = 0f;
                    if (math.lengthsq(direction) > 0f)
                    {
                        transform.ValueRW.Rotation = quaternion.LookRotationSafe(math.normalize(direction), math.up());
                    }

                    if (tower.ValueRO.BulletPrefab != Entity.Null)
                    {
                        Entity bullet = ecb.Instantiate(tower.ValueRO.BulletPrefab);
                        ecb.SetComponent(bullet, LocalTransform.FromPosition(towerPos));
                        ecb.AddComponent(bullet, new BulletData
                        {
                            StartPos = towerPos,
                            EndPos = targetPos,
                            Timer = 0f
                        });
                    }

                    fireTime.ValueRW.Value = currentTime;
                    tower.ValueRW.AttackTimer = 1f / tower.ValueRO.AttackSpeed;
                }
            }
        }

        foreach (var (bullet, bulletTr, bulletEntity) in SystemAPI.Query<RefRW<BulletData>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            bullet.ValueRW.Timer += dt;
            float t = math.saturate(bullet.ValueRO.Timer);
            bulletTr.ValueRW.Position = math.lerp(bullet.ValueRO.StartPos, bullet.ValueRO.EndPos, t);

            if (t >= 1.0f)
            {
                ecb.DestroyEntity(bulletEntity);
                // 여기에 폭발 VFX 생성 로직 추가 가능
            }
        }

        monsterEntities.Dispose();
        monsterTransforms.Dispose();
        monsterPositions.Dispose();
    }

    private bool FindNearestMonsterInRange(float3 towerPos, float range, NativeArray<MonsterPosInfo> sortedMonsters, out float3 targetPos)
    {
        targetPos = float3.zero;
        int count = sortedMonsters.Length;
        if (count == 0) return false;

        // 이진 탐색으로 X축 기준 가장 가까운 지점 찾기
        int startIdx = 0;
        int low = 0, high = count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (sortedMonsters[mid].Position.x < towerPos.x) low = mid + 1;
            else high = mid - 1;
        }
        startIdx = low;

        float nearestDistSq = range * range;
        bool found = false;

        // 양방향 검색 (X축 거리 차이가 이미 최소 거리보다 크면 중단)
        for (int i = startIdx; i < count; i++)
        {
            float dx = sortedMonsters[i].Position.x - towerPos.x;
            if (dx * dx > nearestDistSq) break;
            float dSq = math.distancesq(towerPos, sortedMonsters[i].Position);
            if (dSq < nearestDistSq) { nearestDistSq = dSq; targetPos = sortedMonsters[i].Position; found = true; }
        }
        for (int i = startIdx - 1; i >= 0; i--)
        {
            float dx = towerPos.x - sortedMonsters[i].Position.x;
            if (dx * dx > nearestDistSq) break;
            float dSq = math.distancesq(towerPos, sortedMonsters[i].Position);
            if (dSq < nearestDistSq) { nearestDistSq = dSq; targetPos = sortedMonsters[i].Position; found = true; }
        }

        return found;
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