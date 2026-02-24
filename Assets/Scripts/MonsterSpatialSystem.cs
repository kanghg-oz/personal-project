using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct MonsterSpatialSystem : ISystem
{
    private EntityQuery _monsterQuery;

    public void OnCreate(ref SystemState state)
    {
        _monsterQuery = state.GetEntityQuery(ComponentType.ReadOnly<MonsterData>(), ComponentType.ReadOnly<LocalTransform>());
        
        var singletonEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(singletonEntity, new MonsterSpatialSingleton 
        { 
            SortedMonsters = new NativeList<MonsterPosInfo>(Allocator.Persistent) 
        });
        
        state.RequireForUpdate<MonsterSpatialSingleton>();
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<MonsterSpatialSingleton>(out var singleton))
        {
            singleton.ValueRW.SortedMonsters.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var singleton = SystemAPI.GetSingletonRW<MonsterSpatialSingleton>();
        var monsters = singleton.ValueRW.SortedMonsters;
        
        monsters.Clear();
        
        if (_monsterQuery.IsEmpty) return;

        var monsterEntities = _monsterQuery.ToEntityArray(Allocator.Temp);
        var monsterTransforms = _monsterQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        
        for (int i = 0; i < monsterTransforms.Length; i++)
        {
            monsters.Add(new MonsterPosInfo
            {
                Position = monsterTransforms[i].Position,
                Entity = monsterEntities[i]
            });
        }
        
        monsters.Sort(new MonsterXComparer());
    }
}
