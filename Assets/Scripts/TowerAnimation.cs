using Unity.Entities;
using Unity.Rendering;

// 구조체 전체가 셰이더의 _AnimParams(Vector 4) 프로퍼티 하나가 됩니다.
[MaterialProperty("_AnimParams")]
public struct TowerAnimation : IComponentData
{
    // 필드 선언 순서가 곧 셰이더의 R, G, B, A 채널이 됩니다.

    public float LastFireTime; // R (x) 채널 매핑
    public float Speed;        // G (y) 채널 매핑
    public float Timer;        // B (z) 채널 매핑 (셰이더로 가지만 로직용으로 사용)
    public float Padding;      // A (w) 채널 매핑 (16바이트 크기를 맞추기 위한 여분)
}