using Unity.Entities;
using Unity.Burst;

[BurstCompile]
public partial struct GameInitializationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<PlayerStats>())
        {
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                               .CreateCommandBuffer(state.WorldUnmanaged);
            
            int initialGold = 100;
            int initialHP = 20;

            // CSV 파일에서 초기 값 로드 (BurstCompile 내에서는 유니티 메인 스레드 객체 접근이 제한되므로 주의)
            // 하지만 ISystem의 OnUpdate는 메인 스레드에서 실행되므로 Unity API 사용 가능 (Burst 비활성화 시)
            // 여기서는 초기화 로직이므로 메인 스레드에서 텍스트 처리를 수행
            var csvAsset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("init_stat");
            if (csvAsset != null)
            {
                string text = csvAsset.text.Trim();
                string[] values = text.Split(',');
                if (values.Length >= 2)
                {
                    int.TryParse(values[0], out initialGold);
                    int.TryParse(values[1], out initialHP);
                }
            }

            var playerEntity = ecb.CreateEntity();
            ecb.AddComponent(playerEntity, new PlayerStats 
            { 
                Gold = initialGold, 
                HP = initialHP 
            });
        }
        
        // This system only needs to run once to initialize
        state.Enabled = false;
    }
}
