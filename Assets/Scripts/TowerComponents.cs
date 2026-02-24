using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;

public enum TowerAttackType
{
    Projectile,
    Direct,
    Hit
}

public enum TowerRangeType
{
    Default,
    Sector,
    Annulus,
    OffsetCircle,
    Wall
}

public struct TowerStats : IComponentData
{
    public FixedString64Bytes Name;
    public int Damage;
    public float AttackSpeed;
    public float AttackTimer;
    public Entity TargetMonster;
    public bool Rotationable;
    public quaternion LogicalRotation;
}

[MaterialProperty("_FireTime")]
public struct TowerFireTime : IComponentData
{
    public float Value;
}

public struct TowerBulletPool : IComponentData
{
    public Entity BulletPrefab;
    public int PoolSize;
    public int CurrentIndex;
}

public struct AttackDirectData : IComponentData
{
    public float Speed;
}

public struct AttackHitData : IComponentData { }

public struct AttackProjectileData : IComponentData
{
    public float Speed;
    public float Height;
}

public struct TowerVFXPool : IComponentData
{
    public Entity ExplosionPrefab;
    public float Duration;
    public int PoolSize;
    public int CurrentIndex;
    public float VFXScale;
}

public struct VFXScaleProperty : IComponentData
{
    public float Value;
}

// Range Components
public struct TowerRangeDefault : IComponentData { public float MaxRange; }
public struct TowerRangeSector : IComponentData { public float MaxRange; public float Angle; }
public struct TowerRangeAnnulus : IComponentData { public float MaxRange; public float MinRange; }
public struct TowerRangeOffset : IComponentData 
{ 
    public float MaxRange;      // Offset이 이동 가능한 최대 거리 (Movement Limit)
    public float AttackRadius;  // Offset 위치에서 타겟을 찾는 탐색 반경 (Targeting Radius)
    public float3 Offset; 
}

// Attack Logic Components
public struct SingleHitAttack : IComponentData { }
public struct AoEHitAttack : IComponentData { public float AoERadius; }

public struct SingleDamageRequest : IComponentData
{
    public int Damage;
    public Entity TargetEntity;
    public float3 ImpactPosition;
    public Entity TowerEntity;
}

public struct AoEDamageRequest : IComponentData
{
    public int Damage;
    public float3 TargetPosition;
    public float Radius;
    public Entity TowerEntity;
}

// Buffers
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

[InternalBufferCapacity(10)]
public struct TowerBulletElement : IBufferElementData
{
    public Entity Value;
}

[InternalBufferCapacity(10)]
public struct TowerVFXElement : IBufferElementData
{
    public Entity Value;
}

