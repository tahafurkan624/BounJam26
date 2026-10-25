// BlobShip - 3D liquid / glass UI slab (URP).
// Thick rounded jelly: refracts what is behind (needs "Opaque Texture" on the URP asset), coloured by absorption,
// glows from inside, reflects a built-in studio (no reflection probe needed), ripples come from the wave simulation
// (LiquidSlab.cs). The same shader draws the blue glass parts (panel, switch end, drops) with _GlassAll = 1.
// Object space comes from LiquidSlabMesh: x right, y up, the front faces -z (toward the camera).
Shader "BlobShip/UI/Liquid Slab"
{
    Properties
    {
        [HDR] _Core ("Liquid core", Color) = (0.059, 0.71, 0.65, 1)
        [HDR] _Rim ("Liquid thin / edge", Color) = (0.54, 0.97, 0.89, 1)
        _Absorb ("Absorption (RGB)", Vector) = (2.6, 0.32, 0.55, 0)
        _GlowK ("Inner glow", Range(0, 2)) = 0.4
        _BackDim ("Scene brightness through the liquid", Range(0, 1)) = 0.2
        _BackSat ("Scene colour through the liquid", Range(0, 1)) = 0.6
        _GlassTint ("Glass tint", Color) = (0.86, 0.95, 1.0, 1)
        _GlassEdge ("Glass edge light", Color) = (0.4, 0.9, 1.0, 1)
        _Milk ("Glass milkiness", Color) = (0, 0, 0, 0)
        _GlassAll ("All glass", Range(0, 1)) = 0
        _LiqC ("Liquid centre (switch)", Range(0, 1)) = 0.5
        _LiqHalf ("Liquid half width (switch)", Float) = 2
        _RefrK ("Refraction", Range(0, 0.1)) = 0.022
        _Iri ("Iridescence", Range(0, 1)) = 0.3
        _FrostAmt ("Frost", Range(0, 1)) = 0
        _EnvK ("Reflection strength", Range(0, 3)) = 1
        _SolidMask ("Reacts to Solid mode", Range(0, 1)) = 1
        _Hover ("Hover", Range(0, 1)) = 0
        _Fade ("Fade", Range(0, 1)) = 1
        _Fill ("Pour in (panel)", Range(0, 1)) = 1
        _Drain ("Drain (panel)", Range(0, 1)) = 0
        _Seed ("Drip seed", Float) = 1.7
        _PanelMask ("Use pour / drain mask", Float) = 0
        [HideInInspector] _SimTex ("Simulation", 2D) = "black" {}
        [HideInInspector] _Size ("w, d, T, unit", Vector) = (450, 100, 40, 100)
        [HideInInspector] _InnerB ("cap half size + radius", Vector) = (183, 8, 8, 0)
        [HideInInspector] _SimParams ("gradK, amp, texel x, texel y", Vector) = (6, 3, 0.004, 0.006)
        [HideInInspector] _SpillR0 ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpillR1 ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpillR2 ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpillC0 ("", Color) = (0, 0, 0, 0)
        [HideInInspector] _SpillC1 ("", Color) = (0, 0, 0, 0)
        [HideInInspector] _SpillC2 ("", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "LiquidSlab"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_SimTex); SAMPLER(sampler_SimTex);
            float _BlobLiquidSolid;            // global, set by LiquidUI (0 liquid .. 1 frozen)

            CBUFFER_START(UnityPerMaterial)
                float4 _Core, _Rim, _Absorb, _GlassTint, _GlassEdge, _Milk;
                float _GlowK, _GlassAll, _LiqC, _LiqHalf, _RefrK, _Iri, _FrostAmt, _EnvK, _SolidMask, _Hover, _Fade;
                float _Fill, _Drain, _Seed, _PanelMask, _BackDim, _BackSat;
                float4 _Size, _InnerB, _SimParams;
                float4 _SpillR0, _SpillR1, _SpillR2, _SpillC0, _SpillC1, _SpillC2;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 posOS : TEXCOORD1;
                float3 nOS : TEXCOORD2;
                float3 posWS : TEXCOORD3;
                float cap : TEXCOORD4;
            };

            float sdRound(float2 p, float2 b, float r) { float2 q = abs(p) - b + r; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r; }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = v.positionOS.xyz;
                float3 n = v.normalOS;
                if (v.uv1.x > 0.5)
                {
                    // front cap: lifted by the wave height, calm at its border so it meets the rounded edge
                    float h = SAMPLE_TEXTURE2D_LOD(_SimTex, sampler_SimTex, v.uv, 0).r;
                    float e = smoothstep(0.0, 0.1 * _Size.w, -sdRound(p.xy, _InnerB.xy, _InnerB.z));
                    p.z = -_Size.z - 0.003 * _Size.w - h * _SimParams.y * e;
                    n = float3(0, 0, -1);
                }
                o.uv = v.uv;
                o.cap = v.uv1.x;
                o.posOS = p;
                o.nOS = n;
                o.posWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.posWS);
                return o;
            }

            // studio around the UI, in view space (+z = toward the viewer). Dark room; the lights sit where a calm
            // front face does not see them, so calm liquid stays deep and ripples / rounded edges break them into shards.
            float box(float2 ae, float4 r, float s)
            {
                float2 lo = smoothstep(r.xz - s, r.xz + s, ae);
                float2 hi = 1.0 - smoothstep(r.yw - s, r.yw + s, ae);
                return lo.x * lo.y * hi.x * hi.y;
            }
            float3 studio(float3 d, float soft)
            {
                d = normalize(d);
                float a = atan2(d.x, d.z);
                float e = asin(clamp(d.y, -1.0, 1.0));
                float2 ae = float2(a, e);
                float3 c = lerp(float3(0.004, 0.003, 0.009), float3(0.016, 0.014, 0.04), smoothstep(-0.4, 1.0, d.y));
                float bars = smoothstep(0.42, 0.5, abs(frac(a * 2.2) - 0.5)) * (1.0 - soft * 2.0);
                c += float3(2.2, 2.2, 2.7) * box(ae, float4(-0.85, 0.85, 0.66, 1.2), 0.04 + soft) * (1.0 - 0.75 * saturate(bars));
                c += float3(1.3, 1.45, 2.3) * box(ae, float4(-0.6, -0.14, 0.22, 0.46), 0.02 + soft);
                c += float3(1.9, 1.2, 2.2) * box(ae, float4(0.14, 0.6, 0.22, 0.46), 0.02 + soft);
                c += lerp(float3(0.25, 0.55, 3.2), float3(1.4, 0.6, 3.0), smoothstep(-0.3, 0.8, e)) * box(ae, float4(-1.7, -1.25, -0.35, 0.85), 0.05 + soft);
                c += lerp(float3(3.0, 1.5, 0.8), float3(2.6, 1.9, 1.6), smoothstep(-0.3, 0.8, e)) * box(ae, float4(1.25, 1.65, -0.3, 0.8), 0.05 + soft);   // warm peach accent
                c += float3(0.02, 0.12, 0.13) * smoothstep(-0.1, -0.7, d.y);
                return c;
            }

            float fingers(float u)
            {
                float a = sin(u * 17.0 + _Seed * 3.1) * 0.5 + 0.5, b = sin(u * 29.0 + _Seed * 7.7) * 0.5 + 0.5, c = sin(u * 7.0 + _Seed * 1.3) * 0.5 + 0.5;
                return pow(a, 6.0) * 0.7 + pow(b, 10.0) * 0.5 + c * 0.25;
            }

            float3 sceneAt(float2 uv) { return SampleSceneColor(saturate(uv)); }

            half4 frag(Varyings i) : SV_Target
            {
                float unit = _Size.w;
                bool isCap = i.cap > 0.5;
                if (isCap) clip(0.025 * unit - sdRound(i.posOS.xy, _InnerB.xy, _InnerB.z));

                // pour in from the top / drain away downward (same maths as UILiquidMorph and the panel transition)
                float men = 0.0;
                if (_PanelMask > 0.5)
                {
                    float u = saturate(i.uv.x), vT = 1.0 - i.uv.y, m = 1.0;
                    if (_Fill < 0.9995)
                    {
                        float mn = 1.0 - 0.12 * (1.0 - sin(u * 3.14159)) * (1.0 - _Fill);
                        float frontL = min(1.08, _Fill * 1.25 * mn * (1.0 + 0.30 * fingers(u) * (1.0 - _Fill)));
                        float spread = lerp(0.30, 1.04, smoothstep(0.0, 1.0, _Fill / 0.6));
                        m = min(frontL - vT, (spread - abs(u - 0.5) * 2.0) * 0.5);
                    }
                    if (_Drain > 0.0005)
                    {
                        float roll = sin(u * 6.0 + _Seed * 2.3) * 0.5 + 0.5;
                        float topL = clamp(_Drain * 1.12 - 0.14 * roll * sin(_Drain * 3.14159), 0.0, 1.02);
                        float spread = lerp(1.04, 0.32, smoothstep(0.45, 1.0, _Drain));
                        m = min(m, min(vT - topL, (spread - abs(u - 0.5) * 2.0) * 0.5));
                    }
                    clip(m);
                    men = smoothstep(0.03, 0.0, m) * step(0.0005, (1.0 - _Fill) + _Drain);
                }

                float solid = _BlobLiquidSolid * _SolidMask;
                float2 tx = _SimParams.zw;
                float h = SAMPLE_TEXTURE2D(_SimTex, sampler_SimTex, i.uv).r;
                float hl = SAMPLE_TEXTURE2D(_SimTex, sampler_SimTex, i.uv - float2(tx.x, 0)).r;
                float hr = SAMPLE_TEXTURE2D(_SimTex, sampler_SimTex, i.uv + float2(tx.x, 0)).r;
                float hd = SAMPLE_TEXTURE2D(_SimTex, sampler_SimTex, i.uv - float2(0, tx.y)).r;
                float hu = SAMPLE_TEXTURE2D(_SimTex, sampler_SimTex, i.uv + float2(0, tx.y)).r;
                float2 g = float2(hr - hl, hu - hd) * _SimParams.x;
                float lap = hl + hr + hd + hu - 4.0 * h;

                float3 n0 = normalize(i.nOS);
                float frontness = saturate(-n0.z);
                float3 nObj = normalize(n0 - float3(g, 0.0) * frontness);       // ripples run over the rounded edge too
                float3 N = normalize(TransformObjectToWorldNormal(nObj));
                float3 V = normalize(GetWorldSpaceViewDir(i.posWS));
                float ndv = saturate(dot(N, V));
                float3 Nv = normalize(TransformWorldToViewDir(N));
                float3 Vv = normalize(TransformWorldToViewDir(V));
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);

                float glass = max(_GlassAll, smoothstep(_LiqHalf - 0.012, _LiqHalf + 0.012, abs(i.uv.x - _LiqC) + h * 0.03));
                float th = 2.0 * (_Size.z / unit) * lerp(0.25, 1.0, ndv);          // path through the body, in "button heights"

                // what is behind, bent by the surface; a slight RGB split = the rainbow fringe of real glass
                float2 off = -Nv.xy * _RefrK * (0.6 + th);
                off.y *= _ProjectionParams.x;
                float3 bk;
                bk.r = sceneAt(suv + off).r;
                bk.g = sceneAt(suv + off * 1.12).g;
                bk.b = sceneAt(suv + off * 1.26).b;
                float frost = max(_FrostAmt, solid * (1.0 - glass) * 0.9);
                if (frost > 0.001)
                {
                    float3 bl = 0;
                    float rad = 0.012 + 0.02 * frost;
                    [unroll] for (int k = 0; k < 8; k++)
                    {
                        float an = k * 0.785398 + 0.4;
                        bl += sceneAt(suv + off * 1.2 + float2(cos(an), sin(an)) * rad * float2(_ScreenParams.y / _ScreenParams.x, 1.0) * (k % 2 == 0 ? 1.0 : 0.55));
                    }
                    bk = lerp(bk, bl / 8.0, frost);
                }

                // liquid: coloured by absorption + a soft inner glow, clearer in the middle, glowing at the edges
                float thin = pow(1.0 - ndv, 1.5);
                // the bright level shows through dimmed and a little greyed, like the game behind the mockup's menu,
                // so the liquid stays deep and the white LED labels and icons read on it; the scene is HDR, so it is
                // clamped first or a sunlit floor would stay white however much it is dimmed
                float3 bkC = saturate(bk);
                float3 bkL = lerp(dot(bkC, float3(0.3, 0.55, 0.15)).xxx, bkC, _BackSat) * _BackDim;
                float3 liq = bkL * exp(-_Absorb.xyz * th) * 1.7;
                float3 emi = lerp(_Core.rgb, _Rim.rgb, thin * 0.8) * _GlowK;
                emi *= (0.85 + 0.25 * smoothstep(0.3, -0.9, nObj.y)) * lerp(0.78, 1.0, thin);
                float caustic = max(-lap - 0.006, 0.0) * 14.0 * (1.0 - solid);
                emi += lerp(_Rim.rgb, float3(1.0, 0.9, 0.95), 0.4) * min(caustic, 1.0) * 0.55;
                liq += emi;
                liq = lerp(liq, liq * float3(0.8, 0.85, 1.0) + float3(0.07, 0.09, 0.12), solid * 0.6);     // frozen: cooler, frosted
                float3 gls = bk * _GlassTint.rgb + _Milk.rgb + _GlassEdge.rgb * thin * 0.3;
                float3 col = lerp(liq, gls, glass);

                // reflections of the studio (view space), with a faint thin-film colour shift
                float3 Rv = reflect(-Vv, Nv);
                float3 rc = studio(Rv, frost * 0.25) * _EnvK;
                float3 film = 0.5 + 0.5 * cos(6.2832 * (float3(0.0, 0.33, 0.67) + ndv * 1.2 + h * 1.5 + 0.1));
                rc *= lerp(float3(1, 1, 1), film * 1.35, _Iri);
                float fr = 0.04 + 0.96 * pow(1.0 - ndv, 5.0);
                col += rc * (fr + 0.03);
                col += float3(0.75, 0.88, 1.15) * men * 0.7;
                col += _Rim.rgb * _Hover * 0.05;

                // coloured light from the buttons spilling onto the glass under them (panel only)
                if (isCap && _PanelMask > 0.5)
                {
                    col += _SpillC0.rgb * exp(-max(sdRound(i.posOS.xy - _SpillR0.xy, _SpillR0.zw, _SpillR0.w), 0.0) / (0.32 * unit)) * _SpillC0.a;
                    col += _SpillC1.rgb * exp(-max(sdRound(i.posOS.xy - _SpillR1.xy, _SpillR1.zw, _SpillR1.w), 0.0) / (0.32 * unit)) * _SpillC1.a;
                    col += _SpillC2.rgb * exp(-max(sdRound(i.posOS.xy - _SpillR2.xy, _SpillR2.zw, _SpillR2.w), 0.0) / (0.32 * unit)) * _SpillC2.a;
                }
                return half4(col, _Fade);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
