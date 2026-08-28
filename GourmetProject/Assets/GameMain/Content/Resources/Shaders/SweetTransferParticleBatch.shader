Shader "GourmetProject/SweetTransferParticleBatch"
{
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SweetTransferParticleBatch"

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 与 BattleShadow.CreateRadialShadowSprite(0.4f) 相同：
                // 中心 40% 保持实心，随后用 SmoothStep 羽化到边缘。
                float distanceFromCenter = length((input.uv - 0.5) * 2.0);
                half feather = 1.0h - smoothstep(0.4h, 1.0h, distanceFromCenter);
                half alpha = input.color.a * feather;
                clip(alpha - 0.001h);
                return half4(input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
