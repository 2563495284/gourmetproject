Shader "GourmetProject/UIAlphaConvolutionGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // Expanded glow quad size and the size at which the source sprite is drawn.
        _QuadSize ("Quad Size (UI px)", Vector) = (208, 256, 0, 0)
        _ContentSize ("Content Size (UI px)", Vector) = (160, 208, 0, 0)
        // xy = atlas UV minimum, zw = atlas UV size.
        _SpriteUVRect ("Sprite UV Rect", Vector) = (0, 0, 1, 1)
        _GlowRadius ("Glow Radius (UI px)", Range(1, 32)) = 12
        _GlowIntensity ("Glow Intensity", Range(0, 4)) = 1.6
        _Falloff ("Glow Falloff", Range(0.25, 4)) = 1.1
        _AlphaThreshold ("Source Alpha Threshold", Range(0.001, 1)) = 0.1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
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
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _QuadSize;
            float4 _ContentSize;
            float4 _SpriteUVRect;
            float _GlowRadius;
            float _GlowIntensity;
            float _Falloff;
            float _AlphaThreshold;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            float SampleSpriteAlpha(float2 sourceUV)
            {
                float2 inside = step(0.0, sourceUV) * step(sourceUV, 1.0);
                float2 atlasUV = _SpriteUVRect.xy + sourceUV * _SpriteUVRect.zw;
                float alpha = saturate(tex2D(_MainTex, atlasUV).a + _TextureSampleAdd.a);
                return alpha * inside.x * inside.y;
            }

            float KernelWeight(int index)
            {
                index = abs(index);
                return index == 0
                    ? 20.0
                    : (index == 1 ? 15.0 : (index == 2 ? 6.0 : 1.0));
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 quadSize = max(_QuadSize.xy, float2(1.0, 1.0));
                float2 contentSize = max(_ContentSize.xy, float2(1.0, 1.0));
                float2 localUV = (IN.texcoord - _SpriteUVRect.xy)
                    / max(_SpriteUVRect.zw, float2(1e-6, 1e-6));

                // Map the expanded glow quad back onto the unpadded source sprite.
                float2 sourceUV = (localUV - 0.5) * quadSize / contentSize + 0.5;
                float sourceAlpha = SampleSpriteAlpha(sourceUV);

                // A direct 7x7 binomial Gaussian convolution of the source alpha.
                // The separable weights [1 6 15 20 15 6 1] sum to 64 per axis.
                float2 kernelStep = (_GlowRadius / 3.0) / contentSize;
                float blurredAlpha = 0.0;
                [unroll]
                for (int y = -3; y <= 3; y++)
                {
                    [unroll]
                    for (int x = -3; x <= 3; x++)
                    {
                        float weight = KernelWeight(x) * KernelWeight(y);
                        blurredAlpha += SampleSpriteAlpha(
                            sourceUV + float2(x, y) * kernelStep) * weight;
                    }
                }
                blurredAlpha *= 1.0 / 4096.0;

                // Remove the original sprite, leaving only the alpha convolution outside it.
                float outside = 1.0 - smoothstep(
                    _AlphaThreshold * 0.5,
                    _AlphaThreshold,
                    sourceAlpha);
                float glow = pow(
                    saturate(blurredAlpha * _GlowIntensity),
                    _Falloff) * outside;

                fixed4 color = IN.color;
                color.a *= glow;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                clip(color.a - 0.001);
                return color;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
