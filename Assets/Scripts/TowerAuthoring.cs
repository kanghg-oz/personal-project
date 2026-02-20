using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

[MaterialProperty("_FireTime")]
public struct TowerFireTime : IComponentData
{
    public float Value;
}

public enum TowerRangeType
{
    Default,      // 일반 원형
    Sector,       // 부채꼴 (각도)
    Annulus,      // 동심원 (최소 사거리)
    OffsetCircle,  // 중심점 이동
    Wall          // 벽
}

public class TowerAuthoring : MonoBehaviour
{
    [Header("Basic Stats")]
    public string TowerName;
    public float Damage = 10f;
    public float AttackSpeed = 1f;
    public TowerRangeType RangeType = TowerRangeType.Default;

    [Header("Range Config (Fill only what's needed)")]
    public float MaxRange = 5f;
    public float MinRange = 0f;    // Annulus용
    public float RangeAngle = 360f; // Sector용
    public Vector3 RangeOffset;    // Offset용

    [Header("Attack Visuals")]
    public GameObject BulletPrefab;
    public GameObject ExplosionVFX; 

    public class TowerBaker : Baker<TowerAuthoring>
    {
        public override void Bake(TowerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new PickingIdColor());
            AddComponent(entity, new TowerFireTime { Value = 0f });

            float dmg = authoring.Damage;
            float spd = authoring.AttackSpeed;
            float maxR = authoring.MaxRange;
            float minR = authoring.MinRange;

            string towerName = string.IsNullOrEmpty(authoring.TowerName)
                ? authoring.gameObject.name
                : authoring.TowerName;

            TowerData data = new TowerData
            {
                Name = towerName,
                Damage = dmg,
                AttackSpeed = spd,
                RangeType = authoring.RangeType,
                MaxRange = maxR,
                MinRange = minR,
                RangeAngle = authoring.RangeAngle,
                RangeOffset = authoring.RangeOffset,
                BulletPrefab = GetEntity(authoring.BulletPrefab, TransformUsageFlags.Dynamic),
                ExplosionPrefab = GetEntity(authoring.ExplosionVFX, TransformUsageFlags.Dynamic),
                TargetMonster = Entity.Null,
                AttackTimer = 0f
            };

            AddComponent(entity, data);
        }
    }
}

public struct TowerData : IComponentData
{
    // --- Stats (불변/설정값) ---
    public FixedString64Bytes Name;
    public float Damage;
    public float AttackSpeed;
    public float MaxRange;
    public float MinRange;
    public float RangeAngle;
    public float3 RangeOffset;
    public TowerRangeType RangeType;

    // --- Assets (참조) ---
    public Entity BulletPrefab;    
    public Entity ExplosionPrefab; 

    // --- Runtime States (가변값) ---
    public Entity TargetMonster;
    public float AttackTimer;
}
