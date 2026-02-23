Shader "Custom/DebugIdShader"
{
    Properties
    {
        _IdColor("ID Color", Color) = (1,0,0,1)
        _IdColorDefault("ID Color Default", Color) = (1,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "DebugIdPass"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Non-instancing material default (separate name to avoid collision with instanced `_IdColor`).
            CBUFFER_START(UnityPerMaterial)
                float4 _IdColorDefault;
            CBUFFER_END

            // Traditional GPU instancing path (non-DOTS)
            UNITY_INSTANCING_BUFFER_START(PerInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _IdColor)
            UNITY_INSTANCING_BUFFER_END(PerInstance)

            // DOTS instancing declaration (generates metadata + override mode symbols)
            #if defined(DOTS_INSTANCING_ON)
                UNITY_DOTS_INSTANCING_START(UnityPerMaterial)
                    UNITY_DOTS_INSTANCED_PROP(float4, _IdColor)
                UNITY_DOTS_INSTANCING_END(UnityPerMaterial)
            #endif

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                #if defined(DOTS_INSTANCING_ON)
                    float4 idColor = UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _IdColor);
                #elif defined(UNITY_INSTANCING_ENABLED)
                    float4 idColor = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _IdColor);
                #else
                    float4 idColor = _IdColorDefault;
                #endif

                return (half4)idColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}