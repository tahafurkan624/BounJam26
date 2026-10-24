// BlobShip - wave simulation for the 3D liquid UI (one ping-pong step). R = height now, G = height one step ago.
// Waves reflect off the rounded outline. Up to 4 impulses (uv.xy, radius, strength) per step. Run by LiquidSlab.cs.
Shader "Hidden/BlobShip/LiquidSim"
{
    Properties { _MainTex ("State", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _SimSize;        // xy = size in units, z = corner radius in units
            float4 _Imp[4];         // uv.x, uv.y, radius (units), strength
            float _Damp;

            float sdRound(float2 p, float2 b, float r) { float2 q = abs(p) - b + r; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r; }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 p = (uv - 0.5) * _SimSize.xy;
                float inside = step(sdRound(p, _SimSize.xy * 0.5, _SimSize.z), -0.005);
                float2 tx = _MainTex_TexelSize.xy;
                float4 s = tex2D(_MainTex, uv);
                float h = s.r, prev = s.g;
                float hl = tex2D(_MainTex, uv - float2(tx.x, 0)).r, hr = tex2D(_MainTex, uv + float2(tx.x, 0)).r;
                float hd = tex2D(_MainTex, uv - float2(0, tx.y)).r, hu = tex2D(_MainTex, uv + float2(0, tx.y)).r;
                float lap = hl + hr + hd + hu - 4.0 * h;
                float hn = h + (h - prev) * _Damp + 0.42 * lap;
                hn = lerp(hn, (hl + hr + hd + hu) * 0.25, 0.012);
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float2 d = (uv - _Imp[k].xy) * _SimSize.xy;
                    hn += _Imp[k].w * exp(-dot(d, d) / max(_Imp[k].z * _Imp[k].z, 1e-5));
                }
                return float4(hn * inside, h * inside, 0, 1);
            }
            ENDCG
        }
    }
}
