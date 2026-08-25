Shader "GourmetProject/SettlementFeverScreen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0,1.5)) = 1
        _Pulse ("Burst Pulse", Range(0,1)) = 0
        _FeverTime ("Effect Time", Float) = 0
        _Aspect ("Aspect Ratio", Float) = 1.777778
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
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "FeverScreen"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _Intensity;
            float _Pulse;
            float _FeverTime;
            float _Aspect;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            float SoftSignal(float value)
            {
                return value * 0.5 + 0.5;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uv = input.texcoord;
                float time = _FeverTime;
                float2 centered = uv - 0.5;
                float aspect = clamp(_Aspect, 0.45, 3.2);

                // 屏幕中央保留大块透明安全区；热量从左右和下沿向内包围。
                float sideEdge = pow(saturate(abs(centered.x) * 2.0), 3.15);
                float bottomEdge = pow(saturate(1.0 - uv.y), 2.65);
                float topEdge = pow(saturate(uv.y), 5.0) * 0.12;
                float2 ellipse = centered / float2(0.43, 0.37);
                ellipse.x *= min(1.0, aspect / 1.6);
                float centerSafety = smoothstep(0.52, 1.22, length(ellipse));

                // 两组不同频率的缓慢波动构成参考图中的低频烟雾和热浪。
                float waveA = sin(
                    uv.x * 12.5
                    + sin(uv.y * 8.2 - time * 0.72) * 1.75
                    + time * 0.46);
                float waveB = sin(
                    uv.x * 22.0
                    - uv.y * 10.0
                    + sin(uv.x * 7.0 + time * 0.38) * 1.25
                    - time * 0.54);
                float waveC = sin(
                    uv.y * 15.0
                    + centered.x * centered.x * 17.0
                    - time * 0.62);
                float smokeNoise = saturate(
                    SoftSignal(waveA) * 0.52
                    + SoftSignal(waveB) * 0.30
                    + SoftSignal(waveC) * 0.18);
                smokeNoise = smoothstep(0.35, 0.82, smokeNoise);

                float edgeMask = saturate(sideEdge * 0.88 + bottomEdge * 0.82 + topEdge);
                edgeMask *= lerp(0.32, 1.0, centerSafety);
                float smokeMask = smokeNoise
                    * saturate(bottomEdge * 1.18 + sideEdge * 0.58)
                    * centerSafety;

                // 下沿的窄亮带和弯曲细带提供“爆燃”而非普通红色暗角的感觉。
                float risingBand = sin(
                    centered.x * 19.0
                    + time * 1.12
                    + sin(centered.x * 8.0 - time * 0.66));
                float heatRibbon = smoothstep(0.73, 0.98, SoftSignal(risingBand));
                heatRibbon *= pow(saturate(1.0 - uv.y), 4.2) * centerSafety;

                float pulse = saturate(_Pulse);

                // 底部实体火焰控制在约屏幕高度的 4%–15%；更高区域只保留烟、热浪和火星。
                float flameBroad = SoftSignal(sin(
                    uv.x * 18.0
                    - time * 2.75
                    + sin(uv.x * 7.5 + time * 1.15) * 1.65));
                float flameDetail = SoftSignal(sin(
                    uv.x * 43.0
                    + time * 3.25
                    + sin(uv.x * 15.0 - time * 1.85) * 1.10));
                float tongueSignal = saturate(
                    pow(flameBroad, 1.55) * 0.72
                    + pow(flameDetail, 2.15) * 0.28);
                float flameHeight = 0.040
                    + tongueSignal * 0.075
                    + smokeNoise * 0.012
                    + pulse * 0.018;
                float flameWarp = sin(
                    uv.y * 21.0
                    + uv.x * 13.0
                    - time * 3.35) * 0.005;
                float outerFlame = smoothstep(
                    flameHeight + 0.018,
                    flameHeight - 0.014,
                    uv.y + flameWarp);
                outerFlame *= lerp(0.64, 1.0, centerSafety);

                float innerHeight = 0.015
                    + flameHeight * 0.40
                    + flameDetail * 0.010
                    + pulse * 0.006;
                float innerFlame = smoothstep(
                    innerHeight + 0.011,
                    innerHeight - 0.009,
                    uv.y - flameWarp * 0.45);
                innerFlame *= outerFlame;

                // 两侧火舌沿屏幕高度错落窜入，升档脉冲时会短暂向中心伸展。
                float sideDistance = min(uv.x, 1.0 - uv.x);
                float sideSignal = SoftSignal(sin(
                    uv.y * 21.0
                    - time * 2.45
                    + sin(uv.y * 8.0 + time * 1.70) * 1.45));
                float sideReach = 0.012
                    + pow(sideSignal, 2.20) * 0.038
                    + pulse * 0.008;
                float sideFlame = smoothstep(
                    sideReach + 0.010,
                    sideReach - 0.006,
                    sideDistance);
                sideFlame *= 0.48 + pow(saturate(1.0 - uv.y), 0.72) * 0.52;

                float glowMask = edgeMask * (0.52 + smokeNoise * 0.30)
                    + heatRibbon * (0.62 + pulse * 0.58)
                    + outerFlame * 0.72
                    + sideFlame * 0.58;
                float darkMask = smokeMask * (0.58 + pulse * 0.12);
                float alpha = (edgeMask * 0.155
                    + darkMask * 0.110
                    + heatRibbon * 0.135
                    + outerFlame * 0.255
                    + innerFlame * 0.105
                    + sideFlame * 0.205)
                    * _Intensity
                    * (1.0 + pulse * 0.42);
                alpha = saturate(alpha * input.color.a);

                fixed3 deepSmoke = fixed3(0.055, 0.003, 0.0005);
                fixed3 emberRed = fixed3(0.72, 0.045, 0.0025);
                fixed3 hotOrange = fixed3(1.0, 0.26, 0.012);
                fixed3 hotYellow = fixed3(1.0, 0.64, 0.075);
                float flameMask = saturate(outerFlame + sideFlame * 0.82);
                float hotMix = saturate(
                    glowMask * 0.72
                    + innerFlame * 0.82
                    + pulse * heatRibbon * 0.44);
                fixed3 rgb = lerp(deepSmoke, emberRed, saturate(glowMask));
                rgb = lerp(rgb, emberRed, flameMask * 0.82);
                rgb = lerp(rgb, hotOrange, hotMix * (0.32 + pulse * 0.28));
                rgb = lerp(
                    rgb,
                    hotYellow,
                    innerFlame * (0.44 + pulse * 0.18));
                rgb *= input.color.rgb;

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(rgb * alpha, alpha);
            }
            ENDCG
        }
    }
}
