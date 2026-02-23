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
        foreach (var (tower, transform, fireTime, bulletBuffer, vfxBuffer, towerEntity) in SystemAPI.Query<RefRW<TowerData>, RefRW<LocalTransform>, RefRW<TowerFireTime>, DynamicBuffer<TowerBulletElement>, DynamicBuffer<TowerVFXElement>>().WithEntityAccess())
        {
            tower.ValueRW.AttackTimer -= dt;

            if (tower.ValueRO.AttackTimer <= 0)
            {
                float3 towerPos = transform.ValueRO.Position;
                float3 targetPos = float3.zero;
                Entity targetEntity = Entity.Null;
                
                // 타워의 회전 방향 (전방 벡터) - 시각적 회전이 아닌 논리적 회전(LogicalRotation) 사용
                float3 forward = math.forward(tower.ValueRO.LogicalRotation);
                
                bool found = FindNearestMonsterInRange(towerPos, forward, tower.ValueRO.LogicalRotation, tower.ValueRO, monsterPositions, out targetPos, out targetEntity);

                if (found)
                {
                    float3 direction = targetPos - towerPos;
                    direction.y = 0f;
                    quaternion lookRot = quaternion.identity;
                    if (math.lengthsq(direction) > 0f)
                    {
                        lookRot = quaternion.LookRotationSafe(math.normalize(direction), math.up());
                        // Rotationable 변수에 따라 타워가 타겟을 바라보도록 회전할지 결정 (시각적 회전만 변경)
                        if (tower.ValueRO.Rotationable)
                        {
                            transform.ValueRW.Rotation = lookRot;
                        }
                    }

                    if (tower.ValueRO.AttackType == TowerAttackType.Hit)
                    {
                        if (tower.ValueRO.IsAoe && tower.ValueRO.AttackArea > 0f)
                        {
                            // 광역(AoE) 즉시 피해
                            float radius = tower.ValueRO.AttackArea * 0.5f;
                            float radiusSq = radius * radius;
                            float2 targetPos2D = new float2(targetPos.x, targetPos.z);

                            int startIdxAoe = 0;
                            int lowAoe = 0, highAoe = monsterPositions.Length - 1;
                            while (lowAoe <= highAoe)
                            {
                                int mid = (lowAoe + highAoe) / 2;
                                if (monsterPositions[mid].Position.x < targetPos.x) lowAoe = mid + 1;
                                else highAoe = mid - 1;
                            }
                            startIdxAoe = lowAoe;

                            for (int i = startIdxAoe; i < monsterPositions.Length; i++)
                            {
                                float dx = monsterPositions[i].Position.x - targetPos.x;
                                if (dx * dx > radiusSq) break;

                                float2 mPos2D = new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z);
                                if (math.distancesq(targetPos2D, mPos2D) <= radiusSq)
                                {
                                    Entity mEntity = monsterPositions[i].Entity;
                                    if (SystemAPI.Exists(mEntity) && SystemAPI.HasComponent<MonsterData>(mEntity))
                                    {
                                        var monsterData = SystemAPI.GetComponent<MonsterData>(mEntity);
                                        monsterData.HP -= tower.ValueRO.Damage;
                                        if (monsterData.HP <= 0) ecb.DestroyEntity(mEntity);
                                        else ecb.SetComponent(mEntity, monsterData);
                                    }
                                }
                            }
                            for (int i = startIdxAoe - 1; i >= 0; i--)
                            {
                                float dx = targetPos.x - monsterPositions[i].Position.x;
                                if (dx * dx > radiusSq) break;

                                float2 mPos2D = new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z);
                                if (math.distancesq(targetPos2D, mPos2D) <= radiusSq)
                                {
                                    Entity mEntity = monsterPositions[i].Entity;
                                    if (SystemAPI.Exists(mEntity) && SystemAPI.HasComponent<MonsterData>(mEntity))
                                    {
                                        var monsterData = SystemAPI.GetComponent<MonsterData>(mEntity);
                                        monsterData.HP -= tower.ValueRO.Damage;
                                        if (monsterData.HP <= 0) ecb.DestroyEntity(mEntity);
                                        else ecb.SetComponent(mEntity, monsterData);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // 단일 타겟 즉시 피해
                            if (targetEntity != Entity.Null && SystemAPI.Exists(targetEntity))
                            {
                                if (SystemAPI.HasComponent<MonsterData>(targetEntity))
                                {
                                    var monsterData = SystemAPI.GetComponent<MonsterData>(targetEntity);
                                    monsterData.HP -= tower.ValueRO.Damage;
                                    
                                    if (monsterData.HP <= 0)
                                    {
                                        ecb.DestroyEntity(targetEntity);
                                    }
                                    else
                                    {
                                        ecb.SetComponent(targetEntity, monsterData);
                                    }
                                }
                            }
                        }

                        // 발사 VFX (BulletPrefab) 재생 요청
                        if (SystemAPI.HasBuffer<MuzzleVFXPlayRequest>(towerEntity))
                        {
                            if (bulletBuffer.Length > 0)
                            {
                                int idx = tower.ValueRO.CurrentBulletIndex % bulletBuffer.Length;
                                Entity muzzleEntity = bulletBuffer[idx].Value;
                                tower.ValueRW.CurrentBulletIndex++;

                                var muzzleTransform = SystemAPI.GetComponent<LocalTransform>(muzzleEntity);
                                muzzleTransform.Position = towerPos;
                                ecb.SetComponent(muzzleEntity, muzzleTransform);

                                ecb.AppendToBuffer(towerEntity, new MuzzleVFXPlayRequest { VfxEntity = muzzleEntity });
                            }
                        }

                        // 폭발 VFX 재생 요청
                        if (tower.ValueRO.ExplosionPrefab != Entity.Null && SystemAPI.HasBuffer<VFXPlayRequest>(towerEntity))
                        {
                            if (vfxBuffer.Length > 0)
                            {
                                int vfxIdx = tower.ValueRO.CurrentVFXIndex % vfxBuffer.Length;
                                Entity vfxEntity = vfxBuffer[vfxIdx].Value;
                                tower.ValueRW.CurrentVFXIndex++;

                                var vfxTransform = SystemAPI.GetComponent<LocalTransform>(vfxEntity);
                                vfxTransform.Position = targetPos;
                                ecb.SetComponent(vfxEntity, vfxTransform);

                                ecb.AppendToBuffer(towerEntity, new VFXPlayRequest { VfxEntity = vfxEntity });
                            }
                        }
                    }
                    else
                    {
                        if (bulletBuffer.Length > 0)
                        {
                            // 풀에서 탄환 가져오기 (순환)
                            int idx = tower.ValueRO.CurrentBulletIndex % bulletBuffer.Length;
                            Entity bullet = bulletBuffer[idx].Value;
                            tower.ValueRW.CurrentBulletIndex++;

                            // 탄환의 위치는 타워 위치로 고정하고, 회전은 타워와 동일하게 타겟을 바라보도록 설정
                            ecb.SetComponent(bullet, LocalTransform.FromPositionRotation(towerPos, lookRot));
                            
                            // 타워와 몬스터 사이의 거리 계산 (2D)
                            float distance = math.distance(new float2(towerPos.x, towerPos.z), new float2(targetPos.x, targetPos.z));
                            float shaderLength = distance;
                            
                            float bulletSpeed = tower.ValueRO.BulletSpeed;
                            float bulletHeight = tower.ValueRO.BulletHeight;

                            // Direct 타입(직선 궤적)일 경우 속도 보정 및 높이 0 설정
                            if (tower.ValueRO.AttackType == TowerAttackType.Direct)
                            {
                                // 거리가 가까울수록 도달 시간이 짧아지도록(즉, Timer 증가 속도인 Speed가 커지도록) 보정
                                // 1/T = V/D 공식에 따라 (원래속도 * 최대사거리 / 현재거리)로 계산
                                float safeDistance = math.max(distance, 0.1f);
                                bulletSpeed = bulletSpeed * (tower.ValueRO.MaxRange / safeDistance);
                                bulletHeight = 0f;
                            }

                            ecb.SetComponent(bullet, new BulletData
                            {
                                StartPos = towerPos,
                                EndPos = targetPos,
                                Speed = bulletSpeed,
                                Timer = 0f,
                                Damage = tower.ValueRO.Damage,
                                TargetEntity = targetEntity,
                                TowerEntity = towerEntity, // 타워 엔티티 저장 (VFX 풀 접근용)
                                AttackType = tower.ValueRO.AttackType,
                                IsAoe = tower.ValueRO.IsAoe,
                                AttackArea = tower.ValueRO.AttackArea
                            });
                            
                            ecb.SetComponent(bullet, new BulletAnimation
                            {
                                LastFireTime = currentTime,
                                Speed = bulletSpeed,
                                Length = shaderLength,
                                Height = bulletHeight
                            });
                        }
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
                if (bullet.ValueRO.IsAoe)
                {
                    // Aoe: 도달 지점(EndPos)을 중심으로 반경(AttackArea / 2) 내의 모든 적에게 피해
                    float radius = bullet.ValueRO.AttackArea * 0.5f;
                    float radiusSq = radius * radius;
                    float2 endPos2D = new float2(bullet.ValueRO.EndPos.x, bullet.ValueRO.EndPos.z);

                    // 이진 탐색으로 X축 기준 가장 가까운 지점 찾기
                    int startIdx = 0;
                    int low = 0, high = monsterPositions.Length - 1;
                    while (low <= high)
                    {
                        int mid = (low + high) / 2;
                        if (monsterPositions[mid].Position.x < bullet.ValueRO.EndPos.x) low = mid + 1;
                        else high = mid - 1;
                    }
                    startIdx = low;

                    // 양방향 검색 (X축 거리 차이가 반경보다 크면 중단)
                    for (int i = startIdx; i < monsterPositions.Length; i++)
                    {
                        float dx = monsterPositions[i].Position.x - bullet.ValueRO.EndPos.x;
                        if (dx * dx > radiusSq) break;

                        float2 mPos2D = new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z);
                        if (math.distancesq(endPos2D, mPos2D) <= radiusSq)
                        {
                            Entity mEntity = monsterPositions[i].Entity;
                            if (SystemAPI.Exists(mEntity) && SystemAPI.HasComponent<MonsterData>(mEntity))
                            {
                                var mData = SystemAPI.GetComponent<MonsterData>(mEntity);
                                mData.HP -= bullet.ValueRO.Damage;
                                

                                if (mData.HP <= 0)
                                {
                                    ecb.DestroyEntity(mEntity);
                                }
                                else
                                {
                                    ecb.SetComponent(mEntity, mData);
                                }
                            }
                        }
                    }

                    for (int i = startIdx - 1; i >= 0; i--)
                    {
                        float dx = bullet.ValueRO.EndPos.x - monsterPositions[i].Position.x;
                        if (dx * dx > radiusSq) break;

                        float2 mPos2D = new float2(monsterPositions[i].Position.x, monsterPositions[i].Position.z);
                        if (math.distancesq(endPos2D, mPos2D) <= radiusSq)
                        {
                            Entity mEntity = monsterPositions[i].Entity;
                            if (SystemAPI.Exists(mEntity) && SystemAPI.HasComponent<MonsterData>(mEntity))
                            {
                                var mData = SystemAPI.GetComponent<MonsterData>(mEntity);
                                mData.HP -= bullet.ValueRO.Damage;
                                

                                if (mData.HP <= 0)
                                {
                                    ecb.DestroyEntity(mEntity);
                                }
                                else
                                {
                                    ecb.SetComponent(mEntity, mData);
                                }
                            }
                        }
                    }
                }
                else // Default
                {
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
                        }
                    }
                }

                // 폭발 VFX 생성 요청 (버퍼에 추가)
                if (bullet.ValueRO.TowerEntity != Entity.Null && SystemAPI.Exists(bullet.ValueRO.TowerEntity))
                {
                    if (SystemAPI.HasComponent<TowerData>(bullet.ValueRO.TowerEntity))
                    {
                        var tDataRW = SystemAPI.GetComponentRW<TowerData>(bullet.ValueRO.TowerEntity);
                        if (tDataRW.ValueRO.ExplosionPrefab != Entity.Null)
                        {
                            if (SystemAPI.HasBuffer<VFXPlayRequest>(bullet.ValueRO.TowerEntity) && SystemAPI.HasBuffer<TowerVFXElement>(bullet.ValueRO.TowerEntity))
                            {
                                var vfxBuffer = SystemAPI.GetBuffer<TowerVFXElement>(bullet.ValueRO.TowerEntity);
                                if (vfxBuffer.Length > 0)
                                {
                                    int vfxIdx = tDataRW.ValueRO.CurrentVFXIndex % vfxBuffer.Length;
                                    Entity vfxEntity = vfxBuffer[vfxIdx].Value;
                                    
                                    tDataRW.ValueRW.CurrentVFXIndex++;

                                    var vfxTransform = SystemAPI.GetComponent<LocalTransform>(vfxEntity);
                                    vfxTransform.Position = bullet.ValueRO.EndPos;
                                    ecb.SetComponent(vfxEntity, vfxTransform);

                                    ecb.AppendToBuffer(bullet.ValueRO.TowerEntity, new VFXPlayRequest { VfxEntity = vfxEntity });
                                }
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
            // 타워의 회전을 고려하여 오프셋 적용. Y축 변화를 배제하여 정확한 2D 평면 위치를 계산함
            float3 worldOffset = math.rotate(towerRotation, towerData.RangeOffset);
            attackCenter = towerPos + new float3(worldOffset.x, 0, worldOffset.z);
            
            // OffsetCircle의 실제 타격 반경은 MinRange로 가정
            maxRangeSq = towerData.MinRange * towerData.MinRange;
        }

        float2 attackCenter2D = new float2(attackCenter.x, attackCenter.z);

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
            if (dx * dx > maxRangeSq) break; // nearestDistSq 대신 maxRangeSq로 검색 범위 제한
            
            float2 mPos2D = new float2(sortedMonsters[i].Position.x, sortedMonsters[i].Position.z);
            float dSq = math.distancesq(attackCenter2D, mPos2D);
            
            // OffsetCircle일 때는 타워 위치가 아닌 attackCenter를 기준으로 거리를 계산하므로,
            // IsValidTarget에 넘겨주는 distSq는 attackCenter와의 거리(dSq)를 사용합니다.
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
            if (dx * dx > maxRangeSq) break; // nearestDistSq 대신 maxRangeSq로 검색 범위 제한
            
            float2 mPos2D = new float2(sortedMonsters[i].Position.x, sortedMonsters[i].Position.z);
            float dSq = math.distancesq(attackCenter2D, mPos2D);
            
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
            float2 towerPos2D = new float2(towerPos.x, towerPos.z);
            float2 targetPos2D = new float2(targetPos.x, targetPos.z);
            float2 dirToTarget2D = math.normalize(targetPos2D - towerPos2D);
            float2 towerForward2D = math.normalize(new float2(towerForward.x, towerForward.z));
            
            // 타워의 전방 벡터와 타겟 방향 벡터 사이의 각도 계산
            float dot = math.dot(towerForward2D, dirToTarget2D);
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