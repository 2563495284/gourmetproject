Shader "GourmetProject/SettlementScoreFlame"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Colour1 ("Outer Flame", Color) = (0.996,0.373,0.333,1)
        _Colour2 ("Core Flame", Color) = (1,0.619,0.071,1)
        _FlameTime ("Flame Time", Float) = 0
        _Amount ("Amount", Float) = 0
        _Seed ("Seed", Float) = 17
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

            fixed4 frag(v2f input) : SV_Target
            {
                float intensity = min(10.0, _Amount);
                if (intensity < 0.1)
                {
                    return 0;
                }

                // LÖVE 提交给 effect() 的局部纹理坐标与 Unity UI 在此处方向一致。
                float2 uv = input.texcoord - 0.5;
                uv.x *= 1.32;
                uv.y *= 0.72;
                // 将原始噪声场约束在主焰向上的形态区间内，连续往返而不发生跳帧。
                float referenceTime = 0.75
                    + 0.75 * sin(_FlameTime * 0.82 + _Seed * 0.013);
                float2 centeredUv = uv;
                centeredUv += centeredUv * 0.01
                    * (sin(-1.123 * uv.x + 0.2 * referenceTime)
                    * cos(5.3332 * uv.y + referenceTime * 0.931));

                float2 flameUp = float2(
                    0.0,
                    fmod(4.0 * referenceTime, 10000.0) - 5000.0 + fmod(1.781 * _Seed, 1000.0));
                float scaleFactor = 7.5 + 3.0 / (2.0 + 2.0 * intensity);
                float2 sv = centeredUv * scaleFactor + flameUp;
                float speed = fmod(20.781 * _Seed, 100.0)
                    + sin(referenceTime + _Seed) * cos(referenceTime * 0.151 + _Seed);
                float2 sv2 = 0;

                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    sv2 += sv + 0.05 * sv2.yx
                        + 0.3 * (cos(length(sv) * 0.411)
                        + 0.3344 * sin(length(sv))
                        - 0.23 * cos(length(sv)));
                    sv += 0.5 * float2(
                        cos(cos(sv2.y) + speed * 0.0812)
                            * sin(3.22 + sv2.x - speed * 0.1531),
                        sin(-sv2.x * 1.21222 + 0.113785 * speed)
                            * cos(sv2.y * 0.91213 - 0.13582 * speed));
                }

                float smoke = max(
                    0.0,
                    (length((sv - flameUp) / scaleFactor * 5.0)
                    + 0.1 * (length(centeredUv) - 0.5))
                    * (2.0 / (2.0 + intensity * 0.2)));
                smoke += max(0.0, 2.0 - 0.3 * intensity)
                    * max(0.0, 2.0 * (centeredUv.y - 0.5) * (centeredUv.y - 0.5));

                if (abs(uv.x) > 0.4)
                {
                    smoke += 10.0 * (abs(uv.x) - 0.4);
                }

                float cavity = length((uv - float2(0.0, 0.1)) * float2(0.19, 1.0));
                if (cavity < min(0.1, intensity * 0.5) && smoke > 1.0)
                {
                    smoke += min(8.5, intensity * 10.0) * (cavity - 0.1);
                }

                fixed4 result = _Colour1;
                if (uv.y < 0.12)
                {
                    result = result * (1.0 - 0.5 * (0.12 - uv.y))
                        + 2.5 * (0.12 - uv.y) * _Colour2;
                    result += result * (-2.0 + 0.5 * intensity * smoke) * (0.12 - uv.y);
                }

                float innerHeat = 1.0 - smoothstep(0.28, 0.92, smoke);
                result.rgb = lerp(result.rgb, _Colour2.rgb, innerHeat * 0.55);

                // 原版是二值像素边；这里保留火场算法，只将轮廓改为连续软边。
                result.a = 1.0 - smoothstep(0.94, 1.04, smoke);
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
