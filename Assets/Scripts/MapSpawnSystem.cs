using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using System;

public partial struct MapSpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MapConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // 1. 매니저 대기
        if (PlayerInputManager.Instance == null) return;

        state.Enabled = false;

        var entityManager = state.EntityManager;
        var configEntity = SystemAPI.GetSingletonEntity<MapConfig>();
        var config = SystemAPI.GetComponent<MapConfig>(configEntity);
        var floorPrefabs = entityManager.GetBuffer<FloorPrefabElement>(configEntity);
        var objPrefabs = entityManager.GetBuffer<ObjectPrefabElement>(configEntity);

        TextAsset floorCsv = Resources.Load<TextAsset>("map_floor");
        TextAsset objectCsv = Resources.Load<TextAsset>("map_objects");

        // CSV 파싱 (빈 줄 제거)
        string[] floorRows = floorCsv.text.Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        string[] objRows = objectCsv.text.Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        string[] firstRowCols = floorRows[0].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        int width = firstRowCols.Length;
        int height = floorRows.Length;

        config.MapWidth = width;
        config.MapHeight = height;
        entityManager.SetComponentData(configEntity, config);

        // 2. 맵 할당
        PlayerInputManager.Instance.AllocateMap(width, height);

        float4 IndexToColor(int idx)
        {
            float r = (idx & 0xFF) / 255f;
            float g = ((idx >> 8) & 0xFF) / 255f;
            float b = ((idx >> 16) & 0xFF) / 255f;
            return new float4(r, g, b, 1.0f);
        }

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // 3. 데이터 채우기 루프
        for (int z = 0; z < height; z++)
        {
            string[] fCols = floorRows[z].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string[] oCols = objRows[z].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            for (int x = 0; x < width; x++)
            {
                int uniqueId = (z * width + x) + 1;
                float4 idColor = IndexToColor(uniqueId);
                float3 pos = new float3(x, 0, z);

                int floorVal = int.Parse(fCols[x]);
                int objVal = int.Parse(oCols[x]);

                ecb.AppendToBuffer(configEntity, new ObjectDataElement { Value = objVal });
                ecb.AppendToBuffer(configEntity, new FloorDataElement { Value = floorVal });

                // 데이터 전송
                PlayerInputManager.Instance.SetTileData(x, z, floorVal, objVal);

                // 엔티티 생성
                Entity floorInstance = ecb.Instantiate(floorPrefabs[floorVal].Value);
                ecb.SetComponent(floorInstance, LocalTransform.FromPositionRotation(pos, quaternion.identity));
                ecb.AddComponent(floorInstance, new PickingIdColor { Value = idColor });
                Entity objInstance = Entity.Null;
                if (objVal > 0)
                {
                    objInstance = ecb.Instantiate(objPrefabs[objVal - 1].Value);
                    pos.y = 0.2f;
                    ecb.SetComponent(objInstance, LocalTransform.FromPositionRotation(pos, quaternion.identity));
                    ecb.AddComponent(objInstance, new PickingIdColor { Value = idColor });
                }
                ecb.AppendToBuffer(configEntity, new ObjectEntityElement { Value = objInstance });
            }
        }

        PlayerInputManager.Instance.FullUpdatePath();
        PlayerInputManager.Instance.FullSyncPathToECS();
        ecb.Playback(entityManager);
        ecb.Dispose();

    }
}