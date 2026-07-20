Shader "GourmetProject/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _OutlineColor ("Outline Color", Color) = (0.35, 0.9, 0.4, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.055
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0
        _OuterAlpha ("Outer Alpha", Range(0, 1)) = 1
        _InnerAlpha ("Inner Edge Alpha", Range(0, 1)) = 0.58
        _GlowIntensity ("Glow Intensity", Range(0.25, 3)) = 1.15
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 0
        _PulseAmplitude ("Pulse Amplitude", Range(0, 0.5)) = 0
        _PulseFrequency ("Pulse Frequency", Range(0, 64)) = 18
        _AlphaThreshold ("Alpha Threshold", Range(0.001, 0.5)) = 0.08
        [PerRendererData] _UseRectMask ("Use Rectangle Mask", Float) = 0
        [PerRendererData] _RectSize ("Rectangle Size", Vector) = (1, 1, 0, 0)
        [PerRendererData] _UvInflate ("UV Inflate Compensation", Float) = 1
        [PerRendererData] _SpriteUvRect ("Sprite UV Rect", Vector) = (0, 0, 1, 1)
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
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _FillAlpha;
                float _OuterAlpha;
                float _InnerAlpha;
                float _GlowIntensity;
                float _PulseSpeed;
                float _PulseAmplitude;
                float _PulseFrequency;
                float _AlphaThreshold;
                float _UseRectMask;
                float4 _RectSize;
                float _UvInflate;
                float4 _SpriteUvRect;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Color;
                output.uv = input.uv;
                return output;
            }

            half InsideSpriteRect(float2 uv)
            {
                float2 aboveMin = step(_SpriteUvRect.xy, uv);
                float2 belowMax = step(uv, _SpriteUvRect.zw);
                return (half)(aboveMin.x * aboveMin.y * belowMax.x * belowMax.y);
            }

            float2 CompensatedUv(float2 uv)
            {
                float2 center = (_SpriteUvRect.xy + _SpriteUvRect.zw) * 0.5;
                return center + (uv - center) * max(1.0, _UvInflate);
            }

            half AlphaAt(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a * InsideSpriteRect(uv);
            }

            half AlphaMask(half alpha)
            {
                float threshold = max(_AlphaThreshold, 0.001);
                return (half)smoothstep(threshold * 0.35, threshold, alpha);
            }

            half RingAlpha(float2 uv, float2 offset)
            {
                half a = 0;
                a = max(a, AlphaMask(AlphaAt(uv + float2( offset.x,  0))));
                a = max(a, AlphaMask(AlphaAt(uv + float2(-offset.x,  0))));
                a = max(a, AlphaMask(AlphaAt(uv + float2( 0,  offset.y))));
                a = max(a, AlphaMask(AlphaAt(uv + float2( 0, -offset.y))));
                a = max(a, AlphaMask(AlphaAt(uv + float2( offset.x,  offset.y))));
                a = max(a, AlphaMask(AlphaAt(uv + float2(-offset.x,  offset.y))));
                a = max(a, AlphaMask(AlphaAt(uv + float2( offset.x, -offset.y))));
                a = max(a, AlphaMask(AlphaAt(uv + float2(-offset.x, -offset.y))));
                return a;
            }

            half RingMinAlpha(float2 uv, float2 offset)
            {
                half a = 1;
                a = min(a, AlphaMask(AlphaAt(uv + float2( offset.x,  0))));
                a = min(a, AlphaMask(AlphaAt(uv + float2(-offset.x,  0))));
                a = min(a, AlphaMask(AlphaAt(uv + float2( 0,  offset.y))));
                a = min(a, AlphaMask(AlphaAt(uv + float2( 0, -offset.y))));
                a = min(a, AlphaMask(AlphaAt(uv + float2( offset.x,  offset.y))));
                a = min(a, AlphaMask(AlphaAt(uv + float2(-offset.x,  offset.y))));
                a = min(a, AlphaMask(AlphaAt(uv + float2( offset.x, -offset.y))));
                a = min(a, AlphaMask(AlphaAt(uv + float2(-offset.x, -offset.y))));
                return a;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float wave = 1.0 + sin((input.uv.y * _PulseFrequency + _Time.y * _PulseSpeed) * 6.2831853) * _PulseAmplitude;
                if (_UseRectMask > 0.5)
                {
                    float2 safeSize = max(_RectSize.xy, float2(0.001, 0.001));
                    float2 uvSpan = max(_SpriteUvRect.zw - _SpriteUvRect.xy, float2(0.0001, 0.0001));
                    float2 rectUv = saturate((input.uv - _SpriteUvRect.xy) / uvSpan);
                    float2 edgeUv = min(rectUv, 1.0 - rectUv);
                    float edgeDistance = min(edgeUv.x * safeSize.x, edgeUv.y * safeSize.y);
                    float lineWidth = max(_OutlineWidth, 0.001);
                    half core = (half)(1.0 - smoothstep(lineWidth * 0.35, lineWidth, edgeDistance));
                    half softGlow = (half)(1.0 - smoothstep(lineWidth, lineWidth * 2.4, edgeDistance));
                    half outlineAlpha = (half)(saturate(core + softGlow * 0.32) * _OutlineColor.a);
                    half3 outlineRgb = (half3)(_OutlineColor.rgb * _GlowIntensity * wave);
                    clip(outlineAlpha - 0.001);
                    return half4(outlineRgb, outlineAlpha);
                }

                float2 sourceUv = CompensatedUv(input.uv);
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sourceUv) * input.color;
                tex.a *= InsideSpriteRect(sourceUv);
                half sourceMask = AlphaMask(tex.a);

                // Treat the old 0..0.2 width as a normalized authoring control, then sample in texture pixels.
                float pixelRadius = max(1.0, saturate(_OutlineWidth) * 140.0);
                float2 texelRadius = _MainTex_TexelSize.xy * pixelRadius;
                half nearAlpha = RingAlpha(sourceUv, texelRadius * 0.36);
                half midAlpha = RingAlpha(sourceUv, texelRadius * 0.72);
                half farAlpha = RingAlpha(sourceUv, texelRadius);
                half outer = max(nearAlpha, max(midAlpha * 0.68, farAlpha * 0.34));
                outer = (half)saturate(outer * (1.0 - sourceMask) * _OuterAlpha);

                // When the glow renderer is drawn above the sprite, this inner edge keeps the scope readable
                // even on sprites with black line art or very tight transparent padding.
                half eroded = RingMinAlpha(sourceUv, texelRadius * 0.52);
                half inner = (half)saturate(sourceMask * (1.0 - eroded) * _InnerAlpha);
                half outline = max(outer, inner);

                half outlineAlpha = (half)(outline * _OutlineColor.a);
                half3 outlineRgb = (half3)(_OutlineColor.rgb * _GlowIntensity * wave);

                half4 fill = tex;
                fill.a *= (half)_FillAlpha;
                half alpha = max(fill.a, outlineAlpha);
                half blend = alpha > 0.0001 ? saturate(outlineAlpha / alpha) : 0;
                half3 rgb = lerp(fill.rgb, outlineRgb, blend);
                clip(alpha - 0.001);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
