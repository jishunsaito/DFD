Shader "Hidden/DepthEstimator/Grayscale"
{
    Properties
    {
        _MainTex ("Depth", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                uv.x = 1.0 - uv.x;
                uv.y = 1.0 - uv.y;
                float d = tex2D(_MainTex, uv).r;
                return float4(d, d, d, 1);
            }
            ENDCG
        }
    }
}
