using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public partial struct MonsterMoveSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // MapConfig 엔티티가 생성되기 전까지는 OnUpdate를 실행하지 않음
        state.RequireForUpdate<MapConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // 1. 필요한 싱글톤 및 버퍼 데이터 가져오기 (Burst 호환 방식)
        var configEntity = SystemAPI.GetSingletonEntity<MapConfig>();
        var config = SystemAPI.GetSingleton<MapConfig>();

        // [최적화] state.EntityManager 대신 SystemAPI를 사용하여 Burst 호환성 확보
        var preTileBuffer = SystemAPI.GetBuffer<PreTileDataElement>(configEntity);
        var distBuffer = SystemAPI.GetBuffer<DistDataElement>(configEntity);

        // [추가] 엔티티 파괴를 위한 ECB 생성 (EndSimulation 시점에 일괄 처리)
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);

        float dt = SystemAPI.Time.DeltaTime;

        // [최적화] .WithEntityAccess()를 추가하여 파괴할 엔티티 ID에 접근
        foreach (var (transform, move, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<MonsterMoveData>>().WithEntityAccess())
        {
            float3 currentPos = transform.ValueRO.Position;

            // [상태 1] 맵 밖에서 안으로 진입
            if (!move.ValueRO.IsInsideMap)
            {
                bool insideX = currentPos.x >= 0 && currentPos.x < config.MapWidth - 1;
                bool insideZ = currentPos.z >= 0 && currentPos.z < config.MapHeight - 1;

                if (insideX && insideZ)
                {
                    move.ValueRW.IsInsideMap = true;
                    move.ValueRW.HasTarget = false;
                }
                else
                {
                    float maxX = config.MapWidth - 1.0f;
                    float maxY = config.MapHeight - 1.0f;
                    float2 clipped = math.clamp(currentPos.xz, 0, new float2(maxX, maxY));
                    float3 entryTarget = new float3(clipped.x, 0.2f, clipped.y);

                    float3 dir = math.normalize(entryTarget - currentPos);
                    transform.ValueRW.Position += dir * move.ValueRO.Speed * dt;

                    if (!dir.Equals(float3.zero))
                        transform.ValueRW.Rotation = quaternion.LookRotationSafe(dir, math.up());

                    continue;
                }
            }

            // [상태 2] 맵 내부 이동
            float distToTarget = math.distance(currentPos, move.ValueRO.CurrentTargetPos);

            if (!move.ValueRO.HasTarget || distToTarget < 0.05f)
            {
                int2 myGridPos = (int2)math.round(currentPos.xz);
                myGridPos = math.clamp(myGridPos, 0, new int2(config.MapWidth - 1, config.MapHeight - 1));

                int idx = myGridPos.y * config.MapWidth + myGridPos.x;

                if (idx >= 0 && idx < preTileBuffer.Length)
                {
                    int2 nextTile = preTileBuffer[idx].Value;
                    float distVal = distBuffer[idx].Value;

                    if (distVal > 0 && nextTile.x != -1)
                    {
                        move.ValueRW.CurrentTargetPos = new float3(nextTile.x, 0.2f, nextTile.y) + move.ValueRO.Offset;
                        move.ValueRW.HasTarget = true;
                    }
                    else if (distVal == 0) // Goal 도착
                    {
                        // [기능 추가] 목적지 도착 시 엔티티 파괴 명령 예약
                        ecb.DestroyEntity(entity);
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