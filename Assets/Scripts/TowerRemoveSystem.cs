using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct TowerRemoveSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (PlayerInputManager.Instance == null || !PlayerInputManager.Instance.IsRemoveRequestPending)
            return;

        var input = PlayerInputManager.Instance;
        input.IsRemoveRequestPending = false;

        var configEntity = SystemAPI.GetSingletonEntity<MapConfig>();
        var objData = state.EntityManager.GetBuffer<ObjectDataElement>(configEntity);
        var objEntities = state.EntityManager.GetBuffer<ObjectEntityElement>(configEntity);
        int dataIndex = input.JS_Z * SystemAPI.GetComponent<MapConfig>(configEntity).MapWidth + input.JS_X;

        // 1. 엔티티 파괴
        Entity targetEntity = objEntities[dataIndex].Value;
        if (targetEntity != Entity.Null && state.EntityManager.Exists(targetEntity))
        {
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            ecb.DestroyEntity(targetEntity);
            objEntities[dataIndex] = new ObjectEntityElement { Value = Entity.Null };
        }

        // 2. 데이터 및 경로 초기화
        objData[dataIndex] = new ObjectDataElement { Value = 0 };
        input.JS_Obj = 0;
        input.SetTileData(input.JS_X, input.JS_Z, input.JS_Floor, 0);

        // 경로가 다시 뚫릴 수 있으므로 전체 업데이트 호출
        //input.FullUpdatePath();
        //input.FullSyncPathToECS();
        input.UpdatePathAt(input.JS_X, input.JS_Z);

        Debug.Log($"<color=red>[ECS] Object Removed at ({input.JS_X}, {input.JS_Z})</color>");
    }
}