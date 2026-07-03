Shader "GourmetProject/UIFrostedGlass"
{
    // 严格照抄参考工程 Effects/FrostedGlass 的算法：用 _FrostTex 的明暗在 4 级模糊之间做
    // smoothstep 混合，输出模糊背景。仅额外包了 UI 必需的 Stencil / ClipRect / 顶点色，
    // 以便作为 UGUI 元素使用。模糊贴图由 FrostedGlassBlurFeature 抓取相机画面产出。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _GrabBlurTexture_0 ("Blur Level 0 (auto)", 2D) = "black" {}
        _GrabBlurTexture_1 ("Blur Level 1 (auto)", 2D) = "black" {}
        _GrabBlurTexture_2 ("Blur Level 2 (auto)", 2D) = "black" {}
        _GrabBlurTexture_3 ("Blur Level 3 (auto)", 2D) = "black" {}

        _FrostTex ("Frost Texture", 2D) = "white" {}
        _FrostIntensity ("Frost Intensity", Range(0, 1)) = 0.5

        _GlassAlpha ("Glass Alpha", Range(0,1)) = 1
        _Roundness ("Corner Roundness", Range(0,0.5)) = 0
        _Softness ("Corner Softness", Range(0.0001,0.5)) = 0.02

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
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

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
                float2 frostcoord    : TEXCOORD1;
                float4 worldPosition : TEXCOORD2;
                float4 screenPos     : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            sampler2D _GrabBlurTexture_0;
            sampler2D _GrabBlurTexture_1;
            sampler2D _GrabBlurTexture_2;
            sampler2D _GrabBlurTexture_3;
            float4 _GrabBlurTexture_0_TexelSize;

            sampler2D _FrostTex;
            float4 _FrostTex_ST;
            float _FrostIntensity;

            float _GlassAlpha;
            float _Roundness;
            float _Softness;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.frostcoord = TRANSFORM_TEX(v.texcoord, _FrostTex);
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            float RoundedRectMask(float2 uv)
            {
                float2 p = abs(uv - 0.5) * 2.0;
                float inner = saturate(1.0 - _Roundness * 2.0);
                float2 corner = max(p - inner, 0.0) / max(_Roundness * 2.0, 1e-5);
                float dist = length(corner);
                return 1.0 - smoothstep(1.0 - _Softness, 1.0, dist);
            }

            float4 GetGrabScreenPos(float4 screenPos)
            {
                // 抓取的 RenderTexture 在部分图形 API 下相对 UI 几何上下颠倒，按 texel 符号翻正。
                #if UNITY_UV_STARTS_AT_TOP
                if (_GrabBlurTexture_0_TexelSize.y < 0.0)
                {
                    screenPos.y = screenPos.w - screenPos.y;
                }
                #endif
                return screenPos;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // === 与参考 Effects/FrostedGlass 完全一致的核心逻辑 ===
                float surfSmooth = 1.0 - tex2D(_FrostTex, IN.frostcoord).r * _FrostIntensity;
                surfSmooth = saturate(surfSmooth);

                float4 grabPos = GetGrabScreenPos(IN.screenPos);
                half4 ref00 = tex2Dproj(_GrabBlurTexture_0, grabPos);
                half4 ref01 = tex2Dproj(_GrabBlurTexture_1, grabPos);
                half4 ref02 = tex2Dproj(_GrabBlurTexture_2, grabPos);
                half4 ref03 = tex2Dproj(_GrabBlurTexture_3, grabPos);

                float step00 = smoothstep(0.75, 1.00, surfSmooth);
                float step01 = smoothstep(0.5, 0.75, surfSmooth);
                float step02 = smoothstep(0.05, 0.5, surfSmooth);
                float step03 = smoothstep(0.00, 0.05, surfSmooth);

                half4 refraction = lerp(
                    ref03,
                    lerp(lerp(lerp(ref03, ref02, step02), ref01, step01), ref00, step00),
                    step03);
                // === 核心逻辑结束 ===

                // 抓取的是 HDR 相机画面，UI 目标缓冲为 LDR，压回 0-1。
                fixed3 rgb = saturate(refraction.rgb);

                // UI 包装：顶点色 alpha × 玻璃不透明度（默认 1 = 参考的不透明玻璃）+ 圆角 + 裁剪。
                fixed4 color = fixed4(rgb, IN.color.a * _GlassAlpha);
                color.a *= RoundedRectMask(IN.texcoord);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
