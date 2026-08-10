Shader "GourmetProject/FlavorAura"
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
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _FlavorMask;
                float _FlavorCount;
                float _Seed;
                float _Intensity;
                float _Aspect;
                float _AnimationEnabled;
            CBUFFER_END

            int IsActive(int flavor) { return (int)fmod(floor(_FlavorMask / exp2((float)flavor)), 2.0); }
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
                int flavor = FlavorForSlot(min(count - 1, (int)floor(phase * count)));
                half3 tint = FlavorColor(flavor);
                float animationTime = _Time.y * _AnimationEnabled;
                float breathe = 0.5 + 0.5 * sin(animationTime * 2.1 + phase * 12.566 + flavor * 0.83);
                float shimmer = smoothstep(0.70, 0.96, sin((input.uv.x * 9.0 - input.uv.y * 5.0 + animationTime * 0.55) * 3.14159) * 0.5 + 0.5);
                float blendAmount = _Intensity * (0.055 + breathe * 0.045 + shimmer * 0.055);
                half3 rgb = lerp(tex.rgb, tint, (half)blendAmount);
                rgb += tint * (half)(shimmer * 0.035 * _Intensity);
                return half4(rgb, tex.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
