Shader "GourmetProject/GrotesqueCartoonPostProcess"
{
    Properties
    {
        _Saturation ("Saturation", Float) = 1.18
        _Contrast ("Contrast", Float) = 1.08
        _PosterizeSteps ("Posterize Steps", Float) = 18
        _InkStrength ("Ink Strength", Range(0, 1)) = 0.28
        _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.18
        _WarmTint ("Warm Tint", Color) = (1.05, 0.96, 0.86, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "GrotesqueCartoonPostProcess"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Saturation;
            float _Contrast;
            float _PosterizeSteps;
            float _InkStrength;
            float _VignetteStrength;
            float4 _WarmTint;

            float CartoonLuminance(float3 color)
            {
                return dot(color, float3(0.299, 0.587, 0.114));
            }

            float3 ApplySaturation(float3 color, float amount)
            {
                float gray = CartoonLuminance(color);
                return lerp(gray.xxx, color, amount);
            }

            float3 ApplyContrast(float3 color, float amount)
            {
                return (color - 0.5) * amount + 0.5;
            }

            half4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;
                float4 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel);

                float2 texel = _BlitTexture_TexelSize.xy;
                float center = CartoonLuminance(color.rgb);
                float left = CartoonLuminance(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(-texel.x, 0), _BlitMipLevel).rgb);
                float right = CartoonLuminance(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, 0), _BlitMipLevel).rgb);
                float down = CartoonLuminance(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(0, -texel.y), _BlitMipLevel).rgb);
                float up = CartoonLuminance(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(0, texel.y), _BlitMipLevel).rgb);
                float edge = abs(center - left) + abs(center - right) + abs(center - down) + abs(center - up);
                edge = smoothstep(0.08, 0.32, edge) * _InkStrength;

                float3 graded = color.rgb * _WarmTint.rgb;
                graded = ApplySaturation(graded, _Saturation);
                graded = ApplyContrast(graded, _Contrast);

                float steps = max(2.0, _PosterizeSteps);
                graded = floor(saturate(graded) * steps) / steps;
                graded *= 1.0 - edge;

                float2 centeredUv = uv * 2.0 - 1.0;
                float vignette = saturate(dot(centeredUv, centeredUv));
                graded *= 1.0 - vignette * _VignetteStrength;

                return half4(saturate(graded), color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
