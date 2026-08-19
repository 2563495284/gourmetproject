Shader "GourmetProject/NegativePassiveCloud"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SmokeColor ("Smoke Color", Color) = (0.008, 0.001, 0.016, 1)
        _PurpleColor ("Purple Color", Color) = (0.42, 0.018, 0.68, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0.95
        _Speed ("Speed", Range(0, 2)) = 0.90
        _Seed ("Seed", Float) = 0
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

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Negative Cloud"

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            fixed4 _SmokeColor;
            fixed4 _PurpleColor;
            float _Intensity;
            float _Speed;
            float _Seed;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(Hash21(i), Hash21(i + float2(1.0, 0.0)), f.x),
                    lerp(Hash21(i + float2(0.0, 1.0)), Hash21(i + 1.0), f.x),
                    f.y);
            }

            float Fbm(float2 p)
            {
                float value = Noise(p) * 0.56;
                value += Noise(p * 2.03 + 13.7) * 0.29;
                value += Noise(p * 4.11 - 7.2) * 0.15;
                return value;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.color = input.color * _Color;
                output.uv = input.uv;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float time = _Time.y * _Speed;
                float seed = frac(_Seed * 0.017) * 31.0;
                float2 flowA = float2(p.x * 2.4 - time * 0.62, p.y * 3.3 + seed);
                float2 flowB = float2(p.x * 3.1 + time * 0.42, p.y * 4.2 - seed * 0.37);
                float noiseA = Fbm(flowA + float2(seed, 0.0));
                float noiseB = Fbm(flowB + float2(-seed * 0.4, 11.3));
                float cloudNoise = noiseA * 0.66 + noiseB * 0.34;

                float horizontalExtent = abs(p.x) + (cloudNoise - 0.5) * 0.18;
                float verticalExtent = abs(p.y) + (cloudNoise - 0.5) * 0.26;
                float sideFade = smoothstep(0.16, 0.38, horizontalExtent)
                    * (1.0 - smoothstep(0.74, 1.06, horizontalExtent));
                float verticalFade = 1.0 - smoothstep(0.24, 0.68, verticalExtent);
                float billows = smoothstep(0.46, 0.82, cloudNoise);
                float density = sideFade * verticalFade * lerp(0.12, 1.0, billows);

                float purplePulse = saturate((cloudNoise - 0.40) * 1.8);
                purplePulse *= 0.68 + sin(time * 2.1 + p.x * 5.0 + seed) * 0.22;
                fixed3 rgb = lerp(_SmokeColor.rgb, _PurpleColor.rgb, purplePulse * 0.62);
                rgb += _PurpleColor.rgb * smoothstep(0.80, 0.96, cloudNoise) * 0.16;

                fixed alpha = saturate(density * _Intensity * 0.76) * input.color.a;
                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
