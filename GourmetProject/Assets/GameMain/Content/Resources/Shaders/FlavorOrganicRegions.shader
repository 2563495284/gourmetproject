Shader "GourmetProject/FlavorOrganicRegions"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FlavorMask ("Flavor Mask", Float) = 0
        _FlavorCount ("Flavor Count", Range(0,7)) = 0
        _Seed ("Seed", Float) = 0
        _Intensity ("Intensity", Range(0,1)) = 0.72
        _Aspect ("Sprite Aspect", Float) = 1
        _AnimationEnabled ("Animation Enabled", Float) = 1
        _MotionTime ("Motion Time", Float) = 0
        _MotionSpeed ("Motion Speed", Range(0,2)) = 1.6
        _WarpStrength ("Warp Strength", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
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
                float _FlavorMask;
                float _FlavorCount;
                float _Seed;
                float _Intensity;
                float _Aspect;
                float _AnimationEnabled;
                float _MotionTime;
                float _MotionSpeed;
                float _WarpStrength;
            CBUFFER_END

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                    lerp(Hash(i + float2(0, 1)), Hash(i + 1), u.x),
                    u.y);
            }

            int IsActive(int flavor)
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
                    if (IsActive(flavor) != 0)
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

            float FlavorPattern(int flavor, float2 uv)
            {
                if (flavor == 0)
                {
                    float2 cell = frac(uv * float2(8, 7)) - 0.5;
                    return 1.0 - smoothstep(0.12, 0.25, length(cell));
                }
                if (flavor == 1)
                {
                    float2 cell = frac(uv * 6.0) - 0.5;
                    return 1.0 - smoothstep(0.045, 0.105, abs(length(cell) - 0.27));
                }
                if (flavor == 2)
                {
                    float hatch = abs(frac((uv.x + uv.y * 0.72) * 8.0) - 0.5);
                    float crack = abs(frac((uv.x * 0.31 - uv.y) * 5.0 + Noise(uv * 7.0)) - 0.5);
                    return saturate((1.0 - smoothstep(0.07, 0.17, hatch)) + (1.0 - smoothstep(0.04, 0.1, crack)));
                }
                if (flavor == 3)
                {
                    float2 cell = abs(frac(uv * 7.0) - 0.5);
                    return 1.0 - smoothstep(0.18, 0.29, cell.x + cell.y);
                }
                if (flavor == 4)
                {
                    float zig = abs(frac(uv.x * 6.0) - 0.5) * 2.0;
                    float wave = abs(frac(uv.y * 5.0 + zig * 0.55) - 0.5);
                    return 1.0 - smoothstep(0.07, 0.15, wave);
                }
                if (flavor == 5)
                {
                    float wave = abs(sin((uv.x * 8.0 + sin(uv.y * 12.0) * 0.32) * 3.14159));
                    return 1.0 - smoothstep(0.74, 0.94, wave);
                }

                float heat = abs(frac(uv.x * 7.0 + sin(uv.y * 13.0) * 0.22) - 0.5);
                return 1.0 - smoothstep(0.09, 0.2, heat);
            }

            float2 FlowField(float2 uv, float time)
            {
                float2 seedA = float2(_Seed * 0.071, -_Seed * 0.043);
                float2 seedB = float2(-_Seed * 0.037, _Seed * 0.083) + 19.17;
                float2 driftA = float2(time * 0.16, -time * 0.12);
                float2 driftB = float2(-time * 0.13, time * 0.18);
                float x = Noise(uv * 2.25 + seedA + driftA);
                float y = Noise(uv * 2.05 + seedB + driftB);
                return float2(x, y) - 0.5;
            }

            float InternalDisturbance(float2 uv, float2 flow, float time)
            {
                float2 domain = uv + flow * (0.28 * _WarpStrength);
                float coarse = Noise(domain * 3.4 + float2(time * 0.34, -time * 0.25) + _Seed * 0.071);
                float fine = Noise(domain * 7.2 + float2(-time * 0.46, time * 0.39) - _Seed * 0.043);
                float rolling = sin((domain.x * 1.7 + domain.y * 1.15) * 6.2831853 + time * 2.15 + _Seed);
                rolling = rolling * 0.5 + 0.5;
                float field = saturate(coarse * 0.52 + fine * 0.24 + rolling * 0.24);
                return smoothstep(0.24, 0.76, field);
            }

            float SectorPhase(float2 uv)
            {
                float2 p = uv - 0.5;
                p.x *= max(_Aspect, 0.15);
                float angle = atan2(p.y, p.x) / 6.2831853 + 0.5;
                // 分区只由 UV 与固定 Seed 决定；运行时间绝不参与，因此边界保持固定。
                float warp = (Noise(uv * 3.15 + _Seed * 0.071) - 0.5) * 0.28;
                warp += (Noise(uv * 6.7 - _Seed * 0.043) - 0.5) * 0.08;
                return frac(angle + warp + frac(_Seed * 0.017));
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
                if (_FlavorCount < 0.5 || tex.a <= 0.001) return tex;

                int count = max(1, (int)round(_FlavorCount));
                float phase = SectorPhase(input.uv);
                int slot = min(count - 1, (int)floor(phase * count));
                int flavor = FlavorForSlot(slot);
                half3 tint = FlavorColor(flavor);
                float time = _MotionTime * _MotionSpeed;
                float2 flow = FlowField(input.uv, time);
                float disturbance = InternalDisturbance(input.uv, flow, time);
                float2 patternDrift = float2(sin(time * 1.13), cos(time * 0.91)) * 0.065;
                float2 patternUv = input.uv + flow * (0.17 * _WarpStrength) + patternDrift + _Seed * 0.0017;
                float pattern = FlavorPattern(flavor, patternUv);
                float localPhase = frac(phase * count);
                float divider = 1.0 - smoothstep(0.015, 0.045, min(localPhase, 1.0 - localPhase));
                float liquidRidge = 1.0 - abs(disturbance * 2.0 - 1.0);
                liquidRidge = smoothstep(0.32, 0.82, liquidRidge);
                float movingFill = smoothstep(0.30, 0.68, disturbance);
                float blendAmount = _Intensity * (0.08 + pattern * 0.12 + movingFill * 0.32 + liquidRidge * 0.12);
                half3 rgb = lerp(tex.rgb, tint, (half)blendAmount);
                rgb = lerp(rgb, rgb * half3(0.45, 0.36, 0.25), (half)(divider * 0.32 * _Intensity));
                rgb *= (half)(0.84 + disturbance * 0.24);
                rgb += tint * (half)((pattern * 0.06 + liquidRidge * 0.10) * _Intensity);
                return half4(rgb, tex.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
