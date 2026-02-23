using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class AttackHitAuthoring : MonoBehaviour
{
    [Header("Basic Stats")]
    public string TowerName;
    public int Damage = 10;
    public float AttackSpeed = 1f;
    public bool Rotationable = true;

    [Header("AoE Config")]
    public bool IsAoe = false;
    public float AoERadius = 2f;

    [Header("Visuals")]
    public GameObject MuzzleVFXPrefab;
    public GameObject ExplosionVFX;

    public class AttackHitBaker : Baker<AttackHitAuthoring>
    {
        public override void Bake(AttackHitAuthoring authoring)
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

            AddComponent(entity, new AttackHitData());

            if (authoring.IsAoe)
                AddComponent(entity, new AoEHitAttack { AoERadius = authoring.AoERadius });
            else
                AddComponent(entity, new SingleHitAttack());

            int poolSize = Mathf.Max(1, Mathf.RoundToInt(authoring.AttackSpeed) + 1);

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
