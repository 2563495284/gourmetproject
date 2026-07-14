Shader "GourmetProject/SpriteTransform"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Range(0, 1)) = 0
        _Boing ("Boing", Vector) = (0, 0, 0, 0)
        _EdgeClampPoint ("Edge Clamp Point", Vector) = (0.24, 0.24, 0, 0)
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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Brightness;
                float4 _Boing;
                float4 _EdgeClampPoint;
            CBUFFER_END

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
                float2 centerOffset = input.uv - float2(0.5, 0.5);
                float2 distToCenter = abs(centerOffset);
                float2 invertedDistToCenter = float2(distToCenter.y, distToCenter.x);
                float2 maxOffset = (_EdgeClampPoint.xy - float2(
                    pow(invertedDistToCenter.x, 2.0),
                    pow(invertedDistToCenter.y, 2.0))) * _Boing.xy * sign(centerOffset);
                float2 displacedUv = input.uv + distToCenter * maxOffset;
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(displacedUv)) * input.color;
                color.rgb += (half)_Brightness;
                return color;
            }
            ENDHLSL
        }
    }
}
