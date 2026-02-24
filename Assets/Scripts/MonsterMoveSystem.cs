using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public partial struct MonsterMoveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MapConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var configEntity = SystemAPI.GetSingletonEntity<MapConfig>();
        var config = SystemAPI.GetSingleton<MapConfig>();
        var preTileBuffer = SystemAPI.GetBuffer<PreTileDataElement>(configEntity);
        var distBuffer = SystemAPI.GetBuffer<DistDataElement>(configEntity);
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);

        float dt = SystemAPI.Time.DeltaTime;

        // MonsterMoveData 대신 MonsterData를 사용
        foreach (var (transform, monster, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<MonsterData>>().WithEntityAccess())
        {
            float3 currentPos = transform.ValueRO.Position;
            float distToTarget = math.distance(currentPos, monster.ValueRO.CurrentTargetPos);

            // 목적지에 가깝거나 목표가 없는 경우 다음 타겟 설정
            if (!monster.ValueRO.HasTarget || distToTarget < 0.1f)
            {
                int2 myGridPos = (int2)math.round(currentPos.xz);
                
                // 맵 범위 내에 있는지 확인
                bool isInside = myGridPos.x >= 0 && myGridPos.x < config.MapWidth && 
                               myGridPos.y >= 0 && myGridPos.y < config.MapHeight;

                float distVal;
                int2 nextTile = new int2(-1, -1);

                if (isInside)
                {
                    int idx = myGridPos.y * config.MapWidth + myGridPos.x;
                    distVal = distBuffer[idx].Value;
                    nextTile = preTileBuffer[idx].Value;
                }
                else
                {
                    // 맵 밖에서는 거리를 무한대로 설정하고 맵 경계(진입점)를 다음 목표로 설정
                    distVal = float.PositiveInfinity;
                    nextTile = new int2(
                        math.clamp(myGridPos.x, 0, config.MapWidth - 1),
                        math.clamp(myGridPos.y, 0, config.MapHeight - 1)
                    );
                }

                if (distVal > 0) // 이동 중이거나 진입 대기 중
                {
                    monster.ValueRW.CurrentTargetPos = new float3(nextTile.x, 0.2f, nextTile.y) + monster.ValueRO.Offset;
                    monster.ValueRW.HasTarget = true;
                }
                else if (distVal == 0) // Goal 도착
                {
                    if (SystemAPI.HasSingleton<PlayerStats>())
                    {
                        var stats = SystemAPI.GetSingleton<PlayerStats>();
                        stats.HP -= monster.ValueRO.DamageToPlayer;
                        SystemAPI.SetSingleton(stats);
                    }
                    ecb.DestroyEntity(entity);
                    continue;
                }
                else // 경로 끊김
                {
                    monster.ValueRW.HasTarget = false;
                    continue;
                }
            }

            // 이동 실행
            if (monster.ValueRO.HasTarget)
            {
                float3 dir = math.normalize(monster.ValueRO.CurrentTargetPos - currentPos);
                if (!dir.Equals(float3.zero))
                {
                    transform.ValueRW.Position += dir * monster.ValueRO.Speed * dt;
                    transform.ValueRW.Rotation = quaternion.LookRotationSafe(dir, math.up());
                }
            }
        }
    }
}