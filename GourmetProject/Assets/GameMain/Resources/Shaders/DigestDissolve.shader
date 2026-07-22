Shader "GourmetProject/DigestDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [PerRendererData] _SpriteUvRect ("Sprite UV Rect", Vector) = (0, 0, 1, 1)
        [PerRendererData] _DigestProgress ("Digest Progress", Range(0, 1)) = 0
        [PerRendererData] _DigestCenter ("Digest Center", Vector) = (0.5, 0.5, 0, 0)
        [PerRendererData] _DigestGridSize ("Digest Grid Size", Vector) = (1, 1, 0, 0)
        [PerRendererData] _DigestSeed ("Digest Seed", Float) = 0

        // 与 FlavorStain 共用属性，切换材质时保留原食物风味脏印。
        _StainCount ("Stain Count", Range(0, 4)) = 0
        _StainColor0 ("Stain Color 0", Color) = (1, 0.23, 0.19, 0.85)
        _StainColor1 ("Stain Color 1", Color) = (0.35, 0.85, 0.35, 0.85)
        _StainColor2 ("Stain Color 2", Color) = (1, 0.8, 0.2, 0.85)
        _StainColor3 ("Stain Color 3", Color) = (0.6, 0.4, 0.8, 0.85)
        _StainScale ("Stain Scale", Range(1, 24)) = 8
        _StainThreshold ("Stain Threshold", Range(0, 1)) = 0.62
        _StainSoftness ("Stain Softness", Range(0.001, 0.5)) = 0.12
        _StainDarken ("Stain Darken", Range(0, 1)) = 0.12
        _Seed ("Stain Seed", Float) = 0
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
            half4 _TextureSampleAdd;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _SpriteUvRect;
                float _DigestProgress;
                float2 _DigestCenter;
                float2 _DigestGridSize;
                float _DigestSeed;
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
            CBUFFER_END

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash(cell);
                float b = Hash(cell + float2(1.0, 0.0));
                float c = Hash(cell + float2(0.0, 1.0));
                float d = Hash(cell + 1.0);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                [unroll]
                for (int octave = 0; octave < 4; octave++)
                {
                    value += amplitude * ValueNoise(p);
                    p = p * 2.03 + 7.17;
                    amplitude *= 0.5;
                }
                return value;
            }

            half4 StainColor(int index)
            {
                if (index == 0) return _StainColor0;
                if (index == 1) return _StainColor1;
                if (index == 2) return _StainColor2;
                return _StainColor3;
            }

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
                half4 tex = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) + _TextureSampleAdd) * input.color;
                clip(tex.a - 0.001);

                float2 uvSpan = max(_SpriteUvRect.zw - _SpriteUvRect.xy, float2(0.0001, 0.0001));
                float2 rectUv = saturate((input.uv - _SpriteUvRect.xy) / uvSpan);

                // 用占格尺寸校正距离，长条/L/T 食物不会按贴图矩形中心做椭圆式收缩。
                float2 p = (rectUv - _DigestCenter) * max(_DigestGridSize, float2(1.0, 1.0));
                float2 farCorner = max(_DigestCenter, 1.0 - _DigestCenter) * max(_DigestGridSize, float2(1.0, 1.0));
                float maxRadius = max(length(farCorner), 0.0001);
                float radius01 = length(p) / maxRadius;

                // 借鉴原 dissolve.fs 的多频场思路；低频扭曲形成胃液啃蚀，高频颗粒打散边界。
                float2 noiseUv = rectUv * max(_DigestGridSize, float2(1.0, 1.0));
                float coarse = Fbm(noiseUv * 2.6 + _DigestSeed * 0.73);
                float granule = ValueNoise(noiseUv * 10.0 + _DigestSeed * 2.31);
                float noise = (coarse - 0.5) * 0.20 + (granule - 0.5) * 0.055;
                float inwardFront = 1.055 - _DigestProgress * 1.11;
                float field = radius01 + noise;
                float edgeWidth = 0.075;
                float visibleMask = 1.0 - smoothstep(inwardFront - edgeWidth, inwardFront + edgeWidth, field);
                clip(visibleMask - 0.02);

                half3 rgb = tex.rgb;
                int stainCount = (int)round(_StainCount);
                [loop]
                for (int stain = 0; stain < stainCount; stain++)
                {
                    float2 offset = frac(sin(float2(stain + _Seed, stain + _Seed + 1.7)) * float2(43758.5453, 22578.145)) * 11.0;
                    float stainNoise = Fbm(input.uv * _StainScale + offset);
                    half stainMask = (half)smoothstep(_StainThreshold - _StainSoftness, _StainThreshold + _StainSoftness, stainNoise);
                    half4 stainColor = StainColor(stain);
                    half k = stainMask * stainColor.a;
                    rgb = lerp(rgb * (1.0 - _StainDarken * k), stainColor.rgb, k);
                }

                // 溶解前沿由黄绿胃液色过渡到焦橙色，表现“被消化”而非燃烧成灰。
                float frontDistance = abs(field - inwardFront);
                float innerEdge = 1.0 - smoothstep(0.012, edgeWidth * 1.15, frontDistance);
                half3 acid = lerp(half3(0.98, 0.72, 0.10), half3(0.56, 0.82, 0.16), (half)coarse);
                rgb = lerp(rgb, acid, (half)(innerEdge * 0.82));
                rgb *= (half)(1.0 - _DigestProgress * 0.18);

                return half4(rgb, tex.a * (half)visibleMask);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
