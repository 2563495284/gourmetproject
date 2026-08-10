Shader "GourmetProject/FlavorGlyphParticle"
{
    Properties
    {
        _FlavorColor ("Flavor Color", Color) = (1,1,1,1)
        _FlavorIndex ("Flavor Index", Range(0,6)) = 0
        _Intensity ("Intensity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "IgnoreProjector"="True" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _FlavorColor;
                float _FlavorIndex;
                float _Intensity;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float Line(float2 p, float2 a, float2 b, float width)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.0001));
                return 1.0 - smoothstep(width, width + 0.055, length(pa - ba * h));
            }

            float Glyph(int flavor, float2 p)
            {
                if (flavor == 0)
                {
                    p.y += 0.08;
                    float x2 = p.x * p.x;
                    float y2 = p.y * p.y;
                    float heart = (x2 + y2 - 0.45);
                    float implicitValue = heart * heart * heart - x2 * p.y * p.y * p.y;
                    return 1.0 - smoothstep(-0.035, 0.035, implicitValue);
                }
                if (flavor == 1)
                {
                    float ring = 1.0 - smoothstep(0.065, 0.13, abs(length(p) - 0.49));
                    float bubble = 1.0 - smoothstep(0.12, 0.2, length(p - float2(0.33, 0.31)));
                    return max(ring, bubble);
                }
                if (flavor == 2)
                {
                    float crack = Line(p, float2(-0.58, 0.48), float2(-0.08, 0.02), 0.075);
                    crack = max(crack, Line(p, float2(-0.08, 0.02), float2(0.28, -0.5), 0.075));
                    crack = max(crack, Line(p, float2(-0.08, 0.02), float2(0.55, 0.28), 0.07));
                    crack = max(crack, Line(p, float2(0.13, -0.28), float2(0.5, -0.14), 0.055));
                    return crack;
                }
                if (flavor == 3)
                {
                    float diamond = abs(p.x) + abs(p.y);
                    float shell = 1.0 - smoothstep(0.58, 0.67, diamond);
                    float cut = smoothstep(0.36, 0.43, diamond);
                    return max(shell * cut, 1.0 - smoothstep(0.13, 0.2, diamond));
                }
                if (flavor == 4)
                {
                    float bolt = Line(p, float2(0.17, 0.66), float2(-0.15, 0.08), 0.11);
                    bolt = max(bolt, Line(p, float2(-0.15, 0.08), float2(0.16, 0.08), 0.11));
                    bolt = max(bolt, Line(p, float2(0.16, 0.08), float2(-0.2, -0.67), 0.11));
                    return bolt;
                }
                if (flavor == 5)
                {
                    float angle = atan2(p.y, p.x);
                    float radius = length(p);
                    float spiral = abs(radius - (0.17 + frac((angle + 3.14159) / 6.28318) * 0.43));
                    return 1.0 - smoothstep(0.065, 0.125, spiral);
                }

                float body = 1.0 - smoothstep(0.50, 0.59, length(float2(p.x * 1.25, p.y + 0.13)));
                float tip = 1.0 - smoothstep(0.26, 0.36, length(float2(p.x * 1.55, p.y - 0.44)));
                float hollow = 1.0 - smoothstep(0.17, 0.27, length(float2(p.x * 1.5, p.y + 0.18)));
                return saturate(max(body, tip) - hollow * 0.65);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                int flavor = (int)round(_FlavorIndex);
                float glyph = Glyph(flavor, p);
                float outer = 1.0 - smoothstep(0.82, 1.02, length(p));
                float ink = saturate(glyph * outer);
                float halo = (1.0 - smoothstep(0.55, 1.0, length(p))) * 0.22;
                half3 color = lerp(_FlavorColor.rgb * 0.22, _FlavorColor.rgb, (half)ink);
                float alpha = saturate((ink + halo) * input.color.a * _FlavorColor.a * _Intensity);
                return half4(color, (half)alpha);
            }
            ENDHLSL
        }
    }
}
