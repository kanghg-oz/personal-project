using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct TowerBuildSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MapConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // 1. 매니저가 없거나 건설 요청이 없으면 즉시 종료
        if (PlayerInputManager.Instance == null || !PlayerInputManager.Instance.IsBuildRequestPending)
            return;

        var input = PlayerInputManager.Instance;

        // [중요] 건설 명령을 받자마자 플래그를 먼저 끕니다. 
        // 그래야 다음 프레임에 중복 실행되지 않습니다.
        input.IsBuildRequestPending = false;

        var configEntity = SystemAPI.GetSingletonEntity<MapConfig>();
        var config = SystemAPI.GetComponent<MapConfig>(configEntity);
        //var towerPrefabs = state.EntityManager.GetBuffer<TowerPrefabElement>(configEntity);
        //var objData = state.EntityManager.GetBuffer<ObjectDataElement>(configEntity);
        //var objEntities = state.EntityManager.GetBuffer<ObjectEntityElement>(configEntity);

        int x = input.JS_X;
        int z = input.JS_Z;
        int towerIdx = input.PendingTowerIndex;
        int dataIndex = z * config.MapWidth + x;

        // 안전한 엔티티 생성을 위해 ECB 생성
        //var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        //var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // 2. 타워 생성 예약
        var towerPrefabs = state.EntityManager.GetBuffer<TowerPrefabElement>(configEntity);
        Entity towerEntity = state.EntityManager.Instantiate(towerPrefabs[towerIdx].Value);

        // 위치 및 Picking ID 설정 예약
        //ecb.SetComponent(towerEntity, LocalTransform.FromPositionRotation(new float3(x, 0.2f, z), quaternion.identity));
        
        state.EntityManager.SetComponentData(towerEntity, LocalTransform.FromPositionRotation(new float3(x, 0.2f, z), quaternion.identity));
        int uniqueId = (z * config.MapWidth + x) + 1;
        state.EntityManager.AddComponentData(towerEntity, new PickingIdColor { Value = IndexToColor(uniqueId) });

        var objData = state.EntityManager.GetBuffer<ObjectDataElement>(configEntity);
        var objEntities = state.EntityManager.GetBuffer<ObjectEntityElement>(configEntity);

        // 3. 데이터 업데이트 (버퍼 직접 수정 대신 기록)
        // [수정] 이미 구조물이 있는지 최종 체크 후 업데이트
        int towerObjId = 100 + towerIdx;
        objData[dataIndex] = new ObjectDataElement { Value = towerObjId };
        objEntities[dataIndex] = new ObjectEntityElement { Value = towerEntity };

        // 4. 매니저 동기화
        input.JS_Obj = towerObjId;
        input.SetTileData(x, z, input.JS_Floor, towerObjId);
        input.UpdatePathAt(x, z);

        Debug.Log($"<color=orange>[ECS] Tower {towerIdx} Build Success at ({x}, {z})</color>");
    }

    private static float4 IndexToColor(int id)
    {
        float r = (id & 0xFF) / 255f;
        float g = ((id >> 8) & 0xFF) / 255f;
        float b = ((id >> 16) & 0xFF) / 255f;
        return new float4(r, g, b, 1f);
    }
}