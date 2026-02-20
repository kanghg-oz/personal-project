using Unity.Entities;
using Unity.Mathematics;

public struct BulletData : IComponentData
{
    public float3 StartPos;
    public float3 EndPos;
    public float Speed;
    public float Timer; // 0에서 1까지 증가
}