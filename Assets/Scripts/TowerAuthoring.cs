using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.VFX;

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

public enum TowerAttackType
{
    Default,
    Direct,
    Aoe
}

public class TowerAuthoring : MonoBehaviour
{
    [Header("Basic Stats")]
    public string TowerName;
    public int Damage = 10;
    public float AttackSpeed = 1f;
    public TowerRangeType RangeType = TowerRangeType.Default;
    public TowerAttackType AttackType = TowerAttackType.Default;
    public float AttackArea = 2f; // Aoe 타입일 때 피해를 입히는 지름
    public bool Rotationable = true; // 타워가 타겟을 바라보도록 회전할지 여부

    [Header("Range Config (Fill only what's needed)")]
    public float MaxRange = 5f;
    public float MinRange = 0f;    // Annulus용
    public float RangeAngle = 360f; // Sector용
    public Vector3 RangeOffset;    // Offset용

    [Header("Bullet Config")]
    public float BulletSpeed = 10f;
    public float BulletHeight = 1f;

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

            float bulletSpeed = authoring.BulletSpeed;
            float bulletHeight = authoring.BulletHeight;

            // 폭발 VFX 재생 시간 가져오기
            float vfxDuration = 1.0f; // 기본값
            if (authoring.ExplosionVFX != null)
            {
                var vfxAuth = authoring.ExplosionVFX.GetComponent<VFXAuthoring>();
                if (vfxAuth != null)
                {
                    vfxDuration = vfxAuth.Duration;
                }
            }

            // 풀 크기 계산: (타워 공격 속도 / 탄환 속도) * 여유분
            // 탄환이 목표에 도달하는 데 걸리는 최대 시간 = MaxRange / bulletSpeed
            // 그 시간 동안 발사되는 탄환 수 = 최대 시간 * AttackSpeed
            int poolSize = Mathf.CeilToInt((maxR / bulletSpeed) * spd) + 1;
            // 최소 1개는 보장
            poolSize = Mathf.Max(1, poolSize);

            // VFX 풀 크기 계산: (VFX 재생 시간 * AttackSpeed) + 여유분
            int vfxPoolSize = Mathf.CeilToInt(vfxDuration * spd) + 1;
            vfxPoolSize = Mathf.Max(1, vfxPoolSize);

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
                AttackType = authoring.AttackType,
                AttackArea = authoring.AttackArea,
                Rotationable = authoring.Rotationable,
                BulletPrefab = GetEntity(authoring.BulletPrefab, TransformUsageFlags.Dynamic),
                ExplosionPrefab = GetEntity(authoring.ExplosionVFX, TransformUsageFlags.Dynamic),
                TargetMonster = Entity.Null,
                AttackTimer = 0f,
                LogicalRotation = quaternion.identity, // 초기화 시점에는 기본값, 런타임에 설정됨
                BulletSpeed = bulletSpeed,
                BulletHeight = bulletHeight,
                PoolSize = poolSize,
                CurrentBulletIndex = 0,
                VFXDuration = vfxDuration,
                VFXPoolSize = vfxPoolSize,
                CurrentVFXIndex = 0
            };

            AddComponent(entity, data);
            
            // 탄환 풀을 저장할 버퍼 추가
            AddBuffer<TowerBulletElement>(entity);
            
            // VFX 풀을 저장할 버퍼 추가
            AddBuffer<TowerVFXElement>(entity);

            // VFX 재생 요청 버퍼 추가
            AddBuffer<VFXPlayRequest>(entity);
            
            // 발사 VFX 재생 요청 버퍼 추가 (Direct 타입용)
            AddBuffer<MuzzleVFXPlayRequest>(entity);
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
    public TowerAttackType AttackType;
    public float AttackArea;
    public bool Rotationable;

    // --- Assets (referenced) ---
    public Entity BulletPrefab;    
    public Entity ExplosionPrefab; 

    // --- Runtime States (가변값) ---
    public Entity TargetMonster;
    public float AttackTimer;
    public quaternion LogicalRotation; // 로직(사거리 계산 등)에 사용되는 고정된 방향
    
    // --- Bullet Pool Info ---
    public float BulletSpeed;
    public float BulletHeight;
    public int PoolSize;
    public int CurrentBulletIndex;
    
    // --- VFX Pool Info ---
    public float VFXDuration;
    public int VFXPoolSize;
    public int CurrentVFXIndex;
}

[InternalBufferCapacity(10)]
public struct VFXPlayRequest : IBufferElementData
{
    public Entity VfxEntity;
}

[InternalBufferCapacity(10)]
public struct MuzzleVFXPlayRequest : IBufferElementData
{
    public Entity VfxEntity;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class TowerVFXSystem : SystemBase
{
    protected override void OnUpdate()
    {
        foreach (var (requests, muzzleRequests) in SystemAPI.Query<
            DynamicBuffer<VFXPlayRequest>,
            DynamicBuffer<MuzzleVFXPlayRequest>>())
        {
            // 폭발 VFX 처리
            for (int i = 0; i < requests.Length; i++)
            {
                Entity vfxEntity = requests[i].VfxEntity;

                // Play VisualEffect
                if (EntityManager.HasComponent<VisualEffect>(vfxEntity))
                {
                    var localTransform = EntityManager.GetComponentData<Unity.Transforms.LocalTransform>(vfxEntity);
                    var vfx = EntityManager.GetComponentObject<VisualEffect>(vfxEntity);
                    vfx.transform.position = localTransform.Position; // Sync GameObject transform immediately
                    vfx.Play();
                }
            }
            requests.Clear();

            // 발사 VFX 처리 (Direct 타입)
            for (int i = 0; i < muzzleRequests.Length; i++)
            {
                Entity muzzleEntity = muzzleRequests[i].VfxEntity;

                // Play VisualEffect
                if (EntityManager.HasComponent<VisualEffect>(muzzleEntity))
                {
                    var localTransform = EntityManager.GetComponentData<Unity.Transforms.LocalTransform>(muzzleEntity);
                    var vfx = EntityManager.GetComponentObject<VisualEffect>(muzzleEntity);
                    vfx.transform.position = localTransform.Position; // Sync GameObject transform immediately
                    vfx.Play();
                }
            }
            muzzleRequests.Clear();
        }
    }
}

// 타워가 소유한 탄환 엔티티들을 저장하는 버퍼
[InternalBufferCapacity(10)]
public struct TowerBulletElement : IBufferElementData
{
    public Entity Value;
}

// 타워가 소유한 폭발 VFX 엔티티들을 저장하는 버퍼
[InternalBufferCapacity(10)]
public struct TowerVFXElement : IBufferElementData
{
    public Entity Value;
}
