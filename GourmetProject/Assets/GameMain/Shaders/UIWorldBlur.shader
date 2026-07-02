Shader "GourmetProject/UIWorldBlur"
{
    // Dual-Kawase 模糊，供 WorldBlurCapture 通过 Graphics.Blit 逐 pass 调用。
    // Pass 0 = 降采样，Pass 1 = 升采样。使用标准 _MainTex，不依赖 SRP Blit。
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "black" {}
        _BlurRadius ("Blur Radius", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _BlurRadius;

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        v2f vert(appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            return o;
        }
        ENDCG

        // Pass 0: Dual-Kawase 降采样
        Pass
        {
            Name "Downsample"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(v2f i) : SV_Target
            {
                float2 hp = _MainTex_TexelSize.xy * 0.5 * _BlurRadius;
                half4 sum = tex2D(_MainTex, i.uv) * 4.0;
                sum += tex2D(_MainTex, i.uv - hp);
                sum += tex2D(_MainTex, i.uv + hp);
                sum += tex2D(_MainTex, i.uv + float2(hp.x, -hp.y));
                sum += tex2D(_MainTex, i.uv - float2(hp.x, -hp.y));
                return sum * 0.125;
            }
            ENDCG
        }

        // Pass 1: Dual-Kawase 升采样
        Pass
        {
            Name "Upsample"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(v2f i) : SV_Target
            {
                float2 hp = _MainTex_TexelSize.xy * 0.5 * _BlurRadius;
                half4 sum = tex2D(_MainTex, i.uv + float2(-hp.x * 2.0, 0.0));
                sum += tex2D(_MainTex, i.uv + float2(-hp.x, hp.y)) * 2.0;
                sum += tex2D(_MainTex, i.uv + float2(0.0, hp.y * 2.0));
                sum += tex2D(_MainTex, i.uv + float2(hp.x, hp.y)) * 2.0;
                sum += tex2D(_MainTex, i.uv + float2(hp.x * 2.0, 0.0));
                sum += tex2D(_MainTex, i.uv + float2(hp.x, -hp.y)) * 2.0;
                sum += tex2D(_MainTex, i.uv + float2(0.0, -hp.y * 2.0));
                sum += tex2D(_MainTex, i.uv + float2(-hp.x, -hp.y)) * 2.0;
                return sum * (1.0 / 12.0);
            }
            ENDCG
        }
    }

    FallBack Off
}
