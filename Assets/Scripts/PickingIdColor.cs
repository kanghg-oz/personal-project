using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

[MaterialProperty("_col")]
public struct PickingIdColor : IComponentData
{
    public float4 Value;
}