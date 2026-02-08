using Unity.Entities;
using UnityEngine;
using System.Collections.Generic; // List 사용

public class MapConfigAuthoring : MonoBehaviour
{
    public List<GameObject> FloorPrefabs;
    public List<GameObject> ObjectPrefabs;
    public List<GameObject> TowerPrefabs;
    public GameObject MonsterPrefab;

    public class Baker : Baker<MapConfigAuthoring>
    {
        public override void Bake(MapConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            // 컴포넌트 추가
            AddComponent(entity, new MapConfig());

            // 타일 프리팹 리스트 생성 및 추가
            var floorBuffer = AddBuffer<FloorPrefabElement>(entity);
            foreach (var prefab in authoring.FloorPrefabs)
            {
                floorBuffer.Add(new FloorPrefabElement { Value = GetEntity(prefab, TransformUsageFlags.Dynamic) });
            }

            // 구조물 프리팹 리스트 생성 및 추가
            var objBuffer = AddBuffer<ObjectPrefabElement>(entity);
            foreach (var prefab in authoring.ObjectPrefabs)
            {
                objBuffer.Add(new ObjectPrefabElement { Value = GetEntity(prefab, TransformUsageFlags.Dynamic) });
            }

            // 타워 프리팹 베이킹
            var towerBuffer = AddBuffer<TowerPrefabElement>(entity);
            foreach (var prefab in authoring.TowerPrefabs)
            {
                towerBuffer.Add(new TowerPrefabElement { Value = GetEntity(prefab, TransformUsageFlags.Dynamic) });
            }

            // 몬스터 프리팹 베이킹
            var monsterBuffer = AddBuffer<MonsterPrefabElement>(entity);
            monsterBuffer.Add(new MonsterPrefabElement { Value = GetEntity(authoring.MonsterPrefab, TransformUsageFlags.Dynamic) });

            // 맵 데이터 저장용 빈 리스트 생성
            AddBuffer<FloorDataElement>(entity);
            AddBuffer<ObjectDataElement>(entity);
            AddBuffer<DistDataElement>(entity);
            AddBuffer<PreTileDataElement>(entity);
            AddBuffer<ObjectEntityElement>(entity);
        }
    }
}