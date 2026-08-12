Shader "GourmetProject/SettlementScoreFlame"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Colour1 ("Outer Flame", Color) = (0.68,0.14,0.025,1)
        _Colour2 ("Core Flame", Color) = (1,0.48,0.08,1)
        _FlameTime ("Flame Time", Float) = 0
        _Amount ("Amount", Float) = 0
        _Seed ("Seed", Float) = 1
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
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _Colour1;
            fixed4 _Colour2;
            float _FlameTime;
            float _Amount;
            float _Seed;
            float4 _ClipRect;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                local = local * local * (3.0 - 2.0 * local);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + 1.0);
                return lerp(lerp(a, b, local.x), lerp(c, d, local.x), local.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float weight = 0.5;
                [unroll]
                for (int octave = 0; octave < 4; octave++)
                {
                    value += ValueNoise(p) * weight;
                    p = p * 2.03 + float2(11.7, 7.9);
                    weight *= 0.5;
                }
                return value;
            }

            float FlameTongue(
                float2 p,
                float offset,
                float width,
                float height,
                float baseLift,
                float phase,
                float seed)
            {
                float normalizedY = (p.y - baseLift) / max(0.01, height);
                float flow = Fbm(float2(
                    (p.x + seed) * 4.1,
                    normalizedY * 3.2 - _FlameTime * (1.15 + phase * 0.12)));
                float sway = (flow - 0.5) * (0.13 + normalizedY * 0.17)
                    + sin(_FlameTime * (2.1 + phase * 0.1) + normalizedY * 5.4 + seed) * 0.035;
                // 参考图是纵向堆叠的鼓包与收腰，不是从底到顶线性变窄的三角锥。
                float y01 = saturate(normalizedY);
                float lowerBulge = exp(-pow((y01 - 0.22) / 0.20, 2.0));
                float upperBulge = exp(-pow((y01 - 0.58) / 0.14, 2.0));
                float tipTaper = 1.0 - smoothstep(0.70, 1.0, y01);
                float widthProfile = (0.58 + lowerBulge * 0.34 + upperBulge * 0.25) * tipTaper;
                float radius = width * widthProfile;
                float edge = radius - abs(p.x - offset - sway);
                edge += (flow - 0.5) * 0.075;
                float normalizedX = (p.x - offset) / max(0.01, width);
                float curvedBottom = normalizedY
                    - 0.025
                    - 0.26 * saturate(abs(normalizedX)) * saturate(abs(normalizedX));
                float verticalBounds = min(curvedBottom, 1.0 - normalizedY);
                return min(edge, verticalBounds * 0.32);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float intensity = min(10.0, _Amount);
                if (intensity < 0.1)
                {
                    return 0;
                }

                // 在四周留出透明边距，让一个连通的完整火焰轮廓不会被 UI 矩形裁平。
                float2 p = input.texcoord;
                p.x -= 0.5;
                p.y = (p.y - 0.045) / 0.91;
                float phase = saturate(intensity / 3.25);
                float baseHeight = lerp(0.70, 0.82, phase);
                float seed = frac(_Seed * 0.01781) * 17.0;

                // 单束连续主焰：下部鼓起、中段收腰、上部二次鼓起后收尖。
                float field = FlameTongue(p, 0.00, 0.28, baseHeight, 0.0, phase, seed + 6.2);
                float outer = smoothstep(-0.045, 0.025, field);
                float body = smoothstep(-0.005, 0.085, field);
                float innerField = FlameTongue(
                    p,
                    -0.012,
                    0.145,
                    baseHeight * 0.52,
                    0.07,
                    phase,
                    seed + 10.4);
                float core = smoothstep(-0.025, 0.035, innerField);

                // 主焰上方脱离的余烬，对应参考中向上漂散的小火块。
                float emberSway = sin(_FlameTime * 2.2 + seed) * 0.018;
                float ember1 = 1.0 - smoothstep(
                    0.020,
                    0.040,
                    length(p - float2(0.035 + emberSway, baseHeight + 0.035)));
                float ember2 = 1.0 - smoothstep(
                    0.013,
                    0.030,
                    length(p - float2(-0.018 - emberSway, baseHeight + 0.115)));
                float ember3 = 1.0 - smoothstep(
                    0.008,
                    0.022,
                    length(p - float2(0.025 + emberSway * 0.7, baseHeight + 0.175)));
                float embers = max(ember1, max(ember2, ember3)) * smoothstep(0.35, 0.8, phase);
                fixed4 bodyColor = lerp(_Colour1, _Colour2, body * 0.62);
                fixed3 hotCore = lerp(_Colour2.rgb, fixed3(1.0, 0.86, 0.62), 0.55);
                fixed3 flameColor = lerp(bodyColor.rgb, hotCore, core * 0.42);
                fixed4 result = fixed4(lerp(flameColor, _Colour2.rgb, embers * 0.75), 1.0);
                result.a = max(outer, embers * 0.84) * lerp(0.68, 0.90, phase);

                result *= input.color;
                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif
                return result;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
