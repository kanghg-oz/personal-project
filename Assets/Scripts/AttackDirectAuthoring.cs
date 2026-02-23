using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class AttackDirectAuthoring : MonoBehaviour
{
    [Header("Basic Stats")]
    public string TowerName;
    public int Damage = 10;
    public float AttackSpeed = 1f;
    public bool Rotationable = true;

    [Header("AoE Config")]
    public bool IsAoe = false;
    public float AoERadius = 2f;

    [Header("Direct Config")]
    public float BulletSpeed = 15f;

    [Header("Visuals")]
    public GameObject MuzzleVFXPrefab; // Direct 타입은 탄환 대신 머즐/빔 등을 풀링
    public GameObject ExplosionVFX;

    public class AttackDirectBaker : Baker<AttackDirectAuthoring>
    {
        public override void Bake(AttackDirectAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            string towerName = string.IsNullOrEmpty(authoring.TowerName) ? authoring.gameObject.name : authoring.TowerName;

            AddComponent(entity, new TowerStats
            {
                Name = towerName,
                Damage = authoring.Damage,
                AttackSpeed = authoring.AttackSpeed,
                AttackTimer = 0f,
                TargetMonster = Entity.Null,
                Rotationable = authoring.Rotationable,
                LogicalRotation = quaternion.identity
            });

            AddComponent(entity, new AttackDirectData { Speed = authoring.BulletSpeed });

            if (authoring.IsAoe)
                AddComponent(entity, new AoEHitAttack { AoERadius = authoring.AoERadius });
            else
                AddComponent(entity, new SingleHitAttack());

            float maxR = 5f;
            var rangeDefault = authoring.GetComponent<RangeDefaultAuthoring>();
            if (rangeDefault != null) maxR = rangeDefault.MaxRange;
            else {
                var rangeSector = authoring.GetComponent<RangeSectorAuthoring>();
                if (rangeSector != null) maxR = rangeSector.MaxRange;
                else {
                    var rangeAnnulus = authoring.GetComponent<RangeAnnulusAuthoring>();
                    if (rangeAnnulus != null) maxR = rangeAnnulus.MaxRange;
                    else {
                        var rangeOffset = authoring.GetComponent<RangeOffsetAuthoring>();
                        if (rangeOffset != null) maxR = math.length(rangeOffset.Offset) + rangeOffset.AttackRadius;
                    }
                }
            }

            // PoolSize calculation for Direct types
            int poolSize = Mathf.Max(1, Mathf.RoundToInt((maxR / authoring.BulletSpeed) * authoring.AttackSpeed) + 1);

            AddComponent(entity, new TowerBulletPool
            {
                BulletPrefab = GetEntity(authoring.MuzzleVFXPrefab, TransformUsageFlags.Dynamic),
                PoolSize = poolSize,
                CurrentIndex = 0
            });

            float vfxDuration = 1.0f;
            if (authoring.ExplosionVFX != null)
            {
                var vfxAuth = authoring.ExplosionVFX.GetComponent<VFXAuthoring>();
                if (vfxAuth != null) vfxDuration = vfxAuth.Duration;
            }

            int vfxPoolSize = Mathf.Max(1, Mathf.CeilToInt(vfxDuration * authoring.AttackSpeed) + 1);

            AddComponent(entity, new TowerVFXPool
            {
                ExplosionPrefab = GetEntity(authoring.ExplosionVFX, TransformUsageFlags.Dynamic),
                Duration = vfxDuration,
                PoolSize = vfxPoolSize,
                CurrentIndex = 0
            });

            AddBuffer<TowerBulletElement>(entity);
            AddBuffer<TowerVFXElement>(entity);
            AddBuffer<VFXPlayRequest>(entity);
            AddBuffer<MuzzleVFXPlayRequest>(entity);
            AddComponent(entity, new TowerFireTime { Value = 0f });
            AddComponent(entity, new PickingIdColor());
        }
    }
}
