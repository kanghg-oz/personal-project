using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;

public partial struct MonsterSpawnSystem : ISystem
{
    private int _spawnedCount;

    public void OnCreate(ref SystemState state)
    {
        // MapConfig 엔티티가 생성되기 전까지는 OnUpdate를 실행하지 않음
        state.RequireForUpdate<MapConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var input = PlayerInputManager.Instance;
        if (input == null || input.GamePhase != 1) return;

        // 한 번만 스폰하도록 설정 (실제 게임에서는 타이머나 웨이브 로직 사용)
        input.GamePhase = 2; // 스폰 완료 상태로 변경

        TextAsset spawnCsv = Resources.Load<TextAsset>("spawn_pos");
        string[] coords = spawnCsv.text.Trim().Split(',');

        var configEntity = SystemAPI.GetSingletonEntity<MapConfig>();
        var config = SystemAPI.GetComponent<MapConfig>(configEntity);
        var monsterPrefab = state.EntityManager.GetBuffer<MonsterPrefabElement>(configEntity)[0].Value;
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        int totalTiles = config.MapWidth * config.MapHeight;

        for (int i = 0; i < coords.Length; i += 2)
        {
            float x = float.Parse(coords[i]);
            float z = float.Parse(coords[i + 1]);

            Entity monster = ecb.Instantiate(monsterPrefab);
            ecb.SetComponent(monster, LocalTransform.FromPosition(new float3(x, 0.2f, z)));

            // 몬스터 ID 부여 (타일 수 + 순번 + 1)
            int monsterId = totalTiles + (++_spawnedCount);
            ecb.AddComponent(monster, new PickingIdColor { Value = IndexToColor(monsterId) });

            // 이동 데이터 추가
            ecb.AddComponent(monster, new MonsterMoveData { Speed = 1.0f });
        }
    }

    private static float4 IndexToColor(int id)
    {
        return new float4((id & 0xFF) / 255f, ((id >> 8) & 0xFF) / 255f, ((id >> 16) & 0xFF) / 255f, 1f);
    }
}