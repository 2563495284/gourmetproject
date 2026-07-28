Shader "GourmetProject/FlavorStain"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _StainCount ("Stain Count", Range(0, 4)) = 0
        _StainColor0 ("Stain Color 0", Color) = (1, 0.23, 0.19, 0.85)
        _StainColor1 ("Stain Color 1", Color) = (0.35, 0.85, 0.35, 0.85)
        _StainColor2 ("Stain Color 2", Color) = (1, 0.8, 0.2, 0.85)
        _StainColor3 ("Stain Color 3", Color) = (0.6, 0.4, 0.8, 0.85)
        _StainScale ("Stain Scale", Range(1, 24)) = 8
        _StainThreshold ("Stain Threshold", Range(0, 1)) = 0.62
        _StainSoftness ("Stain Softness", Range(0.001, 0.5)) = 0.12
        _StainDarken ("Stain Darken", Range(0, 1)) = 0.12
        _Seed ("Seed", Float) = 0
        _Brightness ("Brightness", Range(0, 1)) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

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
                float4 worldPosition : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            half4 _TextureSampleAdd;
            float4 _ClipRect;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _StainCount;
                half4 _StainColor0;
                half4 _StainColor1;
                half4 _StainColor2;
                half4 _StainColor3;
                float _StainScale;
                float _StainThreshold;
                float _StainSoftness;
                float _StainDarken;
                float _Seed;
                float _Brightness;
            CBUFFER_END

            // 程序化噪声：hash -> value noise -> fbm，无需外部贴图。
            float hash1(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 hash2(float n)
            {
                return frac(sin(float2(n, n + 1.7)) * float2(43758.5453, 22578.145));
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash1(i + float2(0.0, 0.0));
                float b = hash1(i + float2(1.0, 0.0));
                float c = hash1(i + float2(0.0, 1.0));
                float d = hash1(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amp = 0.5;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    value += amp * ValueNoise(p);
                    p *= 2.02;
                    amp *= 0.5;
                }
                return value;
            }

            half UnityGet2DClippingHlsl(float2 position, float4 clipRect)
            {
                float2 inside = step(clipRect.xy, position) * step(position, clipRect.zw);
                return (half)(inside.x * inside.y);
            }

            half4 StainColorByIndex(int i)
            {
                if (i == 0) return _StainColor0;
                if (i == 1) return _StainColor1;
                if (i == 2) return _StainColor2;
                return _StainColor3;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Color;
                output.uv = input.uv;
                output.worldPosition = input.positionOS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) + _TextureSampleAdd) * input.color;
                half3 rgb = tex.rgb;

                int count = (int)round(_StainCount);
                [loop]
                for (int i = 0; i < count; i++)
                {
                    float2 off = hash2(float(i) + _Seed) * 11.0;
                    float n = Fbm(input.uv * _StainScale + off);
                    half mask = (half)smoothstep(
                        _StainThreshold - _StainSoftness,
                        _StainThreshold + _StainSoftness,
                        n);
                    half4 c = StainColorByIndex(i);
                    half k = mask * c.a;
                    // 污渍：先把底色轻微压暗，再叠风味色，避免发亮失去"脏"感。
                    half3 darkened = rgb * (1.0 - _StainDarken * k);
                    rgb = lerp(darkened, c.rgb, k);
                }

                // 主动调味在闪白峰值切入本 Shader，并由这里完成白光退场。
                rgb += (half)_Brightness;

                // alpha 保持原图轮廓，脏印天然被约束在食物内部，不溢出。
                half4 color = half4(rgb, tex.a);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClippingHlsl(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDHLSL
        }
    }

    FallBack "UI/Default"
}
