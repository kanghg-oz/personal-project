using Unity.Entities;

public struct PlayerStats : IComponentData
{
    public int Gold;
    public int HP;
}

public struct TowerCost : IComponentData
{
    public int BuildCost;
    public int DamageUpgradeLevel;
    public int RangeUpgradeLevel;
}

