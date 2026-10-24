// Water for the Water volumes (scaled cubes, often several laid side by side). One premultiplied-alpha pass that
// draws all the boxes as one body of water:
// - a face with more water right outside it is inside the body of water and is dropped (Water.cs shares every box
//   with the shader), and where tops overlap a stencil bit lets only the first through, so neighbouring boxes never
//   double up into seams. Everything on the surface is in world space, so it runs on unbroken from box to box
// - the view's path through the water sets how much shows through: the scene behind (URP opaque texture, bent by the
//   ripples), or, where the box is open underneath, the dark of deep water
// - colour by that path: clear turquoise over the shallows, deep blue in the middle, lit by the sun and sky
// - a stack of sharp-crested waves ripples the top: sun glint and twinkling sparkles, fresnel sky reflection, lighter
//   crests and a faint net of light drifting on the surface
// - caustics dance on whatever is under water, fading with depth; foam hugs walls and anything standing in the water,
//   with a thin line lapping out from it
// - outer side faces show the box in cross-section: pale at the waterline, darkening downwards
// Premultiplied so whatever was drawn before the water (the jelly blob, which writes depth but isn't in the opaque
// texture) still shows through it, dimmed and tinted. Drawn after the jelly, so the part of the blob above the
// surface stays in front of it.
Shader "BounJam/Water"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow Colour", Color) = (0.36, 0.76, 1, 1)
        _DeepColor ("Deep Colour", Color) = (0.12, 0.44, 0.88, 1)
        _ColorDepth ("Deep Colour Reached At (m of view through water)", Range(0.1, 5)) = 1.0
        _Clarity ("Clarity (m of water you can see through)", Range(0.05, 5)) = 0.6
        _ClearTint ("Tint Of What Shows Through", Color) = (0.62, 0.84, 1, 1)
        _SeabedColor ("Open Bottom Colour", Color) = (0.04, 0.16, 0.42, 1)

        [Header(Waves)]
        _WaveSize ("Wave Size (m)", Range(0.1, 5)) = 1.1
        _WaveStrength ("Wave Strength", Range(0, 2)) = 0.9
        _WaveSpeed ("Wave Speed", Range(0, 3)) = 1
        _WaveDirection ("Wind Direction (degrees)", Range(0, 360)) = 35
        _Refraction ("Refraction", Range(0, 0.1)) = 0.03

        [Header(Light)]
        _Specular ("Sun Glint", Range(0, 4)) = 1.2
        _Shininess ("Glint Sharpness", Range(16, 2048)) = 500
        _Sparkle ("Sparkles", Range(0, 4)) = 1.5
        _Reflection ("Sky Reflection", Range(0, 2)) = 1
        _CrestColor ("Crest Colour", Color) = (0.55, 0.88, 1, 1)
        _CrestLight ("Crest Light", Range(0, 1)) = 0.35
        _SurfaceNet ("Light Net On The Surface", Range(0, 1)) = 0.2
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.6

        [Header(Caustics)]
        _CausticColor ("Caustic Colour", Color) = (0.85, 0.97, 1, 1)
        _Caustics ("Caustic Strength", Range(0, 2)) = 0.5
        _CausticSize ("Caustic Size (m)", Range(0.1, 3)) = 0.8
        _CausticDepth ("Caustics Fade By (m)", Range(0.1, 5)) = 1

        [Header(Foam)]
        _FoamColor ("Foam Colour", Color) = (0.92, 0.98, 1, 1)
        _FoamWidth ("Foam Width (m)", Range(0, 1)) = 0.15
        _FoamAmount ("Foam Amount", Range(0, 1)) = 0.9

        [Header(Sides)]
        _SideOpacity ("Side Opacity", Range(0, 1)) = 0.9
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardWater"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Back
            Blend One OneMinusSrcAlpha
            // Each pixel takes water once, from whichever box gets there first.
            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 64
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _ColorDepth;
                float _Clarity;
                half4 _ClearTint;
                half4 _SeabedColor;
                float _WaveSize;
                half _WaveStrength;
                float _WaveSpeed;
                float _WaveDirection;
                half _Refraction;
                half _Specular;
                half _Shininess;
                half _Sparkle;
                half _Reflection;
                half4 _CrestColor;
                half _CrestLight;
                half _SurfaceNet;
                half _ShadowStrength;
                half4 _CausticColor;
                half _Caustics;
                float _CausticSize;
                float _CausticDepth;
                half4 _FoamColor;
                float _FoamWidth;
                half _FoamAmount;
                half _SideOpacity;
            CBUFFER_END

            // Every Water box in the scene as its world-to-local matrix, kept up to date by Water.cs.
            #define MAX_WATER_BOXES 64
            float4x4 _WaterBoxes[MAX_WATER_BOXES];
            float _WaterBoxCount;

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
                float2 span : TEXCOORD2; // world height of this box's surface (x) and bottom (y)
                float eyeDepth : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                Varyings output;
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.span = float2(TransformObjectToWorld(float3(0, 0.5, 0)).y, TransformObjectToWorld(float3(0, -0.5, 0)).y);
                output.eyeDepth = -position.positionVS.z;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            bool InsideWater(float3 positionWS)
            {
                int count = min((int)_WaterBoxCount, MAX_WATER_BOXES);
                for (int i = 0; i < count; i++)
                {
                    float3 local = mul(_WaterBoxes[i], float4(positionWS, 1.0)).xyz;
                    if (all(abs(local) <= 0.5001)) return true;
                }
                return false;
            }

            // Explicit mip so these work inside branches.
            float SceneEyeDepth(float2 uv)
            {
                uv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv), _CameraDepthTexture_TexelSize.xy);
                return LinearEyeDepth(SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, uv, 0).r, _ZBufferParams);
            }

            half3 SceneColor(float2 uv)
            {
                uv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv), _CameraOpaqueTexture_TexelSize.xy);
                return SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv, 0).rgb;
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash12(i), Hash12(i + float2(1, 0)), u.x),
                            lerp(Hash12(i + float2(0, 1)), Hash12(i + float2(1, 1)), u.x), u.y);
            }

            // Height (x, about 0..1) and slope (yz) of the surface at world point p: a stack of sharp-crested waves,
            // each smaller and quicker than the last and turned to a new heading, every one nudging where the next
            // are sampled so the crests wander instead of tiling.
            float3 Waves(float2 p)
            {
                p /= max(_WaveSize, 0.05);
                float time = _Time.y * _WaveSpeed;
                float angle = radians(_WaveDirection);
                float amplitude = 1.0;
                float frequency = 1.0;
                float total = 0.0;
                float height = 0.0;
                float2 slope = 0.0;
                [unroll]
                for (int i = 0; i < 10; i++)
                {
                    float2 heading = float2(cos(angle), sin(angle));
                    // Longer waves travel faster, as on deep water.
                    float x = dot(heading, p) * frequency - time * sqrt(frequency) * 1.7;
                    float wave = exp(sin(x) - 1.0);
                    float rise = wave * cos(x);
                    height += wave * amplitude;
                    slope += heading * (rise * amplitude * frequency);
                    p -= heading * (rise * amplitude * 0.35);
                    total += amplitude;
                    amplitude *= 0.78;
                    frequency *= 1.22;
                    angle += 2.39996; // golden angle: headings never line up
                }
                return float3(height, slope) / total;
            }

            // 0 on the borders of a drifting cell pattern, rising towards the cell middles.
            float CellBorders(float2 p, float time)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float nearest = 8.0;
                float second = 8.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 offset = float2(x, y);
                    float2 seed = Hash22(cell + offset);
                    float2 site = offset + 0.5 + 0.38 * sin(time * (0.6 + 0.5 * seed) + seed * 6.2832);
                    float d = distance(site, f);
                    second = d < nearest ? nearest : min(second, d);
                    nearest = min(nearest, d);
                }
                return second - nearest;
            }

            // The web of light that ripples focus onto what's under them: two drifting cell patterns' borders.
            half Caustics(float3 positionWS)
            {
                // Lean the pattern with height so it also lands on walls, not just floors.
                float2 p = (positionWS.xz + positionWS.y * float2(0.45, -0.3)) / max(_CausticSize, 0.05);
                float time = _Time.y * _WaveSpeed;
                half a = saturate(1.0 - CellBorders(p, time) * 3.0);
                half b = saturate(1.0 - CellBorders(p * 1.37 + 5.3, time * 0.8) * 3.0);
                return a * a * a + b * b * b * 0.7;
            }

            // Tiny points that wink on and off as wavelets catch the sun, in a quarter of the cells of a fine grid.
            half Sparkles(float2 p, float time)
            {
                float2 grid = p * 7.0;
                float2 cell = floor(grid);
                float2 seed = Hash22(cell);
                float2 offset = frac(grid) - (0.2 + 0.6 * seed);
                half spot = saturate(1.0 - length(offset) * 8.0);
                half wink = saturate(sin(time * (1.5 + 3.0 * seed.x) + seed.y * 6.2832) * 4.0 - 3.0);
                return spot * spot * wink * step(0.75, Hash12(cell + 17.0));
            }

            // A faint net of light on the surface, carried along and bent by the waves: what makes it read as water
            // from high above, where the waves themselves barely change the shading.
            half SurfaceNet(float2 p, float3 wave, float time)
            {
                // The waves bend the cell borders into wavy lines (straight ones look like cracked ice).
                float2 q = (p + wave.yz * 1.6) / max(_CausticSize * 0.8, 0.05);
                half a = saturate(1.0 - CellBorders(q, time * 0.6) * 7.0);
                half b = saturate(1.0 - CellBorders(q * 0.77 + 3.1, -time * 0.45) * 7.0);
                return max(a * a * a, b * b * b * 0.6);
            }

            // Foam where the water is shallowest over what's under it: a band against the edge with its rim eaten
            // into bubbles, and a thinner line rolling out from it. depthBelow is how far under the surface the
            // scene point is, aa about one pixel in the same units.
            half Foam(float depthBelow, float2 p, float time, float aa)
            {
                float width = max(_FoamWidth, 1e-3);
                float edge = saturate(1.0 - depthBelow / width);
                float noise = ValueNoise(p * 3.3 + float2(time * 0.23, time * 0.17)) * 0.6
                            + ValueNoise(p * 8.1 - float2(time * 0.31, -time * 0.19)) * 0.4;
                aa = max(aa / width, 0.02);
                half band = smoothstep(0.55 - aa, 0.55 + aa, edge - (noise - 0.5) * 0.5);
                float roll = frac(time * 0.25 + noise * 0.2);
                float lineAt = 1.0 - roll;
                half lap = smoothstep(0.06 + aa, 0.06 - aa, abs(edge - lineAt)) * (1.0 - roll) * step(0.01, edge);
                return saturate(band + lap * 0.8);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                // A face with more water right outside it is inside the body of water: drop it so the boxes join up.
                if (InsideWater(input.positionWS + normalWS * 0.05)) discard;

                float time = _Time.y * _WaveSpeed;
                float3 cameraWS = GetCameraPositionWS();
                float3 toPixel = input.positionWS - cameraWS;
                float3 rayDir = normalize(toPixel);
                half3 viewDir = -rayDir;
                float pixelEye = input.eyeDepth;
                // Converts eye depth past this pixel into distance along the view.
                float alongView = length(toPixel) / pixelEye;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float surfaceY = input.span.x;
                float floorY = input.span.y;
                // Roughly one pixel in metres, for antialiasing the foam's edges.
                float pixelSize = length(fwidth(input.positionWS));

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);
                half3 sun = mainLight.color * shadow;
                half3 ambient = SampleSH(half3(0, 1, 0));
                half sunHeight = saturate(mainLight.direction.y);
                // Everything is lit relative to flat water in the open, so the colours show as picked whatever the
                // light's strength, with ripples and shadows shading them.
                half3 flatLight = max(ambient + mainLight.color * sunHeight, 0.05h);
                half3 foamLit = _FoamColor.rgb * (ambient + sun * sunHeight) / flatLight;

                float sceneEye = SceneEyeDepth(screenUV);
                float scenePath = max(0.0, sceneEye - pixelEye) * alongView;

                if (normalWS.y < 0.5)
                {
                    // Side: the box in cross-section, the light from above fading with depth.
                    float below = max(0.0, surfaceY - input.positionWS.y);
                    float height = max(surfaceY - floorY, 1e-3);
                    half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(0.3 + 0.7 * below / height));
                    half fade = exp(-below / max(_CausticDepth, 0.05));
                    half3 color = body * (ambient + sun * sunHeight * (0.4 + 0.6 * fade)) / flatLight;
                    color *= 1.0 + _CausticColor.rgb * (Caustics(input.positionWS) * fade * _Caustics * 0.5);
                    half waterline = 1.0 - smoothstep(0.0, 0.05, below);
                    color = lerp(color, foamLit, waterline * 0.8);
                    // Something just behind the side (inside the water) shows through a little.
                    half alpha = _SideOpacity * (1.0 - exp(-scenePath / max(_Clarity * 0.5, 0.01)));
                    alpha = max(alpha, waterline * 0.8);
                    return half4(MixFog(color, input.fogFactor) * alpha, alpha);
                }

                float3 wave = Waves(input.positionWS.xz);
                half3 n = normalize(half3(-wave.y * _WaveStrength, 1.0, -wave.z * _WaveStrength));

                // How far the view runs through water: to the scene behind, or out of the box's bottom if that's
                // nearer (nothing below it, as on a platform over the void).
                float floorPath = (input.positionWS.y - floorY) / max(-rayDir.y, 1e-3);
                float path = min(scenePath, floorPath);

                // Refraction: show the scene a little off to the side, unless that pulls in something in front.
                float2 bend = mul((float3x3)GetWorldToViewMatrix(), float3(n.x, 0, n.z)).xy * (_Refraction * saturate(path));
                float2 bentUV = screenUV + bend;
                float bentEye = SceneEyeDepth(bentUV);
                if (bentEye < pixelEye)
                {
                    bentUV = screenUV;
                    bentEye = sceneEye;
                }
                float3 scenePos = cameraWS + toPixel * (bentEye / pixelEye);
                scenePath = max(0.0, bentEye - pixelEye) * alongView;
                path = min(scenePath, floorPath);
                half open = saturate((scenePath - floorPath) * 10.0);
                float depthBelow = lerp(surfaceY - scenePos.y, surfaceY - floorY, open);

                half clear = exp(-path / max(_Clarity, 0.01));
                half deep = 1.0 - exp(-path / max(_ColorDepth, 0.01));
                // Height around its average of about 0.47, give or take 0.12.
                half crest = saturate((wave.x - 0.47) * 4.0 + 0.5);
                half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep);
                body = lerp(body, _CrestColor.rgb, crest * _CrestLight);
                body *= (ambient + sun * saturate(dot(n, mainLight.direction))) / flatLight;

                // Light focused by the ripples onto what's under them, where the sun reaches it.
                half sceneShadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(scenePos));
                half caustic = Caustics(scenePos) * exp(-max(depthBelow, 0.0) / max(_CausticDepth, 0.05))
                             * saturate(depthBelow * 20.0) * lerp(1.0h, sceneShadow, 0.8h) * _Caustics;
                half3 underwater = SceneColor(bentUV) * _ClearTint.rgb * (1.0 + _CausticColor.rgb * caustic);

                // Premultiplied: the scene already behind this pixel (with the jelly, if it's there) is kept at the
                // share that gets through, and only the change refraction and tint make to it is added here.
                half3 color = ((underwater - SceneColor(screenUV)) * (1.0 - open) + _SeabedColor.rgb * open) * clear
                            + body * (1.0 - clear);
                half alpha = 1.0 - clear * (1.0 - open);

                half fresnel = saturate((0.04 + 0.96 * pow(1.0 - saturate(dot(n, viewDir)), 5.0)) * _Reflection);
                half3 reflection = GlossyEnvironmentReflection(reflect(-viewDir, n), input.positionWS, 0.05h, 1.0h);
                color = color * (1.0 - fresnel) + reflection * fresnel;
                alpha = 1.0 - (1.0 - alpha) * (1.0 - fresnel);

                half3 halfDir = normalize(mainLight.direction + viewDir);
                half facing = saturate(dot(n, halfDir));
                half glint = pow(facing, _Shininess) * _Specular
                           + Sparkles(input.positionWS.xz + wave.yz * 0.4, _Time.y) * (0.06 + pow(facing, 8.0)) * _Sparkle;
                color += sun * glint;

                half net = SurfaceNet(input.positionWS.xz, wave, time) * _SurfaceNet * lerp(0.5h, 1.0h, shadow);
                half foam = Foam(max(depthBelow, 0.0), input.positionWS.xz, time, pixelSize) * (1.0 - open) * _FoamAmount;
                half cover = saturate(max(foam, net));
                color = lerp(color, foamLit, cover);
                alpha = lerp(alpha, 1.0h, cover);

                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
