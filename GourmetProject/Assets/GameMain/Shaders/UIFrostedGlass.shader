Shader "GourmetProject/UIFrostedGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 由 UIFrostedGlass 组件每帧绑定为场景模糊贴图（Overlay Canvas 拿不到 SRP 全局贴图，需声明为材质属性才能可靠绑定）。
        _FrostedGlassTex ("Blur Texture (auto)", 2D) = "black" {}
        _TintColor ("Glass Tint", Color) = (1,1,1,1)
        _TintStrength ("Tint Strength", Range(0,1)) = 0.35
        _GlassAlpha ("Glass Alpha", Range(0,1)) = 0.9
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
                float4 worldPosition : TEXCOORD1;
                float4 screenPos     : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            sampler2D _FrostedGlassTex;
            fixed4 _TintColor;
            float _TintStrength;
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
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            float RoundedRectMask(float2 uv)
            {
                // 0 at center, 1 at edges on each axis
                float2 p = abs(uv - 0.5) * 2.0;
                float inner = saturate(1.0 - _Roundness * 2.0);
                float2 corner = max(p - inner, 0.0) / max(_Roundness * 2.0, 1e-5);
                float dist = length(corner);
                return 1.0 - smoothstep(1.0 - _Softness, 1.0, dist);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                fixed4 blurred = tex2D(_FrostedGlassTex, screenUV);

                fixed3 rgb = lerp(blurred.rgb, _TintColor.rgb, saturate(_TintStrength * _TintColor.a));
                rgb *= IN.color.rgb;

                // 半透明磨砂：整体不透明度由 _GlassAlpha 主控，让底下清晰世界略微透出，
                // 不再受 _MainTex（背景 Image 无 sprite 时为白）强制拉成不透明。
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
