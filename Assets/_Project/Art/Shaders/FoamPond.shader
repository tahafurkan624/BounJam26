// Calm pond water with light, realistic foam for Water volumes (a scaled cube), made so the blob never gets lost in it.
// - The surface stays flat: no vertex waves. Small ripples only tilt the normal a little, for moving glints.
// - Foam is sparse and thin: loose patches of soft froth lace (a sheet with round holes, fraying at the edges) and a
//   few tiny bubbles, slowly drifting and warping like the froth on a real pond (Voronoi cells, warped by noise).
// - The water is lagoon green, not the blob's aqua or the crystal's blue, and turns clear where it is shallow over
//   something (camera depth texture), so a crystal floating half under still shows.
// - Around each blob piece (positions from PondFocus) the foam clears, the water gets more see-through and a thin
//   froth ring traces the swimmer.
// - A thin foam line hugs walls, floors and anything standing in the water.
// Drawn after the jelly (which writes depth), like BounJam/Water, so the part of the blob above water stays in front.
Shader "BounJam/Foam Pond"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow Colour", Color) = (0.36, 0.82, 0.62, 1)
        _DeepColor ("Deep Colour", Color) = (0.04, 0.36, 0.36, 1)
        _ShallowOpacity ("Opacity (shallow)", Range(0, 1)) = 0.3
        _DeepOpacity ("Opacity (deep)", Range(0, 1)) = 0.9
        _Murkiness ("Murkiness (per metre of water)", Range(0.1, 5)) = 1.8
        _Reflection ("Sky Reflection", Range(0, 1)) = 0.3

        [Header(Small Ripples)]
        _RippleSize ("Ripple Size (m)", Range(0.05, 2)) = 0.35
        _RippleStrength ("Ripple Strength (normal only)", Range(0, 0.3)) = 0.05
        _RippleSpeed ("Ripple Speed", Range(0, 3)) = 0.6
        _Shininess ("Glint Sharpness", Range(8, 1024)) = 300
        _Specular ("Glint Strength", Range(0, 4)) = 0.8

        [Header(Foam)]
        _FoamColor ("Foam Colour", Color) = (0.95, 0.99, 0.97, 1)
        _FoamOpacity ("Foam Opacity", Range(0, 1)) = 0.7
        _LaceSize ("Foam Lace Size (m)", Range(0.1, 3)) = 0.4
        _LaceWidth ("Foam Line Width", Range(0.01, 0.2)) = 0.05
        _PatchSize ("Foam Patch Size (m)", Range(0.5, 10)) = 2.2
        _Coverage ("Foam Coverage", Range(0, 1)) = 0.3
        _Drift ("Drift Speed (m per s)", Range(0, 0.3)) = 0.05
        _EdgeFoam ("Edge Foam Width (m)", Range(0, 0.5)) = 0.1

        [Header(Swimmer)]
        _ClearRadius ("Clear Radius Around The Blob (x its size)", Range(0, 3)) = 1.2
        _RingStrength ("Froth Ring Strength", Range(0, 1)) = 0.55
        _RingWidth ("Froth Ring Width (m)", Range(0.01, 0.3)) = 0.05
        _SeeThrough ("See-Through Near The Blob", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardFoamPond"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half _ShallowOpacity;
                half _DeepOpacity;
                float _Murkiness;
                half _Reflection;
                float _RippleSize;
                half _RippleStrength;
                float _RippleSpeed;
                half _Shininess;
                half _Specular;
                half4 _FoamColor;
                half _FoamOpacity;
                float _LaceSize;
                float _LaceWidth;
                float _PatchSize;
                half _Coverage;
                float _Drift;
                float _EdgeFoam;
                float _ClearRadius;
                half _RingStrength;
                float _RingWidth;
                half _SeeThrough;
            CBUFFER_END

            // Set every frame by PondFocus: xyz = a blob piece's centre, w = its radius.
            float4 _PondFocus[4];
            float _PondFocusCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                Varyings output;
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.eyeDepth = -position.positionVS.z;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + 1.0);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                return (0.5 * ValueNoise(p) + 0.25 * ValueNoise(p * 2.03 + 7.1) + 0.125 * ValueNoise(p * 4.01 + 3.7)) / 0.875;
            }

            // Distance to the nearest and second-nearest cell point; the points wobble slowly in their cells.
            void Voronoi(float2 p, float t, out float f1, out float f2, out float id)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f1 = 8.0;
                f2 = 8.0;
                id = 0.0;
                UNITY_UNROLL
                for (int y = -1; y <= 1; y++)
                {
                    UNITY_UNROLL
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 g = float2(x, y);
                        float h = Hash21(cell + g);
                        float2 point2 = 0.5 + 0.32 * sin(t * 0.6 + 6.2831 * float2(h, Hash21(cell + g + 17.13)));
                        float d = length(g + point2 - f);
                        if (d < f1)
                        {
                            f2 = f1;
                            f1 = d;
                            id = h;
                        }
                        else if (d < f2)
                        {
                            f2 = d;
                        }
                    }
                }
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // 1 on the top face, 0 on the sides: only the surface ripples and foams.
                half top = saturate(normalWS.y * 2.0 - 1.0);
                float t = _Time.y;
                float2 pw = input.positionWS.xz;
                float2 drift = float2(0.8, 0.6) * _Drift * t;

                // Small ripples: they only tilt the normal, the surface itself never moves.
                float2 rp = pw / max(_RippleSize, 0.01);
                float rt = t * _RippleSpeed;
                float2 slope = float2(cos(rp.x * 1.3 + rp.y * 0.4 + rt * 1.1), cos(rp.y * 1.2 - rp.x * 0.3 - rt * 0.9)) * 0.5
                             + float2(0.6, 0.8) * cos(dot(rp, float2(0.6, 0.8)) * 2.7 - rt * 1.6) * 0.3;
                normalWS = normalize(normalWS + float3(slope.x, 0.0, slope.y) * (_RippleStrength * top));

                // How much water the view crosses before reaching what's behind it.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float behind = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float thickness = max(0.0, behind - input.eyeDepth);
                half deep = 1.0 - exp(-thickness * _Murkiness);

                // Loose foam patches, few and drifting.
                float patch = Fbm((pw + drift * 0.5) / _PatchSize + float2(t * 0.01, 0.0));
                patch = smoothstep(1.0 - _Coverage - 0.25, 1.0 - _Coverage + 0.25, patch);

                // Inside them, a soft lace: a thin froth sheet full of round holes (one per cell, bigger towards the
                // patch edge so it frays out), warped by noise, with finer holes in it and a few tiny bubbles.
                float2 warp = float2(Fbm(pw * 0.35 + t * 0.02), Fbm(pw * 0.35 + 5.2 - t * 0.02)) - 0.5;
                float2 q = (pw + drift + warp * 1.2) / _LaceSize;
                float f1, f2, id;
                Voronoi(q, t * 0.5, f1, f2, id);
                float hole = lerp(0.66, 0.4, patch);
                float lace = smoothstep(hole, hole + _LaceWidth * 2.0, f1);
                float breakup = smoothstep(0.25, 0.65, ValueNoise(q * 1.7 + t * 0.05));
                float g1, g2, gid;
                Voronoi(q * 2.7 + 3.1, t * 0.7, g1, g2, gid);
                float fine = smoothstep(0.36, 0.48, g1);
                float specks = (1.0 - smoothstep(0.05, 0.09, g1)) * step(0.8, gid);
                float foam = patch * saturate(lace * lerp(0.5, 1.0, fine) * lerp(0.6, 1.0, breakup) + specks * 0.7) * top;

                // A thin foam line where the water meets walls, floors and floating things.
                float edge = saturate(1.0 - thickness / max(_EdgeFoam, 1e-4));
                float edgeFoam = saturate(edge * edge * (0.6 + 0.4 * breakup)) * top;

                // Clear water and a thin froth ring around each blob piece near the surface.
                float clear = 1.0;
                float ring = 0.0;
                UNITY_UNROLL
                for (int i = 0; i < 4; i++)
                {
                    if (i < (int)_PondFocusCount)
                    {
                        float4 focus = _PondFocus[i];
                        float d = length(pw - focus.xz);
                        float r = focus.w * _ClearRadius;
                        float near = saturate(1.0 - abs(focus.y - input.positionWS.y) / 1.5);
                        clear = min(clear, lerp(1.0, smoothstep(r * 0.75, r, d), near));
                        float band = (d - r) / max(_RingWidth, 1e-3);
                        ring = max(ring, near * exp(-band * band));
                    }
                }
                foam *= clear;
                ring *= top * _RingStrength * (0.65 + 0.35 * breakup);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, 0.6h);
                half3 ambient = SampleSH(normalWS);
                half3 light = ambient + mainLight.color * saturate(dot(normalWS, mainLight.direction)) * shadow;

                half3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep) * light;
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 4.0);
                color += GlossyEnvironmentReflection(reflect(-viewDir, normalWS), input.positionWS, 0.05h, 1.0h) * fresnel * _Reflection;
                half3 halfDir = normalize(mainLight.direction + viewDir);
                half glint = pow(saturate(dot(normalWS, halfDir)), _Shininess) * _Specular * shadow * top * clear;
                color += mainLight.color * glint;
                half alpha = lerp(_ShallowOpacity, _DeepOpacity, deep) * lerp(1.0 - _SeeThrough, 1.0, clear);
                alpha = saturate(alpha + fresnel * 0.2 + glint);

                half froth = saturate(max(max(foam, edgeFoam), ring) * _FoamOpacity);
                half3 foamLit = _FoamColor.rgb * (ambient + mainLight.color * shadow * 0.8);
                color = lerp(color, foamLit, froth);
                alpha = max(alpha, froth);

                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
