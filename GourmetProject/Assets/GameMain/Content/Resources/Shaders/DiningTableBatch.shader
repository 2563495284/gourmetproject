Shader "GourmetProject/DiningTableBatch"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SpriteUvRect ("Sprite UV Rect", Vector) = (0, 0, 1, 1)
        _InactiveAlpha ("Inactive Alpha", Range(0, 1)) = 0.35
        _Saturation ("Saturation", Range(0, 1)) = 0.16
        _Darken ("Darken", Range(0, 1)) = 0.72
        _RedBias ("Red Bias", Range(0, 1)) = 0.18
        _CrossColor ("Cross Color", Color) = (1, 0.08, 0.06, 0.9)
        _CrossWidth ("Cross Width", Range(0.001, 0.2)) = 0.075
        _CrossSoftness ("Cross Softness", Range(0.001, 0.2)) = 0.035
        _PulseBrightness ("Pulse Brightness", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
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
                float4 cellData : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 cellUv : TEXCOORD1;
                half disabled : TEXCOORD2;
                half pulse : TEXCOORD3;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _SpriteUvRect;
                half _InactiveAlpha;
                half _Saturation;
                half _Darken;
                half _RedBias;
                half4 _CrossColor;
                half _CrossWidth;
                half _CrossSoftness;
                half _PulseBrightness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Color;
                output.uv = input.uv;
                output.cellUv = input.cellData.xy;
                output.disabled = (half)input.cellData.z;
                output.pulse = (half)input.cellData.w;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centerOffset = input.cellUv - float2(0.5, 0.5);
                float2 distToCenter = abs(centerOffset);
                float2 invertedDistance = distToCenter.yx;
                float2 clampPoint = float2(0.24, 0.24);
                float2 boing = float2(0.2, -0.14) * input.pulse;
                float2 maxOffset = (clampPoint - invertedDistance * invertedDistance)
                    * boing
                    * sign(centerOffset);
                float2 displacedCellUv = saturate(
                    input.cellUv + distToCenter * maxOffset);
                float2 displacedUv = lerp(
                    _SpriteUvRect.xy,
                    _SpriteUvRect.zw,
                    displacedCellUv);

                half4 normal = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, displacedUv)
                    * input.color;
                normal.rgb += _PulseBrightness * input.pulse;

                half luminance = dot(normal.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 muted = lerp(luminance.xxx, normal.rgb, _Saturation) * _Darken;
                muted = lerp(muted, half3(1.0h, 0.12h, 0.10h), _RedBias);

                float diagonalDistance = min(
                    abs(input.cellUv.x - input.cellUv.y),
                    abs(input.cellUv.x + input.cellUv.y - 1.0));
                half cross = (half)(1.0 - smoothstep(
                    _CrossWidth,
                    _CrossWidth + _CrossSoftness,
                    diagonalDistance));
                half3 disabledRgb = lerp(
                    muted,
                    _CrossColor.rgb,
                    cross * _CrossColor.a);
                half disabledAlpha = normal.a * lerp(_InactiveAlpha, 1.0h, cross);

                half enabledMask = step(0.5h, input.disabled);
                half4 color = half4(
                    lerp(normal.rgb, disabledRgb, enabledMask),
                    lerp(normal.a, disabledAlpha, enabledMask));
                clip(color.a - 0.001h);
                return color;
            }
            ENDHLSL
        }
    }
}
