using Unity.Entities;
using Unity.Burst;

[UpdateAfter(typeof(FireSystem))]
[BurstCompile]
public partial struct MonsterDeathSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerStats>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);
        
        var stats = SystemAPI.GetSingletonRW<PlayerStats>();

        foreach (var (monster, entity) in SystemAPI.Query<RefRO<MonsterData>>().WithEntityAccess())
        {
            if (monster.ValueRO.HP <= 0)
            {
                stats.ValueRW.Gold += monster.ValueRO.GoldReward;
                ecb.DestroyEntity(entity);
            }
        }
    }
}
