Shader "GourmetProject/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _OutlineColor ("Outline Color", Color) = (0.35, 0.9, 0.4, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.055
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _FillAlpha;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                float width = saturate(_OutlineWidth);
                float2 uv = input.uv;
                float left = 1.0 - step(width, uv.x);
                float right = step(1.0 - width, uv.x);
                float bottom = 1.0 - step(width, uv.y);
                float top = step(1.0 - width, uv.y);
                half outline = (half)saturate(max(max(left, right), max(bottom, top)));

                half4 fill = tex;
                fill.a *= (half)_FillAlpha;
                half alpha = max(fill.a, outline * _OutlineColor.a);
                half3 rgb = lerp(fill.rgb, _OutlineColor.rgb, outline);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
