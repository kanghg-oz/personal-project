using Unity.Entities;
using Unity.Mathematics;

public struct MapConfig : IComponentData
{
    public int MapWidth;
    public int MapHeight;
}

// 타일 프리팹 리스트
[InternalBufferCapacity(100)]
public struct FloorPrefabElement : IBufferElementData
{
    public Entity Value;
}

// 구조물 프리팹 리스트
[InternalBufferCapacity(100)]
public struct ObjectPrefabElement : IBufferElementData
{
    public Entity Value;
}

// 타워 프리팹 리스트
[InternalBufferCapacity(100)]
public struct TowerPrefabElement : IBufferElementData
{
    public Entity Value;
}

// 몬스터 프리팹 리스트
[InternalBufferCapacity(100)]
public struct MonsterPrefabElement : IBufferElementData
{ 
    public Entity Value; 
}

// 타일 데이터 리스트 (flattened 2D array)
[InternalBufferCapacity(500)]
public struct FloorDataElement : IBufferElementData
{
    public int Value;
}

//구조물 데이터 리스트 (flattened 2D array)
[InternalBufferCapacity(500)]
public struct ObjectDataElement : IBufferElementData
{
    public int Value;
}

// 구조물 엔티티 리스트 (flattened 2D array)
public struct ObjectEntityElement : IBufferElementData
{
    public Entity Value;
}

//거리 데이터 리스트 (flattened 2D array)
[InternalBufferCapacity(500)]
public struct DistDataElement : IBufferElementData
{
    public float Value;
}



// [추가] 부모 타일 좌표 리스트 (다음 이동 방향)
public struct PreTileDataElement : IBufferElementData
{
    public int2 Value;
}