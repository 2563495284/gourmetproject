Shader "GourmetProject/FlavorSegmentedRim"
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
        _RimWidth ("Rim Width", Range(1,32)) = 18
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" "RenderPipeline"="UniversalPipeline" }
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

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            half4 _TextureSampleAdd;
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _FlavorMask;
                float _FlavorCount;
                float _Seed;
                float _Intensity;
                float _Aspect;
                float _AnimationEnabled;
                float _RimWidth;
            CBUFFER_END

            int IsActive(int flavor)
            {
                return (int)fmod(floor(_FlavorMask / exp2((float)flavor)), 2.0);
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
                float stripe = frac((uv.x * (5.0 + flavor) + uv.y * (3.0 + fmod(flavor, 3.0))) + flavor * 0.17);
                if (flavor == 1) return 1.0 - smoothstep(0.12, 0.28, abs(stripe - 0.5));
                if (flavor == 3) return step(0.72, stripe);
                if (flavor == 4) return step(stripe, 0.32);
                return smoothstep(0.38, 0.68, stripe);
            }

            float InnerRim(float2 uv, float centerAlpha)
            {
                float2 d = _MainTex_TexelSize.xy * _RimWidth;
                float minAlpha = 1.0;
                minAlpha = min(minAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(d.x, 0)).a);
                minAlpha = min(minAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(d.x, 0)).a);
                minAlpha = min(minAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, d.y)).a);
                minAlpha = min(minAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(0, d.y)).a);
                minAlpha = min(minAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d).a);
                minAlpha = min(minAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - d).a);
                minAlpha = min(minAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(d.x, -d.y)).a);
                minAlpha = min(minAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-d.x, d.y)).a);
                return saturate((centerAlpha - minAlpha) * 3.2);
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

                float2 p = input.uv - 0.5;
                p.x *= max(_Aspect, 0.15);
                float phase = frac(atan2(p.y, p.x) / 6.2831853 + 0.5 + frac(_Seed * 0.017));
                int count = max(1, (int)round(_FlavorCount));
                int slot = min(count - 1, (int)floor(phase * count));
                int flavor = FlavorForSlot(slot);
                half3 tint = FlavorColor(flavor);
                float pattern = FlavorPattern(flavor, input.uv);
                float rim = InnerRim(input.uv, tex.a) * _Intensity;
                rim *= 0.72 + pattern * 0.28;
                float localPhase = frac(phase * count);
                float divider = 1.0 - smoothstep(0.012, 0.04, min(localPhase, 1.0 - localPhase));
                half3 rgb = lerp(tex.rgb, tint, (half)(rim * 0.86));
                rgb = lerp(rgb, half3(0.18, 0.09, 0.035), (half)(rim * divider * 0.82));
                rgb += tint * (half)(rim * pattern * 0.11);
                return half4(rgb, tex.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
