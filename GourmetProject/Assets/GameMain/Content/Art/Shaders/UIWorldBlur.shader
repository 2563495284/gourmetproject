Shader "GourmetProject/FrostedGlassBlur"
{
    // 可分离高斯模糊（照抄参考项目 SeparableBlur.shader 的 7-tap 权重），改写为 URP fullscreen blit。
    // 由 FrostedGlassBlurFeature 通过 RenderGraph AddBlitPass 逐级调用：Pass 0 水平，Pass 1 垂直。
    Properties
    {
        _BlurSpread ("Blur Spread (texels)", Float) = 2.0
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float _BlurSpread;

        half4 SeparableBlur(float2 uv, float2 dir)
        {
            float4 o = dir.xyxy * float4(1, 1, -1, -1);
            half4 color = half4(0, 0, 0, 0);
            color += 0.40 * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel);
            color += 0.15 * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + o.xy, _BlitMipLevel);
            color += 0.15 * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + o.zw, _BlitMipLevel);
            color += 0.10 * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + o.xy * 2.0, _BlitMipLevel);
            color += 0.10 * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + o.zw * 2.0, _BlitMipLevel);
            color += 0.05 * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + o.xy * 3.0, _BlitMipLevel);
            color += 0.05 * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + o.zw * 3.0, _BlitMipLevel);
            return color;
        }
        ENDHLSL

        Pass
        {
            Name "FrostedGlassBlurHorizontal"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 dir = float2(_BlitTexture_TexelSize.x * _BlurSpread, 0.0);
                return SeparableBlur(input.texcoord.xy, dir);
            }
            ENDHLSL
        }

        Pass
        {
            Name "FrostedGlassBlurVertical"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 dir = float2(0.0, _BlitTexture_TexelSize.y * _BlurSpread);
                return SeparableBlur(input.texcoord.xy, dir);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
