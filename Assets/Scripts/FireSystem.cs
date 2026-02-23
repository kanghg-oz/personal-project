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

        // 타워와 탄환 버퍼를 함께 쿼리 (Entity 접근 추가)
        foreach (var (tower, transform, fireTime, bulletBuffer, towerEntity) in SystemAPI.Query<RefRW<TowerData>, RefRW<LocalTransform>, RefRW<TowerFireTime>, DynamicBuffer<TowerBulletElement>>().WithEntityAccess())
        {
            tower.ValueRW.AttackTimer -= dt;

            if (tower.ValueRO.AttackTimer <= 0)
            {
                float3 towerPos = transform.ValueRO.Position;
                float3 targetPos = float3.zero;
                Entity targetEntity = Entity.Null;
                
                // 타워의 회전 방향 (전방 벡터)
                float3 forward = math.forward(transform.ValueRO.Rotation);
                
                bool found = FindNearestMonsterInRange(towerPos, forward, transform.ValueRO.Rotation, tower.ValueRO, monsterPositions, out targetPos, out targetEntity);

                if (found)
                {
                    float3 direction = targetPos - towerPos;
                    direction.y = 0f;
                    quaternion lookRot = quaternion.identity;
                    if (math.lengthsq(direction) > 0f)
                    {
                        lookRot = quaternion.LookRotationSafe(math.normalize(direction), math.up());
                        // Sector 타입이 아닐 때만 타워가 타겟을 바라보도록 회전 (Sector는 고정된 방향을 가짐)
                        if (tower.ValueRO.RangeType != TowerRangeType.Sector)
                        {
                            transform.ValueRW.Rotation = lookRot;
                        }
                    }

                    if (bulletBuffer.Length > 0)
                    {
                        // 풀에서 탄환 가져오기 (순환)
                        int idx = tower.ValueRO.CurrentBulletIndex % bulletBuffer.Length;
                        Entity bullet = bulletBuffer[idx].Value;
                        tower.ValueRW.CurrentBulletIndex++;

                        // 탄환의 위치는 타워 위치로 고정하고, 회전은 타워와 동일하게 타겟을 바라보도록 설정
                        ecb.SetComponent(bullet, LocalTransform.FromPositionRotation(towerPos, lookRot));
                        
                        // 타워와 몬스터 사이의 거리 계산
                        float distance = math.distance(towerPos, targetPos);
                        float shaderLength = distance;
                        float bulletSpeed = tower.ValueRO.BulletSpeed;

                        ecb.SetComponent(bullet, new BulletData
                        {
                            StartPos = towerPos,
                            EndPos = targetPos,
                            Speed = bulletSpeed,
                            Timer = 0f,
                            Damage = tower.ValueRO.Damage,
                            TargetEntity = targetEntity,
                            TowerEntity = towerEntity // 타워 엔티티 저장 (VFX 풀 접근용)
                        });
                        
                        ecb.SetComponent(bullet, new BulletAnimation
                        {
                            LastFireTime = currentTime,
                            Speed = bulletSpeed,
                            Length = shaderLength,
                            Height = 1f  // 기본값
                        });
                    }

                    fireTime.ValueRW.Value = currentTime;
                    tower.ValueRW.AttackTimer = 1f / tower.ValueRO.AttackSpeed;
                }
            }
        }

        foreach (var (bullet, bulletEntity) in SystemAPI.Query<RefRW<BulletData>>().WithEntityAccess())
        {
            // 셰이더에서 이동을 처리하므로 Transform 갱신은 생략
            bullet.ValueRW.Timer += dt * bullet.ValueRO.Speed;
            
            // Timer가 1.0 이상이면 목표 도달로 간주
            if (bullet.ValueRO.Timer >= 1.0f && bullet.ValueRO.Timer < 2.0f)
            {
                // 도달 시점 이벤트(데미지, 폭발 등) 처리
                if (bullet.ValueRO.TargetEntity != Entity.Null && SystemAPI.Exists(bullet.ValueRO.TargetEntity))
                {
                    if (SystemAPI.HasComponent<MonsterData>(bullet.ValueRO.TargetEntity))
                    {
                        var monsterData = SystemAPI.GetComponent<MonsterData>(bullet.ValueRO.TargetEntity);
                        monsterData.HP -= bullet.ValueRO.Damage;
                        
                        if (monsterData.HP <= 0)
                        {
                            ecb.DestroyEntity(bullet.ValueRO.TargetEntity);
                        }
                        else
                        {
                            ecb.SetComponent(bullet.ValueRO.TargetEntity, monsterData);
                        }

                        // 폭발 VFX 생성 요청 (버퍼에 추가)
                        if (bullet.ValueRO.TowerEntity != Entity.Null && SystemAPI.Exists(bullet.ValueRO.TowerEntity))
                        {
                            if (SystemAPI.HasBuffer<VFXPlayRequest>(bullet.ValueRO.TowerEntity))
                            {
                                ecb.AppendToBuffer(bullet.ValueRO.TowerEntity, new VFXPlayRequest { Position = bullet.ValueRO.EndPos });
                            }
                        }
                    }
                }
                
                // 도달 처리가 끝났음을 표시하기 위해 Timer를 2.0 이상으로 설정
                bullet.ValueRW.Timer = 2.0f;
            }
        }

        monsterEntities.Dispose();
        monsterTransforms.Dispose();
        monsterPositions.Dispose();
    }

    private bool FindNearestMonsterInRange(float3 towerPos, float3 towerForward, quaternion towerRotation, TowerData towerData, NativeArray<MonsterPosInfo> sortedMonsters, out float3 targetPos, out Entity targetEntity)
    {
        targetPos = float3.zero;
        targetEntity = Entity.Null;
        int count = sortedMonsters.Length;
        if (count == 0) return false;

        float maxRangeSq = towerData.MaxRange * towerData.MaxRange;
        float minRangeSq = towerData.MinRange * towerData.MinRange;
        float halfAngleRad = math.radians(towerData.RangeAngle) * 0.5f;

        // OffsetCircle의 경우 실제 타격 중심점 계산
        float3 attackCenter = towerPos;
        if (towerData.RangeType == TowerRangeType.OffsetCircle)
        {
            // 타워의 회전을 고려하여 오프셋 적용
            attackCenter = towerPos + math.rotate(towerRotation, towerData.RangeOffset);
            // OffsetCircle의 실제 타격 반경은 MinRange로 가정
            maxRangeSq = towerData.MinRange * towerData.MinRange;
        }

        // 이진 탐색으로 X축 기준 가장 가까운 지점 찾기 (attackCenter 기준)
        int startIdx = 0;
        int low = 0, high = count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (sortedMonsters[mid].Position.x < attackCenter.x) low = mid + 1;
            else high = mid - 1;
        }
        startIdx = low;

        float nearestDistSq = maxRangeSq;
        bool found = false;

        // 양방향 검색 (X축 거리 차이가 이미 최소 거리보다 크면 중단)
        for (int i = startIdx; i < count; i++)
        {
            float dx = sortedMonsters[i].Position.x - attackCenter.x;
            if (dx * dx > nearestDistSq) break;
            
            float dSq = math.distancesq(attackCenter, sortedMonsters[i].Position);
            if (dSq <= nearestDistSq && IsValidTarget(towerPos, towerForward, sortedMonsters[i].Position, dSq, towerData, minRangeSq, halfAngleRad))
            { 
                nearestDistSq = dSq; 
                targetPos = sortedMonsters[i].Position; 
                targetEntity = sortedMonsters[i].Entity; 
                found = true; 
            }
        }
        for (int i = startIdx - 1; i >= 0; i--)
        {
            float dx = attackCenter.x - sortedMonsters[i].Position.x;
            if (dx * dx > nearestDistSq) break;
            
            float dSq = math.distancesq(attackCenter, sortedMonsters[i].Position);
            if (dSq <= nearestDistSq && IsValidTarget(towerPos, towerForward, sortedMonsters[i].Position, dSq, towerData, minRangeSq, halfAngleRad))
            { 
                nearestDistSq = dSq; 
                targetPos = sortedMonsters[i].Position; 
                targetEntity = sortedMonsters[i].Entity; 
                found = true; 
            }
        }

        return found;
    }

    private bool IsValidTarget(float3 towerPos, float3 towerForward, float3 targetPos, float distSq, TowerData towerData, float minRangeSq, float halfAngleRad)
    {
        // 1. Annulus 타입: 최소 사거리 체크
        if (towerData.RangeType == TowerRangeType.Annulus)
        {
            if (distSq < minRangeSq) return false;
        }
        
        // 2. Sector 타입: 각도 체크
        if (towerData.RangeType == TowerRangeType.Sector)
        {
            float3 dirToTarget = math.normalize(targetPos - towerPos);
            // 타워의 전방 벡터와 타겟 방향 벡터 사이의 각도 계산
            float dot = math.dot(towerForward, dirToTarget);
            // acos는 라디안 값을 반환
            float angleToTarget = math.acos(math.clamp(dot, -1f, 1f));
            
            if (angleToTarget > halfAngleRad) return false;
        }

        // OffsetCircle은 FindNearestMonsterInRange에서 이미 attackCenter와 MinRange를 기준으로 거리를 계산했으므로 여기서는 true 반환
        return true;
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