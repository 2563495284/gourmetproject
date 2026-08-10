Shader "Hidden/GourmetProject/BattleDoodleCanvas"
{
    Properties
    {
        _MainTex ("Canvas", 2D) = "black" {}
        _BrushColor ("Brush Color", Color) = (0.15, 0.1, 0.08, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 _StrokeStart;
            float2 _StrokeEnd;
            float2 _CanvasSize;
            float4 _BrushColor;
            float _BrushRadius;
            float _Erase;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 pixelPosition = input.uv * _CanvasSize;
                float2 start = _StrokeStart * _CanvasSize;
                float2 end = _StrokeEnd * _CanvasSize;
                float2 segment = end - start;
                float segmentLengthSquared = max(dot(segment, segment), 0.0001);
                float projection = saturate(dot(pixelPosition - start, segment) / segmentLengthSquared);
                float distanceToSegment = length(pixelPosition - (start + segment * projection));
                float coverage = 1.0 - smoothstep(_BrushRadius - 0.75, _BrushRadius + 0.75, distanceToSegment);

                float4 canvas = tex2D(_MainTex, input.uv);
                float mask = saturate(coverage * _BrushColor.a);

                if (_Erase > 0.5)
                {
                    canvas.a *= 1.0 - mask;
                    canvas.rgb *= step(0.0001, canvas.a);
                    return canvas;
                }

                // RenderTexture 保存直通 Alpha，供 Unity UI/Default 正常进行 SrcAlpha 合成。
                float outputAlpha = mask + canvas.a * (1.0 - mask);
                float3 premultiplied = _BrushColor.rgb * mask + canvas.rgb * canvas.a * (1.0 - mask);
                float3 outputColor = premultiplied / max(outputAlpha, 0.0001);
                return float4(outputColor, outputAlpha);
            }
            ENDCG
        }
    }
}
