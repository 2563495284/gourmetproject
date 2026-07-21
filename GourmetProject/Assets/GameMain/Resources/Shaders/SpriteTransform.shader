Shader "GourmetProject/SpriteTransform"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Range(0, 1)) = 0
        _Boing ("Boing", Vector) = (0, 0, 0, 0)
        _EdgeClampPoint ("Edge Clamp Point", Vector) = (0.24, 0.24, 0, 0)

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
                float _Brightness;
                float4 _Boing;
                float4 _EdgeClampPoint;
            CBUFFER_END

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
                float2 centerOffset = input.uv - float2(0.5, 0.5);
                float2 distToCenter = abs(centerOffset);
                float2 invertedDistToCenter = float2(distToCenter.y, distToCenter.x);
                float2 maxOffset = (_EdgeClampPoint.xy - float2(
                    pow(invertedDistToCenter.x, 2.0),
                    pow(invertedDistToCenter.y, 2.0))) * _Boing.xy * sign(centerOffset);
                float2 displacedUv = input.uv + distToCenter * maxOffset;
                half4 color = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(displacedUv)) + _TextureSampleAdd) * input.color;
                color.rgb += (half)_Brightness;

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
