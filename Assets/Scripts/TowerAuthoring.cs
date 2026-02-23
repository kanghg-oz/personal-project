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
    public int Damage = 10;
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

            int dmg = authoring.Damage;
            float spd = authoring.AttackSpeed;
            float maxR = authoring.MaxRange;
            float minR = authoring.MinRange;

            string towerName = string.IsNullOrEmpty(authoring.TowerName)
                ? authoring.gameObject.name
                : authoring.TowerName;

            // 탄환 속도 가져오기 (BulletPrefab에 BulletAuthoring이 있다고 가정)
            float bulletSpeed = 10f; // 기본값
            if (authoring.BulletPrefab != null)
            {
                var bulletAuth = authoring.BulletPrefab.GetComponent<BulletAuthoring>();
                if (bulletAuth != null)
                {
                    bulletSpeed = bulletAuth.Speed;
                }
            }

            // 풀 크기 계산: (타워 공격 속도 / 탄환 속도) * 여유분
            // 탄환이 목표에 도달하는 데 걸리는 최대 시간 = MaxRange / bulletSpeed
            // 그 시간 동안 발사되는 탄환 수 = 최대 시간 * AttackSpeed
            int poolSize = Mathf.CeilToInt((maxR / bulletSpeed) * spd) + 1;
            // 최소 1개는 보장
            poolSize = Mathf.Max(1, poolSize);

            TowerData data = new TowerData
            {
                Name = towerName,
                Damage = dmg,
                AttackSpeed = spd,
                MaxRange = maxR,
                MinRange = minR,
                RangeAngle = authoring.RangeAngle,
                RangeOffset = authoring.RangeOffset,
                RangeType = authoring.RangeType,
                BulletPrefab = GetEntity(authoring.BulletPrefab, TransformUsageFlags.Dynamic),
                ExplosionPrefab = GetEntity(authoring.ExplosionVFX, TransformUsageFlags.Dynamic),
                TargetMonster = Entity.Null,
                AttackTimer = 0f,
                BulletSpeed = bulletSpeed,
                PoolSize = poolSize,
                CurrentBulletIndex = 0
            };

            AddComponent(entity, data);
            
            // 탄환 풀을 저장할 버퍼 추가
            AddBuffer<TowerBulletElement>(entity);
        }
    }
}

public struct TowerData : IComponentData
{
    // --- Stats (불변/설정값) ---
    public FixedString64Bytes Name;
    public int Damage;
    public float AttackSpeed;
    public float MaxRange;
    public float MinRange;
    public float RangeAngle;
    public float3 RangeOffset;
    public TowerRangeType RangeType;

    // --- Assets (referenced) ---
    public Entity BulletPrefab;    
    public Entity ExplosionPrefab; 

    // --- Runtime States (가변값) ---
    public Entity TargetMonster;
    public float AttackTimer;
    
    // --- Bullet Pool Info ---
    public float BulletSpeed;
    public int PoolSize;
    public int CurrentBulletIndex;
}

// 타워가 소유한 탄환 엔티티들을 저장하는 버퍼
[InternalBufferCapacity(10)]
public struct TowerBulletElement : IBufferElementData
{
    public Entity Value;
}
