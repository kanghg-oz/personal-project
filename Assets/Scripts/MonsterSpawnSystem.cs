using System;
using Unity.Burst;
using Unity.Collections; // NativeList 사용을 위해 추가
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public partial struct MonsterSpawnSystem : ISystem
{
    private int _spawnCount;
    private float _timer;
    private int _currentWaveIdx;
    private bool _isInitialized;

    struct MonsterWave
    {
        public int monsterIdx;
        public int monsterNum;
        public float interval;
    }

    // [중요] ISystem 구조체 내부에는 managed 객체(List, string[])를 필드로 둘 수 없습니다.
    // 따라서 NativeList를 사용하거나 OnUpdate 내부에서 지역 변수로 처리해야 합니다.
    private NativeList<MonsterWave> _waves;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MapConfig>();
        // NativeList 초기화
        _waves = new NativeList<MonsterWave>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        // NativeList 메모리 해제
        if (_waves.IsCreated) _waves.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        var input = PlayerInputManager.Instance;
        if (input == null || input.GamePhase != 1) return;

        if (!_isInitialized)
        {
            InitializeWaves(ref state);
            _isInitialized = true;
            _timer = _waves.Length > 0 ? _waves[0].interval : 0;
            return;
        }

        if (_currentWaveIdx >= _waves.Length)
        {
            input.GamePhase = 2;
            return;
        }

        _timer -= SystemAPI.Time.DeltaTime;

        if (_timer <= 0)
        {
            SpawnCurrentWave(ref state);

            _currentWaveIdx++;
            if (_currentWaveIdx < _waves.Length)
            {
                _timer = _waves[_currentWaveIdx].interval;
            }
        }
    }

    private void InitializeWaves(ref SystemState state)
    {
        _waves.Clear();
        TextAsset monsterCsv = Resources.Load<TextAsset>("monster_list");
        string[] lines = monsterCsv.text.Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            string[] cols = line.Split(',');
            if (cols.Length >= 3)
            {
                _waves.Add(new MonsterWave
                {
                    monsterIdx = int.Parse(cols[0]),
                    monsterNum = int.Parse(cols[1]),
                    interval = float.Parse(cols[2])
                });
            }
        }
        
    }

    private void SpawnCurrentWave(ref SystemState state)
    {
        var configEntity = SystemAPI.GetSingletonEntity<MapConfig>();
        var config = SystemAPI.GetComponent<MapConfig>(configEntity);
        var monsterPrefabs = state.EntityManager.GetBuffer<MonsterPrefabElement>(configEntity);
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        // 스폰 좌표는 OnUpdate 시점에 잠깐 읽어오도록 처리 (구조체 필드 제약 회피)
        TextAsset spawnCsv = Resources.Load<TextAsset>("spawn_pos");
        if (spawnCsv == null) return;
        string[] spawnCoords = spawnCsv.text.Trim().Split(',');

        var wave = _waves[_currentWaveIdx];
        if (wave.monsterIdx >= monsterPrefabs.Length) return;

        Entity prefab = monsterPrefabs[wave.monsterIdx].Value;
        int totalTiles = config.MapWidth * config.MapHeight;
        var random = new Unity.Mathematics.Random((uint)(_spawnCount + 1 + _currentWaveIdx));

        for (int i = 0; i < spawnCoords.Length; i += 2)
        {
            float baseX = float.Parse(spawnCoords[i]);
            float baseZ = float.Parse(spawnCoords[i + 1]);

            for (int n = 0; n < wave.monsterNum; n++)
            {
                Entity monster = ecb.Instantiate(prefab);
                float3 offset = new float3(random.NextFloat(-0.3f, 0.3f), 0, random.NextFloat(-0.3f, 0.3f));
                float3 spawnPos = new float3(baseX, 0.2f, baseZ) + offset;

                ecb.SetComponent(monster, LocalTransform.FromPosition(spawnPos));

                int monsterId = totalTiles + (++_spawnCount);
                ecb.AddComponent(monster, new PickingIdColor { Value = IndexToColor(monsterId) });
                ecb.AddComponent(monster, new MonsterMoveData
                {
                    Speed = 1.0f,
                    IsInsideMap = false,
                    HasTarget = false,
                    CurrentTargetPos = spawnPos,
                    Offset = offset
                });
            }
        }
    }

    private float4 IndexToColor(int index)
    {
        int r = (index >> 16) & 0xFF;
        int g = (index >> 8) & 0xFF;
        int b = index & 0xFF;
        return new float4(r / 255f, g / 255f, b / 255f, 1.0f);
    }
}