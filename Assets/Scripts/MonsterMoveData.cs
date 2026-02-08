using Unity.Entities;
using Unity.Mathematics;

public struct MonsterMoveData : IComponentData
{
    public float Speed;

    // 웨이포인트 이동을 위한 상태 변수들
    public float3 CurrentTargetPos; // 현재 이동 중인 목표 지점 (타일 중앙)
    public bool HasTarget;          // 목표가 설정되었는가?
    public bool IsInsideMap;        // 맵 안에 진입했는가?
}