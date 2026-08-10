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

            half3 FlavorShadow(int flavor)
            {
                if (flavor == 0) return half3(0.48, 0.08, 0.25); // 甜：莓果粉阴影
                if (flavor == 1) return half3(0.18, 0.43, 0.04); // 酸：青柠绿阴影
                if (flavor == 2) return half3(0.14, 0.09, 0.025); // 苦：深咖啡阴影
                if (flavor == 3) return half3(0.12, 0.39, 0.62); // 咸：冰蓝阴影
                if (flavor == 4) return half3(0.25, 0.08, 0.54); // 麻：靛紫阴影
                if (flavor == 5) return half3(0.55, 0.18, 0.015); // 鲜：焦糖橙阴影
                return half3(0.61, 0.025, 0.005);                // 辣：深朱红阴影
            }

            half3 FlavorHighlight(int flavor)
            {
                if (flavor == 0) return half3(1.00, 0.78, 0.88); // 甜：奶油桃粉
                if (flavor == 1) return half3(0.88, 1.00, 0.27); // 酸：荧光黄绿
                if (flavor == 2) return half3(0.66, 0.51, 0.15); // 苦：干芥末高光
                if (flavor == 3) return half3(0.86, 0.98, 1.00); // 咸：冰晶白
                if (flavor == 4) return half3(0.86, 0.61, 1.00); // 麻：薰衣草紫
                if (flavor == 5) return half3(1.00, 0.80, 0.25); // 鲜：琥珀金
                return half3(1.00, 0.56, 0.10);                  // 辣：火焰橙
            }

            half3 ApplyFlavorGrade(half3 source, int flavor)
            {
                half luminance = dot(source, half3(0.299, 0.587, 0.114));
                half curve = smoothstep(0.08, 0.92, luminance);
                half3 duotone = lerp(FlavorShadow(flavor), FlavorHighlight(flavor), curve);

                // 保留少量原图色差，避免双色分级把细节压成平面。
                half3 originalChroma = source - luminance.xxx;
                return saturate(duotone + originalChroma * 0.16);
            }

            float FlavorPattern(int flavor, float2 uv, float time)
            {
                if (flavor == 0)
                {
                    // 甜：有黏性的糖浆带沿斜方向缓慢流动。
                    float syrupWarp = sin(uv.y * 10.0 + time * 0.72 + _Seed) * 0.11;
                    float ribbon = abs(sin((uv.x * 3.0 + uv.y * 1.45 + syrupWarp - time * 0.43) * 3.14159));
                    return 1.0 - smoothstep(0.16, 0.40, ribbon);
                }
                if (flavor == 1)
                {
                    // 酸：气泡向上漂移，并按各自相位轻微胀缩。
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
                    // 苦：干涩斜线缓慢错动，裂纹在原位收紧、放松。
                    float hatch = abs(frac((uv.x + uv.y * 0.72) * 8.5 + time * 0.10) - 0.5);
                    float patchPhase = Hash(floor(uv * 4.0) + _Seed) * 6.2831853;
                    float dryPulse = sin(time * 0.86 + patchPhase) * 0.5 + 0.5;
                    float crackWidth = lerp(0.035, 0.105, dryPulse);
                    float crack = abs(frac((uv.x * 0.31 - uv.y) * 5.5 + Noise(uv * 7.0)) - 0.5);
                    float hatchInk = 1.0 - smoothstep(0.065, 0.16, hatch);
                    float crackInk = 1.0 - smoothstep(crackWidth, crackWidth + 0.055, crack);
                    return saturate(hatchInk + crackInk);
                }
                if (flavor == 3)
                {
                    // 咸：晶体不平移，只按格子随机相位闪烁。
                    float2 crystalUv = uv * 7.0;
                    float2 crystalId = floor(crystalUv);
                    float2 crystalCell = abs(frac(crystalUv) - 0.5);
                    float crystal = 1.0 - smoothstep(0.17, 0.30, crystalCell.x + crystalCell.y);
                    float twinkle = sin(time * 3.7 + Hash(crystalId + _Seed) * 6.2831853) * 0.5 + 0.5;
                    return crystal * lerp(0.32, 1.0, twinkle);
                }
                if (flavor == 4)
                {
                    // 麻：连续的锯齿电流横向传播，不做随机闪断或亮灭。
                    float zig = abs(frac(uv.x * 6.0 - time * 1.32) - 0.5) * 2.0;
                    float wave = abs(frac(uv.y * 5.2 + zig * 0.62) - 0.5);
                    float bolt = 1.0 - smoothstep(0.055, 0.145, wave);
                    return bolt;
                }
                if (flavor == 5)
                {
                    // 鲜：油滴涟漪从多个固定中心向外扩散。
                    float2 rippleUv = uv * 3.5;
                    float2 rippleId = floor(rippleUv);
                    float2 rippleCell = frac(rippleUv) - 0.5;
                    float rippleHash = Hash(rippleId + _Seed);
                    rippleCell += float2(Hash(rippleId + 7.31) - 0.5, Hash(rippleId + 19.17) - 0.5) * 0.12;
                    float rippleRadius = frac(time * 0.36 + rippleHash) * 0.64;
                    float ripple = 1.0 - smoothstep(0.035, 0.105, abs(length(rippleCell) - rippleRadius));
                    return ripple * (1.0 - rippleRadius * 0.72);
                }

                // 辣：重复的火舌向上窜动，顶部收尖并左右摆动。
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
                float2 patternUv = input.uv + flow * (0.055 * _WarpStrength) + _Seed * 0.0017;
                float pattern = FlavorPattern(flavor, patternUv, time);
                float localPhase = frac(phase * count);
                float divider = 1.0 - smoothstep(0.015, 0.045, min(localPhase, 1.0 - localPhase));
                float liquidRidge = 1.0 - abs(disturbance * 2.0 - 1.0);
                liquidRidge = smoothstep(0.32, 0.82, liquidRidge);
                float movingFill = smoothstep(0.30, 0.68, disturbance);
                float patternOuter = smoothstep(0.18, 0.48, pattern);
                float patternCore = smoothstep(0.56, 0.82, pattern);
                float patternEdge = saturate(patternOuter - patternCore);
                float blendAmount = _Intensity * (0.08 + patternOuter * 0.10 + movingFill * 0.30 + liquidRidge * 0.10);

                half3 graded = ApplyFlavorGrade(tex.rgb, flavor);
                half3 rgb = lerp(tex.rgb, graded, (half)(0.52 * _Intensity));
                rgb = lerp(rgb, tint, (half)blendAmount);
                rgb = lerp(rgb, rgb * half3(0.45, 0.36, 0.25), (half)(divider * 0.32 * _Intensity));
                rgb *= (half)(0.84 + disturbance * 0.24);

                // 风味纹理使用“暗边 + 亮芯”，在高饱和和浅色 Sprite 上都能保持清晰。
                half3 patternDark = FlavorShadow(flavor) * 0.58;
                half3 patternLight = lerp(tint, FlavorHighlight(flavor), 0.72);
                rgb = lerp(rgb, patternDark, (half)(patternEdge * 0.28 * _Intensity));
                rgb = lerp(rgb, patternLight, (half)(patternCore * 0.38 * _Intensity));
                rgb += tint * (half)(liquidRidge * 0.075 * _Intensity);
                return half4(rgb, tex.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
