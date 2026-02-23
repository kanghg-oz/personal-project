using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

public class BulletAuthoring : MonoBehaviour
{
    public float Speed = 10f;
    public float Length = 1f;
    public float Height = 1f;
    public int Damage = 10;

    public class BulletBaker : Baker<BulletAuthoring>
    {
        public override void Bake(BulletAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new BulletData
            {
                StartPos = float3.zero,
                EndPos = float3.zero,
                Speed = authoring.Speed,
                Timer = 0f,
                Damage = authoring.Damage,
                TargetEntity = Entity.Null
            });

            AddComponent(entity, new BulletAnimation
            {
                LastFireTime = 0f,
                Speed = authoring.Speed,
                Length = authoring.Length,
                Height = authoring.Height
            });
        }
    }
}

public struct BulletData : IComponentData
{
    public float3 StartPos;
    public float3 EndPos;
    public float Speed;
    public float Timer; // 0에서 1까지 증가
    public int Damage;
    public Entity TargetEntity;
}

[MaterialProperty("_TimeSpeedLengthHeight")]
public struct BulletAnimation : IComponentData
{
    // 필드 선언 순서가 곧 셰이더의 R, G, B, A 채널이 됩니다.

    public float LastFireTime; // R (x) 채널 매핑
    public float Speed;        // G (y) 채널 매핑
    public float Length;        // B (z) 채널 매핑 (셰이더로 가지만 로직용으로 사용)
    public float Height;      // A (w) 채널 매핑 (16바이트 크기를 맞추기 위한 여분)
}
