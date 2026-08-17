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

        // 与 FlavorOrganicRegions 共用属性，切换材质时保留动态有机味区。
        _FlavorMask ("Flavor Mask", Float) = 0
        _FlavorCount ("Flavor Count", Range(0, 7)) = 0
        _Seed ("Flavor Seed", Float) = 0
        _Intensity ("Flavor Intensity", Range(0, 1)) = 0.72
        _Aspect ("Sprite Aspect", Float) = 1
        _AnimationEnabled ("Animation Enabled", Float) = 1
        _MotionTime ("Motion Time", Float) = 0
        _UseGlobalTime ("Use Global Time", Range(0, 1)) = 1
        _MotionSpeed ("Motion Speed", Range(0, 2)) = 1.6
        _WarpStrength ("Warp Strength", Range(0, 1)) = 1

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
            #pragma target 3.0
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
                float4 _SpriteUvRect;
                float _DigestProgress;
                float2 _DigestCenter;
                float2 _DigestGridSize;
                float _DigestSeed;
                float _FlavorMask;
                float _FlavorCount;
                float _Seed;
                float _Intensity;
                float _Aspect;
                float _AnimationEnabled;
                float _MotionTime;
                float _UseGlobalTime;
                float _MotionSpeed;
                float _WarpStrength;
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

            int FlavorIsActive(int flavor)
            {
                float divisor = exp2((float)flavor);
                return (int)fmod(floor(_FlavorMask / divisor), 2.0);
            }

            int FlavorForSlot(int slot)
            {
                int seen = 0;
                [unroll]
                for (int flavor = 0; flavor < 7; flavor++)
                {
                    if (FlavorIsActive(flavor) != 0)
                    {
                        if (seen == slot) return flavor;
                        seen++;
                    }
                }
                return 0;
            }

            half3 FlavorColor(int flavor)
            {
                if (flavor == 0) return half3(1.00, 0.38, 0.67);
                if (flavor == 1) return half3(0.62, 0.91, 0.20);
                if (flavor == 2) return half3(0.34, 0.29, 0.12);
                if (flavor == 3) return half3(0.80, 0.95, 1.00);
                if (flavor == 4) return half3(0.62, 0.34, 0.94);
                if (flavor == 5) return half3(1.00, 0.58, 0.12);
                return half3(1.00, 0.20, 0.09);
            }

            half3 FlavorShadow(int flavor)
            {
                if (flavor == 0) return half3(0.48, 0.08, 0.25);
                if (flavor == 1) return half3(0.18, 0.43, 0.04);
                if (flavor == 2) return half3(0.14, 0.09, 0.025);
                if (flavor == 3) return half3(0.12, 0.39, 0.62);
                if (flavor == 4) return half3(0.25, 0.08, 0.54);
                if (flavor == 5) return half3(0.55, 0.18, 0.015);
                return half3(0.61, 0.025, 0.005);
            }

            half3 FlavorHighlight(int flavor)
            {
                if (flavor == 0) return half3(1.00, 0.78, 0.88);
                if (flavor == 1) return half3(0.88, 1.00, 0.27);
                if (flavor == 2) return half3(0.66, 0.51, 0.15);
                if (flavor == 3) return half3(0.86, 0.98, 1.00);
                if (flavor == 4) return half3(0.86, 0.61, 1.00);
                if (flavor == 5) return half3(1.00, 0.80, 0.25);
                return half3(1.00, 0.56, 0.10);
            }

            half3 ApplyFlavorGrade(half3 source, int flavor)
            {
                half luminance = dot(source, half3(0.299, 0.587, 0.114));
                half curve = smoothstep(0.08, 0.92, luminance);
                half3 duotone = lerp(FlavorShadow(flavor), FlavorHighlight(flavor), curve);
                half3 originalChroma = source - luminance.xxx;
                return saturate(duotone + originalChroma * 0.16);
            }

            float FlavorPattern(int flavor, float2 uv, float time)
            {
                if (flavor == 0)
                {
                    float syrupWarp = sin(uv.y * 10.0 + time * 0.72 + _Seed) * 0.11;
                    float ribbon = abs(sin((uv.x * 3.0 + uv.y * 1.45 + syrupWarp - time * 0.43) * 3.14159));
                    return 1.0 - smoothstep(0.16, 0.40, ribbon);
                }
                if (flavor == 1)
                {
                    float2 bubbleUv = uv * 5.8 + float2(0.0, -time * 0.78);
                    float2 bubbleId = floor(bubbleUv);
                    float2 bubbleCell = frac(bubbleUv) - 0.5;
                    float bubbleHash = Hash(bubbleId + _Seed);
                    bubbleCell.x += (bubbleHash - 0.5) * 0.30;
                    float bubbleRadius = 0.22 + sin(time * 2.4 + bubbleHash * 6.2831853) * 0.045;
                    return 1.0 - smoothstep(0.035, 0.105, abs(length(bubbleCell) - bubbleRadius));
                }
                if (flavor == 2)
                {
                    float hatch = abs(frac((uv.x + uv.y * 0.72) * 8.5 + time * 0.10) - 0.5);
                    float patchPhase = Hash(floor(uv * 4.0) + _Seed) * 6.2831853;
                    float dryPulse = sin(time * 0.86 + patchPhase) * 0.5 + 0.5;
                    float crackWidth = lerp(0.035, 0.105, dryPulse);
                    float crack = abs(frac((uv.x * 0.31 - uv.y) * 5.5 + ValueNoise(uv * 7.0)) - 0.5);
                    float hatchInk = 1.0 - smoothstep(0.065, 0.16, hatch);
                    float crackInk = 1.0 - smoothstep(crackWidth, crackWidth + 0.055, crack);
                    return saturate(hatchInk + crackInk);
                }
                if (flavor == 3)
                {
                    float2 crystalUv = uv * 7.0;
                    float2 crystalId = floor(crystalUv);
                    float2 crystalCell = abs(frac(crystalUv) - 0.5);
                    float crystal = 1.0 - smoothstep(0.17, 0.30, crystalCell.x + crystalCell.y);
                    float twinkle = sin(time * 3.7 + Hash(crystalId + _Seed) * 6.2831853) * 0.5 + 0.5;
                    return crystal * lerp(0.32, 1.0, twinkle);
                }
                if (flavor == 4)
                {
                    float zig = abs(frac(uv.x * 6.0 - time * 1.32) - 0.5) * 2.0;
                    float wave = abs(frac(uv.y * 5.2 + zig * 0.62) - 0.5);
                    return 1.0 - smoothstep(0.055, 0.145, wave);
                }
                if (flavor == 5)
                {
                    float2 rippleUv = uv * 3.5;
                    float2 rippleId = floor(rippleUv);
                    float2 rippleCell = frac(rippleUv) - 0.5;
                    float rippleHash = Hash(rippleId + _Seed);
                    rippleCell += float2(Hash(rippleId + 7.31) - 0.5, Hash(rippleId + 19.17) - 0.5) * 0.12;
                    float rippleRadius = frac(time * 0.36 + rippleHash) * 0.64;
                    float ripple = 1.0 - smoothstep(0.035, 0.105, abs(length(rippleCell) - rippleRadius));
                    return ripple * (1.0 - rippleRadius * 0.72);
                }

                float flameColumns = uv.x * 5.2;
                float flameId = floor(flameColumns);
                float flameY = frac(uv.y * 3.1 - time * 0.92 + Hash(flameId + _Seed) * 0.73);
                float flameSway = sin(uv.y * 13.0 + time * 2.35 + flameId) * 0.14;
                float flameX = abs(frac(flameColumns + flameSway) - 0.5);
                float flameWidth = lerp(0.27, 0.045, flameY);
                float flameBody = 1.0 - smoothstep(flameWidth, flameWidth + 0.07, flameX);
                float flameFade = smoothstep(0.02, 0.14, flameY) * (1.0 - smoothstep(0.76, 1.0, flameY));
                return flameBody * flameFade;
            }

            float2 FlavorFlowField(float2 uv, float time)
            {
                float2 seedA = float2(_Seed * 0.071, -_Seed * 0.043);
                float2 seedB = float2(-_Seed * 0.037, _Seed * 0.083) + 19.17;
                float x = ValueNoise(uv * 2.25 + seedA + float2(time * 0.16, -time * 0.12));
                float y = ValueNoise(uv * 2.05 + seedB + float2(-time * 0.13, time * 0.18));
                return float2(x, y) - 0.5;
            }

            float FlavorDisturbance(float2 uv, float2 flow, float time)
            {
                float2 domain = uv + flow * (0.28 * _WarpStrength);
                float coarse = ValueNoise(domain * 3.4 + float2(time * 0.34, -time * 0.25) + _Seed * 0.071);
                float fine = ValueNoise(domain * 7.2 + float2(-time * 0.46, time * 0.39) - _Seed * 0.043);
                float rolling = sin((domain.x * 1.7 + domain.y * 1.15) * 6.2831853 + time * 2.15 + _Seed) * 0.5 + 0.5;
                return smoothstep(0.24, 0.76, saturate(coarse * 0.52 + fine * 0.24 + rolling * 0.24));
            }

            float FlavorSectorPhase(float2 uv)
            {
                float2 p = uv - 0.5;
                p.x *= max(_Aspect, 0.15);
                float angle = atan2(p.y, p.x) / 6.2831853 + 0.5;
                float warp = (ValueNoise(uv * 3.15 + _Seed * 0.071) - 0.5) * 0.28;
                warp += (ValueNoise(uv * 6.7 - _Seed * 0.043) - 0.5) * 0.08;
                return frac(angle + warp + frac(_Seed * 0.017));
            }

            half3 ApplyOrganicFlavor(half3 source, float2 uv)
            {
                if (_FlavorCount < 0.5) return source;

                int count = max(1, (int)round(_FlavorCount));
                float phase = FlavorSectorPhase(uv);
                int slot = min(count - 1, (int)floor(phase * count));
                int flavor = FlavorForSlot(slot);
                half3 tint = FlavorColor(flavor);
                float timeSource = lerp(_MotionTime, _Time.y, saturate(_UseGlobalTime));
                float time = timeSource * _MotionSpeed;
                float2 flow = FlavorFlowField(uv, time);
                float disturbance = FlavorDisturbance(uv, flow, time);
                float pattern = FlavorPattern(flavor, uv + flow * (0.055 * _WarpStrength) + _Seed * 0.0017, time);
                float localPhase = frac(phase * count);
                float divider = 1.0 - smoothstep(0.015, 0.045, min(localPhase, 1.0 - localPhase));
                float liquidRidge = smoothstep(0.32, 0.82, 1.0 - abs(disturbance * 2.0 - 1.0));
                float movingFill = smoothstep(0.30, 0.68, disturbance);
                float patternOuter = smoothstep(0.18, 0.48, pattern);
                float patternCore = smoothstep(0.56, 0.82, pattern);
                float patternEdge = saturate(patternOuter - patternCore);
                float blendAmount = _Intensity * (0.08 + patternOuter * 0.10 + movingFill * 0.30 + liquidRidge * 0.10);

                half3 rgb = lerp(source, ApplyFlavorGrade(source, flavor), (half)(0.52 * _Intensity));
                rgb = lerp(rgb, tint, (half)blendAmount);
                rgb = lerp(rgb, rgb * half3(0.45, 0.36, 0.25), (half)(divider * 0.32 * _Intensity));
                rgb *= (half)(0.84 + disturbance * 0.24);
                rgb = lerp(rgb, FlavorShadow(flavor) * 0.58, (half)(patternEdge * 0.28 * _Intensity));
                rgb = lerp(rgb, lerp(tint, FlavorHighlight(flavor), 0.72), (half)(patternCore * 0.38 * _Intensity));
                rgb += tint * (half)(liquidRidge * 0.075 * _Intensity);
                return rgb;
            }

            half UnityGet2DClippingHlsl(float2 position, float4 clipRect)
            {
                float2 inside = step(clipRect.xy, position) * step(position, clipRect.zw);
                return (half)(inside.x * inside.y);
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

                half3 rgb = ApplyOrganicFlavor(tex.rgb, rectUv);

                // 溶解前沿由黄绿胃液色过渡到焦橙色，表现“被消化”而非燃烧成灰。
                float frontDistance = abs(field - inwardFront);
                float innerEdge = 1.0 - smoothstep(0.012, edgeWidth * 1.15, frontDistance);
                half3 acid = lerp(half3(0.98, 0.72, 0.10), half3(0.56, 0.82, 0.16), (half)coarse);
                rgb = lerp(rgb, acid, (half)(innerEdge * 0.82));
                rgb *= (half)(1.0 - _DigestProgress * 0.18);

                half4 color = half4(rgb, tex.a * (half)visibleMask);

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

    FallBack "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
