using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public partial struct TowerAnimationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // 현재 시간과 델타 타임 추출
        float currentTime = (float)SystemAPI.Time.ElapsedTime;
        //float currentTime = (float)UnityEngine.Time.time;
        //float shaderTime = (float)SystemAPI.Time.ElapsedTime;
        float dt = SystemAPI.Time.DeltaTime;

        // 단일 구조체 쿼리
        foreach (var anim in SystemAPI.Query<RefRW<TowerAnimation>>())
        {
            // 1. 타이머 계산
            float nextTimer = anim.ValueRO.Timer + dt;

            if (nextTimer >= 3.0f)
            {
                // 2. 셰이더 데이터 갱신 (Vector 4의 x성분만 교체)
                // .x 속성을 직접 사용하여 의미를 명확히 함
                anim.ValueRW.LastFireTime = currentTime;
                anim.ValueRW.Timer = 0f;
            }
            else
            {
                anim.ValueRW.Timer = nextTimer;
            }
        }
    }
}