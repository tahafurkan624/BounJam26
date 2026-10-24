// BlobShip / Lake Water  —  realistic, light-foam lake surface for URP (Unity 2022.3+ / Unity 6)
//
// Needs, on the active URP Renderer / Pipeline Asset:  Depth Texture = ON, Opaque Texture = ON
//
// Mesh vertex colour (written by the Blender exporter, linear):
//   R = depth 0..1 (unused when the depth texture is available, kept as data)
//   G = distance to the shoreline in metres, clamped 0..1  -> damps waves near the shore + shore foam
//
// Features: Gerstner waves (3), physically based absorption through the real water thickness,
// screen-space refraction, Fresnel sky/probe reflection, sun glint, light scattering through wave
// crests, two scrolling normal maps, very light shore / contact foam (also around anything that
// touches the water), soft shoreline edge, fog, main-light shadows.

Shader "BlobShip/LakeWater"
{
    Properties
    {
        [Header(Water Colour)]
        _ShallowColor   ("Shallow Tint", Color) = (0.42, 0.88, 0.82, 1)
        _DeepColor      ("Deep Tint", Color) = (0.03, 0.25, 0.40, 1)
        _DeepDistance   ("Deep Tint Reached At (m)", Float) = 2.5
        _Absorption     ("Absorption per metre (RGB)", Vector) = (0.55, 0.12, 0.07, 0)
        _Clarity        ("Murkiness (higher = less clear)", Range(0.05, 5)) = 1.0

        [Header(Surface)]
        [Normal][NoScaleOffset] _NormalMap ("Ripple Normal Map", 2D) = "bump" {}
        _NormalStrength ("Ripple Strength", Range(0, 2)) = 0.55
        _NormalTiling   ("Ripple Tiling (per metre)", Float) = 0.28
        _NormalSpeed    ("Ripple Scroll (xy, zw)", Vector) = (0.035, 0.018, -0.022, 0.03)
        _Smoothness     ("Smoothness", Range(0, 1)) = 0.94
        _ReflectionStrength ("Reflection Strength", Range(0, 1.5)) = 1.0
        _RefractionStrength ("Refraction Strength", Range(0, 0.15)) = 0.035
        _SpecularStrength   ("Sun Glint Strength", Range(0, 6)) = 2.0
        _SSSColor       ("Crest Light Colour", Color) = (0.20, 0.75, 0.70, 1)
        _SSSStrength    ("Crest Light Strength", Range(0, 2)) = 0.6

        [Header(Waves  xy dir  z steepness  w wavelength m)]
        _WaveA ("Wave A", Vector) = (1.0, 0.35, 0.055, 3.2)
        _WaveB ("Wave B", Vector) = (-0.4, 1.0, 0.045, 1.9)
        _WaveC ("Wave C", Vector) = (0.7, -0.7, 0.035, 1.1)
        _WaveScale      ("Wave Height Multiplier", Range(0, 3)) = 1.0
        _WaveSpeed      ("Wave Speed Multiplier", Range(0, 3)) = 0.8
        _ShoreWaveFade  ("Shore Wave Fade (m)", Range(0.01, 1)) = 0.9

        [Header(Foam)]
        [NoScaleOffset] _FoamTex ("Foam Noise", 2D) = "white" {}
        _FoamColor      ("Foam Colour", Color) = (0.95, 0.98, 1.0, 1)
        _FoamIntensity  ("Shore Foam Amount", Range(0, 1)) = 0.45
        _FoamDistance   ("Shore Foam Width (m)", Range(0.02, 1.5)) = 0.35
        _FoamTiling     ("Foam Tiling (per metre)", Float) = 0.7
        _FoamSpeed      ("Foam Drift Speed", Float) = 0.04
        _CrestFoam      ("Crest Foam Amount", Range(0, 1)) = 0.12

        [Header(Edge)]
        _EdgeFade       ("Shoreline Softness (m)", Range(0.001, 0.5)) = 0.06
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-10" "IgnoreProjector"="True" }

        Pass
        {
            Name "LakeWaterForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            // screen-space shadows do not apply to transparents, so only the regular shadow map variants
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_FoamTex);   SAMPLER(sampler_FoamTex);

            CBUFFER_START(UnityPerMaterial)
                half4  _ShallowColor;
                half4  _DeepColor;
                float  _DeepDistance;
                float4 _Absorption;
                float  _Clarity;
                float  _NormalStrength;
                float  _NormalTiling;
                float4 _NormalSpeed;
                float  _Smoothness;
                float  _ReflectionStrength;
                float  _RefractionStrength;
                float  _SpecularStrength;
                half4  _SSSColor;
                float  _SSSStrength;
                float4 _WaveA;
                float4 _WaveB;
                float4 _WaveC;
                float  _WaveScale;
                float  _WaveSpeed;
                float  _ShoreWaveFade;
                half4  _FoamColor;
                float  _FoamIntensity;
                float  _FoamDistance;
                float  _FoamTiling;
                float  _FoamSpeed;
                float  _CrestFoam;
                float  _EdgeFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 tangentWS  : TEXCOORD1;
                float3 bitangentWS: TEXCOORD2;
                float3 normalWS   : TEXCOORD3;
                float4 screenPos  : TEXCOORD4;
                float3 waveData   : TEXCOORD5;   // x = wave height, y = shore distance (m), z = fog factor
            };

            // ---------------------------------------------------------------- Gerstner wave
            float3 GerstnerWave(float4 wave, float3 p, inout float3 tangent, inout float3 binormal)
            {
                float steepness  = wave.z * _WaveScale;
                float wavelength = max(wave.w, 0.05);
                float k = TWO_PI / wavelength;
                float c = sqrt(9.81 / k) * _WaveSpeed;
                float2 d = normalize(wave.xy);
                float f = k * (dot(d, p.xz) - c * _Time.y);
                float a = steepness / k;
                float s, co;
                sincos(f, s, co);
                tangent  += float3(-d.x * d.x * steepness * s, d.x * steepness * co, -d.x * d.y * steepness * s);
                binormal += float3(-d.x * d.y * steepness * s, d.y * steepness * co, -d.y * d.y * steepness * s);
                return float3(d.x * a * co, a * s, d.y * a * co);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);

                float shoreDist = input.color.g;                                   // metres to shore (0..1)
                float fade = smoothstep(0.0, _ShoreWaveFade, shoreDist);

                float3 dT = 0, dB = 0;
                float3 disp = GerstnerWave(_WaveA, posWS, dT, dB);
                disp += GerstnerWave(_WaveB, posWS, dT, dB);
                disp += GerstnerWave(_WaveC, posWS, dT, dB);

                float3 tangent  = float3(1, 0, 0) + dT * fade;
                float3 binormal = float3(0, 0, 1) + dB * fade;
                posWS += disp * fade;

                o.positionWS  = posWS;
                o.positionCS  = TransformWorldToHClip(posWS);
                o.tangentWS   = normalize(tangent);
                o.bitangentWS = normalize(binormal);
                o.normalWS    = normalize(cross(binormal, tangent));
                o.screenPos   = ComputeScreenPos(o.positionCS);
                o.waveData    = float3(disp.y * fade, shoreDist, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            float3 SceneWorldPos(float2 uv, out float rawDepth)
            {
                rawDepth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 posWS = input.positionWS;
                float  t = _Time.y;

                // ---------------- normals: Gerstner frame + two scrolling ripple maps
                float2 uvN = posWS.xz * _NormalTiling;
                half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvN + t * _NormalSpeed.xy), _NormalStrength);
                half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvN * 1.73 + float2(0.37, 0.61) + t * _NormalSpeed.zw), _NormalStrength * 0.7);
                half3 nTS = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));          // whiteout blend
                float3 N = normalize(nTS.x * input.tangentWS + nTS.y * input.bitangentWS + nTS.z * input.normalWS);

                float3 V = normalize(_WorldSpaceCameraPos - posWS);
                float  surfaceEye = input.screenPos.w;
                float2 screenUV = input.screenPos.xy / input.screenPos.w;

                // ---------------- water thickness from the depth texture
                float rawD;
                float3 sceneWS = SceneWorldPos(screenUV, rawD);
                float verticalDepth = max(posWS.y - sceneWS.y, 0.0);

                // ---------------- refraction (rejects samples from objects in front of the water)
                float2 offset = N.xz * _RefractionStrength * saturate(verticalDepth * 1.5);
                float2 uvR = screenUV + offset;
                float rawR;
                float3 sceneWSR = SceneWorldPos(uvR, rawR);
                if (LinearEyeDepth(rawR, _ZBufferParams) < surfaceEye)
                {
                    uvR = screenUV; sceneWSR = sceneWS; rawR = rawD;
                }
                float pathLength = max(LinearEyeDepth(rawR, _ZBufferParams) - surfaceEye, 0.0);   // light path through water
                float depthR = max(posWS.y - sceneWSR.y, 0.0);
                half3 sceneColor = SampleSceneColor(uvR);

                // ---------------- lighting
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 L = mainLight.direction;
                half  shadow = mainLight.shadowAttenuation;
                half3 sunColor = mainLight.color;
                half3 ambient = SampleSH(half3(0, 1, 0));

                // ---------------- body colour: absorption + in-scattering
                float3 transmittance = exp(-_Absorption.xyz * _Clarity * pathLength);
                half3 tint = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depthR / max(_DeepDistance, 0.01)));
                half3 scatterLight = ambient + sunColor * (0.35 + 0.65 * shadow) * saturate(L.y + 0.2);
                half3 body = sceneColor * transmittance + tint * scatterLight * (1.0 - transmittance);

                // ---------------- reflection + sun glint
                half  NdotV = saturate(dot(N, V));
                half  fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
                half3 R = reflect(-V, N);
                half3 reflection = GlossyEnvironmentReflection(R, 1.0 - _Smoothness, 1.0) * _ReflectionStrength;

                half3 H = normalize(L + V);
                half  specPow = exp2(10.0 * _Smoothness + 1.0);
                half  spec = pow(saturate(dot(N, H)), specPow) * (specPow + 8.0) / (8.0 * PI);
                half3 glint = spec * sunColor * shadow * _SpecularStrength;

                // light passing through the thin crests when looking towards the sun
                half crest = saturate(input.waveData.x * 25.0 + 0.35);
                half3 sss = pow(saturate(dot(V, -L)), 4.0) * crest * _SSSColor.rgb * sunColor * _SSSStrength * (1.0 - fresnel);

                half3 color = lerp(body, reflection, fresnel) + glint + sss;

                // ---------------- very light foam: shoreline / contact (depth) + rare crest foam
                float2 uvF = posWS.xz * _FoamTiling;
                half fA = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, uvF + float2(t, t * 0.6) * _FoamSpeed).r;
                half fB = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, uvF * 1.6 + float2(0.21, 0.73) - float2(t * 0.7, t) * _FoamSpeed).r;
                half foamNoise = saturate((fA + fB) * 0.6);
                half zoneDepth = 1.0 - saturate(verticalDepth / _FoamDistance);
                half zoneShore = 1.0 - saturate(input.waveData.y / _FoamDistance);
                half zone = max(zoneDepth, zoneShore * 0.6);
                half shoreFoam = smoothstep(1.0 - zone - 0.15, 1.0 - zone + 0.15, foamNoise) * zone;
                half crestFoam = saturate((input.waveData.x - 0.012) * 60.0) * foamNoise * _CrestFoam;
                half foam = saturate(shoreFoam * _FoamIntensity + crestFoam);
                half3 foamLit = _FoamColor.rgb * (ambient + sunColor * (0.3 + 0.7 * shadow));
                color = lerp(color, foamLit, foam);

                // ---------------- soft shoreline + fog
                half alpha = saturate(verticalDepth / _EdgeFade);
                color = MixFog(color, input.waveData.z);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
