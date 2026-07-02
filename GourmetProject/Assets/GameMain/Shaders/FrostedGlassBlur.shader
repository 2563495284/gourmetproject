Shader "GourmetProject/FrostedGlassBlur"
{
    Properties
    {
        _FrostedBlurRadius ("Blur Radius", Float) = 1.0
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

        // _RTHandleScale is provided by the core shader library (Common.hlsl).

        float _FrostedBlurRadius;

        half4 SampleSource(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel);
        }

        // Dual Kawase downsample around a given base uv.
        half4 Downsample(float2 uv)
        {
            float2 hp = _BlitTexture_TexelSize.xy * 0.5 * _FrostedBlurRadius;
            half4 sum = SampleSource(uv) * 4.0;
            sum += SampleSource(uv - hp);
            sum += SampleSource(uv + hp);
            sum += SampleSource(uv + float2(hp.x, -hp.y));
            sum += SampleSource(uv - float2(hp.x, -hp.y));
            return sum * 0.125;
        }
        ENDHLSL

        // Pass 0: first downsample directly from the (RTHandle-scaled) camera color.
        Pass
        {
            Name "FrostedGlassDownsampleFirst"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsampleFirst

            half4 FragDownsampleFirst(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // _RTHandleScale maps [0,1] onto the valid region of the camera color RTHandle.
                float2 uv = input.texcoord.xy * _RTHandleScale.xy;
                return Downsample(uv);
            }
            ENDHLSL
        }

        // Pass 1: downsample from an exact-size intermediate texture.
        Pass
        {
            Name "FrostedGlassDownsample"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsample

            half4 FragDownsample(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return Downsample(input.texcoord.xy);
            }
            ENDHLSL
        }

        // Pass 2: dual Kawase upsample.
        Pass
        {
            Name "FrostedGlassUpsample"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUpsample

            half4 FragUpsample(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;
                float2 hp = _BlitTexture_TexelSize.xy * 0.5 * _FrostedBlurRadius;

                half4 sum = SampleSource(uv + float2(-hp.x * 2.0, 0.0));
                sum += SampleSource(uv + float2(-hp.x, hp.y)) * 2.0;
                sum += SampleSource(uv + float2(0.0, hp.y * 2.0));
                sum += SampleSource(uv + float2(hp.x, hp.y)) * 2.0;
                sum += SampleSource(uv + float2(hp.x * 2.0, 0.0));
                sum += SampleSource(uv + float2(hp.x, -hp.y)) * 2.0;
                sum += SampleSource(uv + float2(0.0, -hp.y * 2.0));
                sum += SampleSource(uv + float2(-hp.x, -hp.y)) * 2.0;
                return sum * (1.0 / 12.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
