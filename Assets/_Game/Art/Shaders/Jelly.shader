// Clear water for the liquid blob, painted glacier ice when it hardens. One pass, no raymarching:
// - refraction: the body is treated as an ellipsoid fitted to the skin's renderer bounds (the CPU skin recalculates
//   them every frame). Each pixel follows its refracted ray to where it leaves the water and shows the scene behind
//   that point (URP opaque texture), so the floor is bent like through a real drop, with a little colour fringing
// - Beer-Lambert absorption over the distance the view travels through the water: that ellipsoid, cut short by the
//   depth texture where the floor or the ship's hull is closer. Thin edges stay clear, the thick middle turns cyan,
//   and light scattered inside fills in a little of what was absorbed
// - slow ripples from 3D noise in the skin's own space tilt the normal: they show in the sun glints, the sky
//   reflection (Schlick fresnel), the refracted floor and a faint caustic web. A few millimetres of vertex swell
// - foam where the water meets the floor or the ship's hull, and drip rings while _Splash is up (set from code)
// - hardened (_Solid, set from code): a stylized glacier instead. Deep blue where the view crosses the most ice, pale
//   and glowing at the thin edges, facets lit in soft painted bands with blue-violet shade, and white cracks and
//   trapped bubbles on two shells inside that turn with the crystal
// Renders in the transparent queue (after the opaque texture is captured) but writes depth and outputs an opaque
// colour, so the ship can sit half inside it and still sort correctly. Its own shadow caster (same material
// constants as the forward pass, so it stays SRP Batcher compatible) drops a share of the shadow-map texels: the
// soft-shadow filter turns that into a partial shadow, because light gets through water.
Shader "BounJam/Jelly"
{
    Properties
    {
        [Header(Water)]
        _WaterTint ("Tint Through 1 m Of Water", Color) = (0.44, 0.81, 0.85, 1)
        _Density ("Density (absorption multiplier)", Range(0, 3)) = 1.15
        _ScatterColor ("Scattered Light (murk)", Color) = (0.18, 0.53, 0.59, 1)
        _SurfaceOpacity ("Body Even When Thin", Range(0, 0.5)) = 0.18
        _ShadowStrength ("Shadow Strength On The Water", Range(0, 1)) = 0.35

        [Header(Refraction)]
        _IOR ("Index Of Refraction", Range(1, 1.6)) = 1.33
        _Refraction ("Refraction (1 = physical)", Range(0, 1.5)) = 0.6
        _RippleRefraction ("Ripple Bending (x ripple slope)", Range(0, 6)) = 1.5
        _Dispersion ("Colour Fringing", Range(0, 0.3)) = 0.06

        [Header(Ripples)]
        _RippleSize ("Ripple Size (m)", Range(0.05, 0.6)) = 0.2
        _RippleSlope ("Ripple Slope", Range(0, 0.4)) = 0.12
        _RippleSpeed ("Ripple Drift (m per s)", Range(0, 0.5)) = 0.06
        _RippleHeight ("Ripple Swell (m, vertex)", Range(0, 0.012)) = 0.004

        [Header(Surface)]
        _Reflectance ("Reflectance Facing The Camera (water 0.02)", Range(0, 0.2)) = 0.02
        _ReflectionTint ("Sky Reflection Tint", Color) = (1, 1, 1, 1)
        _RimColor ("Rim Colour", Color) = (0.85, 0.97, 1, 1)
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.65
        [PowerSlider(3)] _GlintSharpness ("Sun Glint Sharpness", Range(50, 3000)) = 400
        _GlintStrength ("Sun Glint Strength", Range(0, 8)) = 2.5
        _Sheen ("Soft Highlight", Range(0, 1)) = 0.15

        [Header(Foam And Light)]
        _FoamColor ("Foam Colour", Color) = (0.93, 0.98, 1, 1)
        _FoamWidth ("Contact Foam Width (m)", Range(0, 0.15)) = 0.035
        _FoamStrength ("Contact Foam Strength", Range(0, 1)) = 0.55
        _Caustics ("Caustic Shimmer", Range(0, 1)) = 0.25
        _ShadowOpacity ("Cast Shadow Opacity", Range(0, 1)) = 0.35

        [Header(Splash)]
        _Splash ("Splash (0 calm, 1 sloshing, set from code)", Range(0, 1)) = 0
        _DripStrength ("Drip Ring Strength", Range(0, 1)) = 0.6
        _DripSpacing ("Drip Ring Spacing (m)", Range(0.08, 0.5)) = 0.22

        [Header(Solid Form)]
        _Solid ("Solid (0 water, 1 ice, set from code)", Range(0, 1)) = 0
        _IceEdgeColor ("Ice Where Thin (edges)", Color) = (0.78, 0.95, 1, 1)
        _IceMidColor ("Ice Midtone", Color) = (0.12, 0.56, 0.84, 1)
        _IceDeepColor ("Ice Where Thick (core)", Color) = (0.02, 0.2, 0.48, 1)
        _IcePaint ("Brushy Wobble In The Gradient", Range(0, 0.4)) = 0.12
        _IceShadeColor ("Sunlight Left On Shaded Facets", Color) = (0.42, 0.45, 0.80, 1)
        _IceBanding ("Painted Light Bands", Range(0, 1)) = 0.7
        _IceTopWash ("White Wash On Sunlit Tops", Range(0, 1)) = 0.06
        _IceRimColor ("Glowing Edge Colour", Color) = (0.90, 0.99, 1, 1)
        _IceRimStrength ("Glowing Edge Strength", Range(0, 2)) = 0.9
        _IceRimPower ("Glowing Edge Tightness", Range(1, 6)) = 2.5
        _IceClarity ("See-Through At The Edges", Range(0, 1)) = 0.2
        _IceRefraction ("Ice Refraction (share of the water's)", Range(0, 1)) = 0.45
        _IceShadowOpacity ("Ice Cast Shadow Opacity", Range(0, 1)) = 0.9

        [Header(Solid Form Inside)]
        _IceCrackColor ("Crack Colour", Color) = (0.92, 0.98, 1, 1)
        _IceCracks ("Share Of Cell Borders That Crack", Range(0, 1)) = 0.45
        _IceCrackSize ("Crack Spacing (m)", Range(0.1, 1)) = 0.3
        _IceCrackWidth ("Crack Width (m)", Range(0.002, 0.04)) = 0.012
        _IceBubbles ("Bubbles (share of cells)", Range(0, 1)) = 0.3
        _IceBubbleSize ("Bubble Spacing (m)", Range(0.04, 0.3)) = 0.12
        _IceLayerDepth ("Depth Of The Near Layer (m)", Range(0.01, 0.3)) = 0.07
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        // Shared by both passes: one material layout, which is what the SRP Batcher needs.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _WaterTint;
            half4 _ScatterColor;
            half4 _ReflectionTint;
            half4 _RimColor;
            half4 _FoamColor;
            half4 _IceEdgeColor;
            half4 _IceMidColor;
            half4 _IceDeepColor;
            half4 _IceShadeColor;
            half4 _IceRimColor;
            half4 _IceCrackColor;
            half _Density;
            half _SurfaceOpacity;
            half _ShadowStrength;
            float _IOR;
            float _Refraction;
            float _RippleRefraction;
            float _Dispersion;
            float _RippleSize;
            float _RippleSlope;
            float _RippleSpeed;
            float _RippleHeight;
            half _Reflectance;
            half _RimStrength;
            float _GlintSharpness;
            half _GlintStrength;
            half _Sheen;
            float _FoamWidth;
            half _FoamStrength;
            half _Caustics;
            half _ShadowOpacity;
            float _Splash;
            half _DripStrength;
            float _DripSpacing;
            float _Solid;
            half _IcePaint;
            half _IceBanding;
            half _IceTopWash;
            half _IceRimStrength;
            half _IceRimPower;
            half _IceClarity;
            half _IceRefraction;
            half _IceShadowOpacity;
            half _IceCracks;
            float _IceCrackSize;
            float _IceCrackWidth;
            half _IceBubbles;
            float _IceBubbleSize;
            float _IceLayerDepth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardJelly"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // Forward+ leaves the per-object light data at zero; without this keyword the sun never reaches the blob.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

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
                // Undisplaced skin-space position: where the ripples are looked up.
                float3 positionOS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            static const float3 kCorners[8] =
            {
                float3(0, 0, 0), float3(1, 0, 0), float3(0, 1, 0), float3(1, 1, 0),
                float3(0, 0, 1), float3(1, 0, 1), float3(0, 1, 1), float3(1, 1, 1)
            };

            // Three pseudo-random values in [0, 1) per lattice point. Sine-free, so it's the same on every GPU.
            float3 RippleHash(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yzx + 33.33);
                return frac((p.xxy + p.yzz) * p.zyx);
            }

            // Gradient noise with its exact derivative, so ripples can tilt the normal without a second lookup.
            // xyz = gradient, w = value (roughly -1..1).
            float4 RippleNoise(float3 p)
            {
                float3 cell = floor(p);
                float3 f = p - cell;
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float3 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);
                float4 result = 0;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float3 corner = kCorners[i];
                    float3 gradient = RippleHash(cell + corner) * 2.0 - 1.0;
                    float value = dot(gradient, f - corner);
                    float3 w = lerp(1.0 - u, u, corner);
                    float3 dw = lerp(-du, du, corner);
                    float weight = w.x * w.y * w.z;
                    result.xyz += weight * gradient + value * float3(dw.x * w.y * w.z, w.x * dw.y * w.z, w.x * w.y * dw.z);
                    result.w += weight * value;
                }
                return result;
            }

            // Slow ripples in the skin's own space: xyz = slope, w = height. Object space moves with the blob, so the
            // ripples ride along instead of streaming over it at 5 m/s (that strobes at the default camera distance),
            // and when the skin squashes or jiggles it sweeps through the field: it shimmers more exactly when the
            // water sloshes. The coarse layer drains down the sides like a film of water, the fine one wanders across.
            // thread = a faint caustic web where the coarse height crosses zero.
            float4 Ripples(float3 positionOS, float footprint, float chop, out float thread)
            {
                float frequency = 1.0 / _RippleSize;
                float drift = _Time.y * _RippleSpeed * frequency;
                float4 coarse = RippleNoise(positionOS * frequency + float3(0.0, drift, 0.0));
                float4 fine = RippleNoise(positionOS * (frequency * 2.17) + float3(-0.7, 0.35, 0.9) * (drift * 1.6) + 11.3);

                // Ripples smaller than a few pixels only fizz and crawl (far away, along the grazing edge): fade them out.
                float cellsPerPixel = footprint * frequency;
                float coarseFade = saturate((0.45 - cellsPerPixel) * 5.0);
                float fineFade = saturate((0.45 - cellsPerPixel * 2.17) * 5.0);

                float threadWidth = 0.12 + fwidth(coarse.w) * 1.5;
                thread = saturate(1.0 - abs(coarse.w) / threadWidth);
                thread *= thread * (0.12 / threadWidth) * coarseFade;

                return coarse * coarseFade + fine * (fineFade * chop);
            }

            // Rings from drops falling back onto the surface after a sharp move. Every cell of a 3D grid drips once per
            // cycle, at its own moment and a fresh spot each time; the harder the splash, the more cells take part.
            // A ring spreads to under half a cell, so the 2x2x2 nearest cells always hold every ring that reaches here.
            // xyz = slope, w = crest height (for a little white aeration).
            float4 DripRings(float3 positionOS, float splash, float footprint)
            {
                float3 p = positionOS / _DripSpacing;
                float3 baseCell = floor(p - 0.5);
                float4 result = 0;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float3 cell = baseCell + kCorners[i];
                    float3 timing = RippleHash(cell + 41.7);
                    float cycle = _Time.y * (1.2 + 0.6 * timing.x) + timing.y;
                    float cycleIndex = floor(cycle);
                    float age = cycle - cycleIndex;
                    // Wrapped so the hash inputs stay small however long the game runs.
                    float variant = cycleIndex - 512.0 * floor(cycleIndex / 512.0);
                    float pick = frac(timing.z + variant * 0.618034);
                    float alive = saturate((splash - pick) * 6.0);
                    float3 drop = cell + 0.25 + 0.5 * RippleHash(cell + variant * 7.31);

                    float3 offset = p - drop;
                    float r = length(offset) + 1e-4;
                    // A short wave packet (crest, trough, crest) that grows and dies out.
                    float x = (r - age * 0.45) * 8.0;
                    float envelope = exp(-x * x) * alive * saturate(age * 12.0) * (1.0 - age) * (1.0 - age);
                    float phase = x * 2.4;
                    float slope = (2.4 * cos(phase) - 2.0 * x * sin(phase)) * 8.0 * envelope;
                    result.xyz += slope * offset / r;
                    result.w = max(result.w, sin(phase) * envelope);
                }
                // Same pixel-size fade as the ripples.
                return result * saturate((0.2 - footprint / _DripSpacing) * 10.0);
            }

            // Fractures inside the ice: the borders of a jittered 3D cell grid, kept between only some pairs of
            // neighbouring cells so they come as broken sheets rather than a full honeycomb, and warped a little by
            // the gradient noise so they run jagged. p and pixel (one pixel's footprint) are in cells; returns 0..1.
            half IceCracks(float3 p, float pixel)
            {
                p += RippleNoise(p * 1.9 + 3.7).xyz * 0.06;
                float3 cell = floor(p);
                float nearest = 8.0;
                float second = 8.0;
                float3 nearestCell = 0;
                float3 secondCell = 0;
                [unroll]
                for (int i = 0; i < 27; i++)
                {
                    float3 c = cell + float3(i % 3, (i / 3) % 3, i / 9) - 1.0;
                    float d = distance(p, c + 0.1 + 0.8 * RippleHash(c + 17.0));
                    if (d < nearest) { second = nearest; secondCell = nearestCell; nearest = d; nearestCell = c; }
                    else if (d < second) { second = d; secondCell = c; }
                }
                // second - nearest is about twice the distance to the border between the two nearest cells, so a
                // crack of full width w ends where it reaches w (in cells).
                float border = second - nearest;
                float cracked = step(RippleHash(nearestCell + secondCell + 61.3).x, _IceCracks);
                float width = _IceCrackWidth / _IceCrackSize;
                // Thinner than a pixel: fade out instead of fizzing.
                return cracked * (1.0 - smoothstep(width, width + 2.0 * pixel, border)) * saturate(width * 2.0 / pixel);
            }

            // Trapped air: in some cells of a finer grid, a small sphere. Where the sample shell cuts one it shows as a
            // thin bright ring around a faint pale fill. A bubble stays inside its cell's middle, so the 2x2x2 nearest
            // cells always hold every bubble that reaches here. p and pixel are in cells; returns 0..1.
            half IceBubbles(float3 p, float pixel)
            {
                float3 baseCell = floor(p - 0.5);
                half result = 0;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float3 c = baseCell + kCorners[i];
                    float3 h = RippleHash(c + 91.7);
                    float radius = 0.08 + 0.12 * h.x;
                    float d = distance(p, c + 0.3 + 0.4 * RippleHash(c + 23.9));
                    float ring = 1.0 - smoothstep(0.025, 0.025 + pixel, abs(d - radius));
                    float fill = (1.0 - smoothstep(radius - pixel, radius, d)) * 0.3;
                    result = max(result, step(h.z, _IceBubbles) * max(ring, fill));
                }
                // Bubbles only a couple of pixels across just sparkle: fade them out.
                return result * saturate((0.12 - pixel) * 15.0);
            }

            // Where a ray starting on (or near) the ellipsoid leaves it, as a distance along the ray.
            float EllipsoidExit(float3 origin, float3 direction, float3 center, float3 radii)
            {
                float3 o = (origin - center) / radii;
                float3 d = direction / radii;
                float a = max(dot(d, d), 1e-6);
                float b = dot(o, d);
                float c = dot(o, o) - 1.0;
                return max((sqrt(max(b * b - a * c, 0.0)) - b) / a, 0.0);
            }

            // Same convention as GetNormalizedScreenSpaceUV, so offsets between the two line up.
            float2 WorldToScreenUV(float3 positionWS)
            {
                float4 positionCS = TransformWorldToHClip(positionWS);
                float2 ndc = positionCS.xy / max(positionCS.w, 1e-5);
                return float2(ndc.x, ndc.y * _ProjectionParams.x) * 0.5 + 0.5;
            }

            // Eye depth of the opaque scene at a screen position (the scene view may be orthographic).
            float SceneEyeDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                return unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(raw) : LinearEyeDepth(raw, _ZBufferParams);
            }

            // The sky cubemap URP binds for every camera. Sampled directly: with Forward+ and probe blending the
            // per-object unity_SpecCube0 isn't set up, and the levels have no reflection probes anyway.
            half3 SkyReflection(half3 direction, half perceptualRoughness)
            {
                half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
                half4 encoded = SAMPLE_TEXTURECUBE_LOD(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap, direction, mip);
                return DecodeHDREnvironment(encoded, _GlossyEnvironmentCubeMap_HDR);
            }

            Varyings Vert(Attributes input)
            {
                float3 positionOS = input.positionOS.xyz;
                // A few millimetres of swell from the coarse ripple layer, outward only so the surface never dips
                // under its own shadow caster, and gone early in the harden. It pushes away from the skin's origin
                // (the core, always inside the body) instead of along the vertex normal: the faceted ice mesh swaps in
                // while _Solid is still 0, and its split vertices share a position but not a normal, so only a push
                // that depends on the position alone keeps the crystal from cracking open along its facet edges.
                float frequency = 1.0 / _RippleSize;
                float swell = RippleNoise(positionOS * frequency + float3(0.0, _Time.y * _RippleSpeed * frequency, 0.0)).w;
                float swellHeight = _RippleHeight * (1.0 + 1.5 * _Splash) * saturate(1.0 - _Solid * 4.0);
                float3 outward = positionOS * rsqrt(max(dot(positionOS, positionOS), 1e-6));
                float3 displacedOS = positionOS + outward * (swellHeight * saturate(swell + 0.5));

                VertexPositionInputs position = GetVertexPositionInputs(displacedOS);
                Varyings output;
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = positionOS;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float solid = _Solid;
                float liquid = 1.0 - solid;
                float splash = _Splash * liquid;

                float3 positionWS = input.positionWS;
                float3 geometricNormal = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(positionWS);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // Ripples (and drip rings while splashing) tilt the normal by the surface gradient of their height.
                // Ice has none.
                float footprint = length(fwidth(input.positionOS));
                float thread;
                float4 ripples = Ripples(input.positionOS, footprint, 0.55 + 0.6 * splash, thread);
                float3 slopeOS = ripples.xyz * (_RippleSlope * (1.0 + 0.6 * splash));
                float dripCrest = 0.0;
                UNITY_BRANCH
                if (splash > 0.001)
                {
                    float4 rings = DripRings(input.positionOS, splash, footprint);
                    slopeOS += rings.xyz * (_DripStrength * 0.02);
                    dripCrest = rings.w * _DripStrength;
                }
                float3 slopeWS = TransformObjectToWorldDir(slopeOS, false) * liquid;
                float3 tangentSlope = slopeWS - geometricNormal * dot(geometricNormal, slopeWS);
                float3 normalWS = normalize(geometricNormal - tangentSlope);

                // The body as an ellipsoid fitted to the renderer bounds. Without bounds, the sphere that fits the
                // surface here.
                float3 boundsMin = unity_RendererBounds_Min.xyz;
                float3 boundsMax = unity_RendererBounds_Max.xyz;
                float hasBounds = all(boundsMax > boundsMin) ? 1.0 : 0.0;
                float3 bodyCenter = lerp(positionWS - geometricNormal * 0.45, (boundsMin + boundsMax) * 0.5, hasBounds);
                float3 bodyRadii = lerp(float3(0.45, 0.45, 0.45), max((boundsMax - boundsMin) * 0.5, 0.05), hasBounds);

                // How much water the view crosses: from this surface to where the ray leaves the body, cut short by
                // whatever opaque is closer (the floor under it, the ship's hull inside it). The depth texture only
                // holds opaques, so over the void it reads the far plane; the ellipsoid keeps that finite.
                float3 rayDir = -viewDir;
                float bodyChord = EllipsoidExit(positionWS, rayDir, bodyCenter, bodyRadii);
                float surfaceEye = -TransformWorldToView(positionWS).z;
                float rayPerEye = unity_OrthoParams.w > 0.5 ? 1.0 : distance(positionWS, GetCameraPositionWS()) / max(surfaceEye, 1e-4);
                float behindDistance = max(SceneEyeDepth(screenUV) - surfaceEye, 0.0) * rayPerEye;
                float waterDepth = min(bodyChord, behindDistance);

                // Refraction: follow the refracted ray to where it leaves the water and look at the scene there. That
                // point is on the body, so the sample always lands inside the blob's own outline. Ripples bend it a
                // little more than they tilt the shading, so they read in the floor at the default camera distance.
                float3 refractNormal = normalize(geometricNormal - tangentSlope * _RippleRefraction);
                float3 refracted = refract(rayDir, refractNormal, 1.0 / lerp(_IOR, 1.31, solid));
                float exitDistance = EllipsoidExit(positionWS, refracted, bodyCenter, bodyRadii);
                float3 exitWS = positionWS + refracted * exitDistance;
                float2 bend = (WorldToScreenUV(exitWS) - WorldToScreenUV(positionWS)) * (_Refraction * lerp(1.0, _IceRefraction, solid));
                // Anything nearer than this surface where the bent sample lands (a wall, a crate, the ship above the
                // water) mustn't show up inside it: look straight through there instead.
                bend *= saturate((SceneEyeDepth(screenUV + bend) - surfaceEye + 0.02) * 25.0);
                // Red bends a little less than blue; the crystal splits colour a little more.
                float dispersion = _Dispersion * (1.0 + solid);
                half3 behind;
                behind.r = SampleSceneColor(screenUV + bend * (1.0 - dispersion)).r;
                behind.g = SampleSceneColor(screenUV + bend).g;
                behind.b = SampleSceneColor(screenUV + bend * (1.0 + dispersion)).b;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS), positionWS, half4(1, 1, 1, 1));
                half sunShadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                // Light gets through water, so shadows on it (including its own) are only partial.
                half bodyShadow = lerp(1.0h, sunShadow, _ShadowStrength);
                half sunOnWater = saturate(dot(geometricNormal, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                // Light scatters inside the water from every side: half-Lambert.
                half3 bodyLight = ambient + mainLight.color * (saturate(dot(geometricNormal, mainLight.direction) * 0.5 + 0.5) * bodyShadow);

                // Beer-Lambert: the tint is what's left after 1 m, so the colour falls off as tint^distance. Light
                // scattered toward the camera fills in part of what was absorbed (murk). The caustic threads brighten
                // what's seen through, as if focused there by the ripples.
                half3 transmittance = pow(max(_WaterTint.rgb, 1e-3h), waterDepth * _Density);
                half3 caustic = mainLight.color * (thread * _Caustics * liquid * sunShadow * sunOnWater);
                half3 color = behind * (1.0h + caustic) * transmittance + _ScatterColor.rgb * bodyLight * (1.0h - transmittance);
                // Thin water (the puddle it squashes into at speed) would be nearly clear: keep a little body so it
                // still reads as water, not glass, from the follow camera.
                color = lerp(color, _ScatterColor.rgb * bodyLight, _SurfaceOpacity);

                // Hardened: glacier ice, painted rather than seen through. Deep blue where the view crosses the most
                // ice, paling to a glowing edge where it crosses little, with white cracks and trapped bubbles inside.
                UNITY_BRANCH
                if (solid > 0.001)
                {
                    // How much ice the view crosses, against the most it could cross going this way (straight through
                    // the middle): 0 at the silhouette, 1 in the middle, for a flat crystal as much as a round one. A
                    // slow noise in the skin's own space wobbles it, so the gradient looks laid in with a brush rather
                    // than computed, and the wobble turns with the crystal.
                    float throughMiddle = 2.0 / max(length(rayDir / bodyRadii), 1e-4);
                    half thickness = saturate(bodyChord / throughMiddle + RippleNoise(input.positionOS * 1.6 + 5.1).w * _IcePaint);
                    // The ice colours are mixed in a perceptual space (the square root of linear, close enough to
                    // sRGB), so blends spread the way a painter mixes them: mixed in linear, a little white swamps the
                    // deep blue.
                    half3 albedo = lerp(sqrt(_IceEdgeColor.rgb), sqrt(_IceMidColor.rgb), smoothstep(0.0h, 0.45h, thickness));
                    albedo = lerp(albedo, sqrt(_IceDeepColor.rgb), smoothstep(0.35h, 1.0h, thickness));

                    // Cracks and bubbles on two shells at fixed depths along the refracted ray. They're found in the
                    // skin's own space, so they turn with the crystal and slide behind its surface as it does. The
                    // deeper shell is dimmer and bluer, as if seen through more ice, and neither shows past where the
                    // ray leaves the body.
                    float3 rayOS = TransformWorldToObjectDir(refracted, false);
                    [unroll]
                    for (int layer = 0; layer < 2; layer++)
                    {
                        float depth = _IceLayerDepth * (1.0 + 1.6 * layer);
                        float3 p = input.positionOS + rayOS * depth;
                        half marks = max(IceCracks(p / _IceCrackSize, footprint / _IceCrackSize),
                                         IceBubbles(p / _IceBubbleSize + 7.7 * layer, footprint / _IceBubbleSize));
                        half inside = saturate((exitDistance - depth) / (0.3 * depth));
                        half3 markColor = lerp(sqrt(_IceCrackColor.rgb), sqrt(_IceMidColor.rgb), 0.45h * layer);
                        albedo = lerp(albedo, markColor, marks * inside * (1.0h - 0.4h * layer));
                    }
                    albedo *= albedo;

                    // Facets lit in soft painted bands: sunlit, or in shade where some sun still comes through tinted
                    // blue-violet (the shadows of a painting, not a photo). The light is taken relative to full sun
                    // and sky, so the colours above are exactly what a sunlit facet shows (however bright the sun
                    // is) and the light's colour still tints it. Sunlit facets that face up wash toward white, like
                    // the top of a painted block catching the sun.
                    half halfLambert = saturate(dot(geometricNormal, mainLight.direction) * 0.5 + 0.5);
                    half lit = lerp(halfLambert, smoothstep(0.4h, 0.7h, halfLambert), _IceBanding) * lerp(1.0h, sunShadow, 0.65h);
                    half3 fullLight = ambient + mainLight.color;
                    half exposure = 1.0h / max(Luminance(fullLight), 0.05h);
                    half3 white = fullLight * exposure;
                    half3 ice = albedo * (ambient + mainLight.color * lerp(_IceShadeColor.rgb, 1.0h, lit)) * exposure;
                    // The washes below are mixed perceptually too.
                    ice = sqrt(max(ice, 0.0h));
                    ice = lerp(ice, sqrt(white), saturate(geometricNormal.y) * lit * _IceTopWash);
                    // Facets toward the silhouette glow pale: the thin lip of the block, lit through. Only where the
                    // view crosses little ice, so the broad top of a flat crystal doesn't glow all over.
                    half edge = pow(1.0h - saturate(dot(geometricNormal, viewDir)), _IceRimPower) * (1.0h - thickness);
                    ice = lerp(ice, sqrt(_IceRimColor.rgb * white), saturate(edge * _IceRimStrength));
                    ice *= ice;
                    // A little of what's behind shows through where it's thin.
                    ice = lerp(ice, behind * _IceEdgeColor.rgb, _IceClarity * (1.0h - thickness));
                    color = lerp(color, ice, solid);
                }

                // Sky reflection, Schlick fresnel on the rippled normal: almost nothing facing the camera, all of it
                // at the grazing edge.
                half NdotV = saturate(dot(normalWS, viewDir));
                half grazing = 1.0h - NdotV;
                half grazing2 = grazing * grazing;
                half fresnel = _Reflectance + (1.0h - _Reflectance) * grazing2 * grazing2 * grazing;
                // Below the horizon the default skybox is a dark ground, but what's really under the blob is the pale
                // floor or the pale void: mirror downward reflections into the sky so the lower rim stays bright.
                float3 reflectDir = reflect(-viewDir, normalWS);
                reflectDir.y = abs(reflectDir.y);
                half3 sky = SkyReflection(reflectDir, lerp(0.03h, 0.08h, solid)) * _ReflectionTint.rgb;
                color = lerp(color, sky, fresnel);

                // Foam where the water meets the floor or the ship's hull (an opaque surface just behind this one),
                // broken up by the ripples; drip-ring crests froth a little too.
                float foamWidth = _FoamWidth * (1.0 + 1.5 * splash);
                half contact = saturate(1.0 - behindDistance / max(foamWidth, 1e-4));
                half foam = contact * contact * saturate(0.6 + ripples.w * 1.2) * _FoamStrength + saturate(dripCrest * 2.0 - 0.6) * 0.5;
                foam = saturate(foam * liquid);
                half3 foamLit = _FoamColor.rgb * (ambient + mainLight.color * (sunOnWater * 0.5 + 0.5) * bodyShadow);
                color = lerp(color, foamLit, foam);

                // A bright rim on the smooth outline, so the edge reads at the default camera distance too (the
                // physical fresnel alone is a pixel wide there). The ice has its own glowing edge.
                half rim = 1.0h - saturate(dot(geometricNormal, viewDir));
                color += _RimColor.rgb * (rim * rim * rim * _RimStrength * liquid);

                // Sun glints on the rippled normal. Specular anti-aliasing: the glint widens (and dims a little) by how
                // much the normal changes across the pixel, so small ripples and far-away facets don't fizz.
                // Hardened facets catch a tighter, brighter glint.
                float3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                float NdotH = saturate(dot(normalWS, halfDir));
                float3 normalDx = ddx(normalWS);
                float3 normalDy = ddy(normalWS);
                float variance = 0.5 * (dot(normalDx, normalDx) + dot(normalDy, normalDy));
                float sharpness = _GlintSharpness * (1.0 + 2.0 * solid);
                float filtered = sharpness / (1.0 + sharpness * variance);
                half glint = pow(NdotH, filtered) * sqrt(filtered / sharpness) * _GlintStrength * (1.0 + 0.5 * solid)
                           + pow(NdotH, filtered * 0.08) * _Sheen;
                glint *= sunShadow * saturate(sunOnWater * 4.0) * (1.0h - foam);
                color += mainLight.color * glint;

                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP for the light whose shadow map is being drawn (as in the Lit shadow caster).
            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                ShadowVaryings output;
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                // Water lets most of the light through: skip a share of the shadow-map texels in a 4x4 ordered pattern
                // (fixed in shadow-map space), and the soft-shadow filter averages that into a partial shadow. Ordered
                // rather than random, so even the small 3x3 filter smooths it instead of leaving grain. Frosted ice
                // blocks more.
                half opacity = lerp(_ShadowOpacity, _IceShadowOpacity, _Solid);
                uint2 texel = uint2(input.positionCS.xy) & 3u;
                static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                clip(opacity - (bayer[texel.y * 4u + texel.x] + 0.5) / 16.0);
                return 0;
            }
            ENDHLSL
        }
    }
}
