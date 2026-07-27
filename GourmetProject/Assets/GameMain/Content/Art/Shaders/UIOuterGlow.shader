Shader "GourmetProject/UIOuterGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // quad（含 padding）在像素下的尺寸，用于把 UV 换算成各向同性的像素坐标。
        _QuadSize ("Quad Size (px)", Vector) = (456, 636, 0, 0)
        // 卡牌矩形相对 quad 每侧的边距（像素）。内层卡矩形 = quad - 2*padding。
        _Padding ("Padding (px)", Float) = 48
        // 卡牌圆角半径（像素）。
        _CornerRadius ("Corner Radius (px)", Float) = 28
        // 光晕向外扩散的宽度（像素）。
        _GlowWidth ("Glow Width (px)", Float) = 44
        // 衰减指数，越大越集中在边缘。
        _Falloff ("Glow Falloff", Range(0.25, 4)) = 1.6

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
            #pragma target 2.0

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

            fixed4 _Color;
            float4 _ClipRect;

            float4 _QuadSize;
            float _Padding;
            float _CornerRadius;
            float _GlowWidth;
            float _Falloff;

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

            // 圆角矩形有向距离场：外部为正、内部为负，单位与传入坐标一致（像素）。
            float SdRoundBox(float2 p, float2 halfExtent, float radius)
            {
                float2 q = abs(p) - halfExtent + radius;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 quad = _QuadSize.xy;
                // 以 quad 中心为原点的像素坐标。
                float2 p = (IN.texcoord - 0.5) * quad;
                // 内层卡矩形半尺寸。
                float2 halfExt = quad * 0.5 - float2(_Padding, _Padding);
                float radius = min(_CornerRadius, min(halfExt.x, halfExt.y));

                float dist = SdRoundBox(p, halfExt - radius, radius);

                // 边缘处最亮，向外 _GlowWidth 内渐隐。
                float glow = 1.0 - smoothstep(0.0, max(_GlowWidth, 1e-3), dist);
                glow = pow(saturate(glow), _Falloff);
                // 卡内（被 Art 遮住）不出光，避免半透明处叠色；边缘留 2px 过渡。
                float outer = smoothstep(-2.0, 0.0, dist);

                fixed4 col = IN.color;
                col.a *= glow * outer;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                clip(col.a - 0.001);
                return col;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
