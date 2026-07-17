Shader "GourmetProject/DebuffMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _TextureSampleAdd ("Texture Sample Add", Vector) = (0, 0, 0, 0)
        _InactiveAlpha ("Inactive Alpha", Range(0, 1)) = 0.35
        _Saturation ("Saturation", Range(0, 1)) = 0.16
        _Darken ("Darken", Range(0, 1)) = 0.72
        _RedBias ("Red Bias", Range(0, 1)) = 0.18
        _CrossColor ("Cross Color", Color) = (1, 0.08, 0.06, 0.9)
        _CrossWidth ("Cross Width", Range(0.001, 0.2)) = 0.075
        _CrossSoftness ("Cross Softness", Range(0.001, 0.2)) = 0.035

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
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ UNITY_UI_ALPHACLIP

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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _TextureSampleAdd;
                half _InactiveAlpha;
                half _Saturation;
                half _Darken;
                half _RedBias;
                half4 _CrossColor;
                half _CrossWidth;
                half _CrossSoftness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) + _TextureSampleAdd)
                    * input.color
                    * _Color;

                half lum = dot(tex.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 muted = lerp(lum.xxx, tex.rgb, _Saturation) * _Darken;
                muted = lerp(muted, half3(1.0h, 0.12h, 0.10h), _RedBias);

                float2 uv = saturate(input.uv);
                float diagDistance = min(abs(uv.x - uv.y), abs(uv.x + uv.y - 1.0));
                half cross = (half)(1.0 - smoothstep(_CrossWidth, _CrossWidth + _CrossSoftness, diagDistance));

                half3 rgb = lerp(muted, _CrossColor.rgb, cross * _CrossColor.a);
                half alpha = tex.a * lerp(_InactiveAlpha, 1.0h, cross);

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001h);
                #endif

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
