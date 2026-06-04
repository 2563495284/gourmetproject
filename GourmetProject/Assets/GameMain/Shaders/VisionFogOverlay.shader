Shader "GourmetProject/VisionFogOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _FogColor ("Fog Color", Color) = (0.02, 0.03, 0.05, 1)
        _Center ("Screen Center (0-1)", Vector) = (0.5, 0.5, 0, 0)
        _InnerRadius ("Clear disk (height-normalized)", Float) = 0.2
        _OuterRadius ("Full fog (height-normalized)", Float) = 0.28
        _Aspect ("Screen Aspect (width/height)", Float) = 1.777
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "true"
            "PreviewType" = "Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "VisionFogOverlay"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float4 _Center;
                float _InnerRadius;
                float _OuterRadius;
                float _Aspect;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 delta = input.uv - _Center.xy;
                delta.x *= _Aspect;
                float dist = length(delta);

                // 圆内全透明；圆外缘向黑雾软过渡（smoothstep 可调宽窄）
                half alpha = smoothstep(_InnerRadius, _OuterRadius, dist);
                return half4(_FogColor.rgb, alpha * _FogColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
