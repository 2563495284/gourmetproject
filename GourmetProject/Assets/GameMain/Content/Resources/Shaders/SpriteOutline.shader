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
        _InnerAlpha ("Inner Edge Alpha", Range(0, 1)) = 0
        _GlowIntensity ("Glow Intensity", Range(0.25, 3)) = 1.15
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 0
        _PulseAmplitude ("Pulse Amplitude", Range(0, 0.5)) = 0
        _PulseFrequency ("Pulse Frequency", Range(0, 64)) = 18
        _AlphaThreshold ("Alpha Threshold", Range(0.001, 0.5)) = 0.08
        [PerRendererData] _UseRectMask ("Use Rectangle Mask", Float) = 0
        [PerRendererData] _UseGridMask ("Use Grid Mask", Float) = 0
        [PerRendererData] _GridOutlinePixels ("Grid Outline Pixels", Float) = 6
        [PerRendererData] _GridGlowPixels ("Grid Glow Pixels", Float) = 12
        [PerRendererData] _GridGlowAlpha ("Grid Glow Alpha", Float) = 0
        [PerRendererData] _GridFlowSpeed ("Grid Flow Speed", Float) = 0
        [PerRendererData] _GridFlowWidth ("Grid Flow Width", Float) = 0.2
        [PerRendererData] _GridFlowIntensity ("Grid Flow Intensity", Float) = 0
        [PerRendererData] _GridStripeDensity ("Grid Stripe Density", Float) = 18
        [PerRendererData] _GridStripeSpeed ("Grid Stripe Speed", Float) = 0
        [PerRendererData] _GridRevealStart ("Grid Reveal Start", Float) = 0
        [PerRendererData] _GridRevealDuration ("Grid Reveal Duration", Float) = 0
        [PerRendererData] _GridVisibility ("Grid Visibility", Range(0, 1)) = 1
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
                float _UseGridMask;
                float _GridOutlinePixels;
                float _GridGlowPixels;
                float _GridGlowAlpha;
                float _GridFlowSpeed;
                float _GridFlowWidth;
                float _GridFlowIntensity;
                float _GridStripeDensity;
                float _GridStripeSpeed;
                float _GridRevealStart;
                float _GridRevealDuration;
                float _GridVisibility;
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
                half4 sourceTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sourceUv);
                sourceTex.a *= InsideSpriteRect(sourceUv);
                // 内外遮罩必须都来自原贴图 alpha。Renderer tint 会被结算聚焦等表现层调整，
                // 若拿 tint 后的 alpha 判断内部，会把整块不透明 sprite 误判成“外圈”。
                half sourceMask = AlphaMask(sourceTex.a);
                half4 tex = sourceTex * input.color;

                if (_UseGridMask > 0.5)
                {
                    float gridPixels = floor(clamp(_GridOutlinePixels, 1.0, 8.0) + 0.5);
                    float glowPixels = max(gridPixels + 1.0, _GridGlowPixels);
                    half outside = RingAlpha(sourceUv, _MainTex_TexelSize.xy * gridPixels);
                    half middle = RingAlpha(sourceUv, _MainTex_TexelSize.xy * glowPixels * 0.62);
                    half wide = RingAlpha(sourceUv, _MainTex_TexelSize.xy * glowPixels);
                    half outsideMask = (half)(1.0 - sourceMask);
                    half core = (half)(outside * outsideMask);
                    half softGlow = (half)(saturate(max(middle * 0.62, wide * 0.32) - core) * outsideMask);

                    float reveal = 1.0;
                    float revealPulse = 0.0;
                    if (_GridRevealDuration > 0.001)
                    {
                        float elapsed = max(0.0, _Time.y - _GridRevealStart);
                        float progress = saturate(elapsed / _GridRevealDuration);
                        float revealCoordinate = saturate(dot(sourceUv, float2(0.58, 0.42)));
                        float revealCursor = progress * 1.36 - 0.18;
                        reveal = smoothstep(
                            revealCoordinate - 0.18,
                            revealCoordinate + 0.18,
                            revealCursor);
                        revealPulse = sin(progress * 3.14159265) * (1.0 - step(_GridRevealDuration, elapsed));
                    }

                    float flowPhase = frac(
                        dot(sourceUv, float2(0.76, 0.42))
                        - _Time.y * _GridFlowSpeed);
                    float flowDistance = abs(flowPhase - 0.5);
                    float flowWidth = max(0.02, _GridFlowWidth);
                    float flowBand = 1.0 - smoothstep(flowWidth * 0.24, flowWidth, flowDistance);
                    float flowEnabled = step(0.001, _GridFlowIntensity);
                    float flowMask = flowBand * flowEnabled;
                    float flowWhiten = flowBand * saturate(_GridFlowIntensity * 1.15);

                    float stripeWave = 0.5 + 0.5 * sin(
                        ((sourceUv.x - sourceUv.y) * _GridStripeDensity
                        + _Time.y * _GridStripeSpeed) * 6.2831853);
                    float stripe = smoothstep(0.30, 0.82, stripeWave);
                    half fillAlpha = (half)saturate(
                        sourceMask
                        * _FillAlpha
                        * lerp(0.82, 1.08, stripe)
                        * reveal);
                    half coreAlpha = (half)saturate(
                        core
                        * _OutlineColor.a
                        * lerp(0.86, 1.0, flowMask)
                        * reveal);
                    half glowAlpha = (half)saturate(
                        softGlow
                        * _OutlineColor.a
                        * _GridGlowAlpha
                        * (0.78 + flowBand * 0.42 + revealPulse * 0.75)
                        * reveal);
                    half edgeAlpha = max(coreAlpha, glowAlpha);
                    half alpha = (half)(max(fillAlpha, edgeAlpha) * saturate(_GridVisibility));
                    half3 fillRgb = (half3)(_OutlineColor.rgb * lerp(0.56, 0.78, stripe));
                    half3 outlineRgb = (half3)(
                        _OutlineColor.rgb
                        * _GlowIntensity
                        * wave);
                    // 单纯乘亮度会让洋红、青色等高饱和语义色迅速裁平，流光看起来像静态实线。
                    // 高亮头部向白色过渡，确保所有颜色、Action/Condition 两种范围都能看清循环方向。
                    half3 flowHighlightRgb = (half3)(
                        half3(1.0, 1.0, 1.0)
                        * max(1.0, _GlowIntensity)
                        * wave);
                    outlineRgb = lerp(outlineRgb, flowHighlightRgb, flowWhiten);
                    half edgeBlend = alpha > 0.0001 ? saturate(edgeAlpha / alpha) : 0;
                    half3 rgb = lerp(fillRgb, outlineRgb, edgeBlend);
                    clip(alpha - 0.001);
                    return half4(rgb, alpha);
                }

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
