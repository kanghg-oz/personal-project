using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public partial struct MonsterMoveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // MapConfig 엔티티가 생성되기 전까지는 OnUpdate를 실행하지 않음
        state.RequireForUpdate<MapConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
       
        var configEntity = SystemAPI.GetSingletonEntity<MapConfig>();
        var config = SystemAPI.GetComponent<MapConfig>(configEntity);

        var preTileBuffer = state.EntityManager.GetBuffer<PreTileDataElement>(configEntity);
        var distBuffer = state.EntityManager.GetBuffer<DistDataElement>(configEntity);

        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (transform, move) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<MonsterMoveData>>())
        {
            float3 currentPos = transform.ValueRO.Position;

            // [상태 1] 맵 밖에서 안으로 진입
            if (!move.ValueRO.IsInsideMap)
            {
                // 맵 내부 판정 (경계 포함)
                bool insideX = currentPos.x >= 0 && currentPos.x < config.MapWidth-1;
                bool insideZ = currentPos.z >= 0 && currentPos.z < config.MapHeight-1;

                if (insideX && insideZ)
                {
                    move.ValueRW.IsInsideMap = true;
                    move.ValueRW.HasTarget = false; // 진입 성공! 이제 내부 길찾기 시작
                }
                else
                {
                    // [수정] 맵 크기에 맞춰 동적으로 "가장 끝 타일의 정중앙(Integer)"으로 진입 유도
                    // 예: 10x10 맵이면 9.5가 아니라 9.0을 향해 이동
                    float maxX = config.MapWidth - 1.0f;
                    float maxY = config.MapHeight - 1.0f;

                    float2 clipped = math.clamp(currentPos.xz, 0, new float2(maxX, maxY));
                    float3 entryTarget = new float3(clipped.x, 0.2f, clipped.y);

                    float3 dir = math.normalize(entryTarget - currentPos);
                    transform.ValueRW.Position += dir * move.ValueRO.Speed * dt;

                    if (!dir.Equals(float3.zero))
                        transform.ValueRW.Rotation = quaternion.LookRotationSafe(dir, math.up());

                    continue; // 진입 중에는 아래 로직 건너뜀
                }
            }

            // [상태 2] 맵 내부 이동 (Waypoint 방식)
            float distToTarget = math.distance(currentPos, move.ValueRO.CurrentTargetPos);

            // 목표가 없거나 도착했을 때
            if (!move.ValueRO.HasTarget || distToTarget < 0.05f)
            {
                int2 myGridPos = (int2)math.round(currentPos.xz);

                // [안전 장치] 진입 직후(9.9) 반올림(10)으로 인한 인덱스 초과 방지
                myGridPos = math.clamp(myGridPos, 0, new int2(config.MapWidth - 1, config.MapHeight - 1));

                int idx = myGridPos.y * config.MapWidth + myGridPos.x;

                if (idx >= 0 && idx < preTileBuffer.Length)
                {
                    int2 nextTile = preTileBuffer[idx].Value;
                    float distVal = distBuffer[idx].Value;

                    if (distVal > 0 && nextTile.x != -1)
                    {
                        // 다음 타일의 중앙으로 목표 설정
                        move.ValueRW.CurrentTargetPos = new float3(nextTile.x, 0.2f, nextTile.y);
                        move.ValueRW.HasTarget = true;
                    }
                    else if (distVal == 0) // Goal 도착
                    {
                        // (필요 시 여기서 엔티티 파괴 또는 도착 처리)
                        continue;
                    }
                    else // 길이 막힘
                    {
                        move.ValueRW.HasTarget = false;
                        continue;
                    }
                }
            }

            // 이동 실행
            if (move.ValueRO.HasTarget)
            {
                float3 dir = math.normalize(move.ValueRO.CurrentTargetPos - currentPos);
                transform.ValueRW.Position += dir * move.ValueRO.Speed * dt;

                if (!dir.Equals(float3.zero))
                    transform.ValueRW.Rotation = quaternion.LookRotationSafe(dir, math.up());
            }
        }
    }
}