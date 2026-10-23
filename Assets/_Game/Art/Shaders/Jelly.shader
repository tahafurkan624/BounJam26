// Thick translucent jelly for the liquid blob. Cheap single pass, no raymarching:
// - refraction: the scene behind (URP opaque texture) is bent by the surface normal and a slow internal wobble,
//   then tinted, darker where the view crosses more jelly (facing the camera) and lighter at the thin edges
// - a lit "body" colour blended over it so the shape still reads as a solid volume from above
// - fake subsurface glow when light shines through from behind, fresnel rim + reflection, sharp wet highlight
// Renders in the transparent queue (after the opaque texture is captured) but writes depth and outputs an opaque
// colour, so the ship can sit half inside it and still sort correctly.
Shader "BounJam/Jelly"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow Tint (thin edges)", Color) = (0.45, 1.0, 0.85, 1)
        _DeepColor ("Deep Tint (thick middle)", Color) = (0.03, 0.55, 0.5, 1)
        _BodyColor ("Body Colour (lit)", Color) = (0.10, 0.80, 0.66, 1)
        _Opacity ("Opacity (thin edges)", Range(0, 1)) = 0.3
        _CoreOpacity ("Opacity (thick middle)", Range(0, 1)) = 0.6

        [Header(Refraction)]
        _Refraction ("Refraction Strength", Range(0, 0.2)) = 0.06
        _Blur ("Scatter Blur", Range(0, 0.05)) = 0.015
        _Wobble ("Inner Wobble", Range(0, 0.02)) = 0.004
        _WobbleSpeed ("Wobble Speed", Range(0, 5)) = 1.5

        [Header(Lighting)]
        _Wrap ("Wrap Lighting", Range(0, 1)) = 0.5
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.35
        _Shininess ("Highlight Sharpness", Range(8, 512)) = 160
        _Specular ("Highlight Strength", Range(0, 4)) = 1.2
        _Reflection ("Reflection Strength", Range(0, 2)) = 0.6

        [Header(Rim)]
        _RimColor ("Rim Colour", Color) = (0.75, 1.0, 0.95, 1)
        _RimPower ("Rim Tightness", Range(0.5, 8)) = 2.5
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.7

        [Header(Solid Form)]
        _Solid ("Solid (0 jelly, 1 hardened)", Range(0, 1)) = 0
        _SolidColor ("Hardened Body Colour", Color) = (0.55, 0.85, 0.88, 1)
        _SolidOpacity ("Hardened Opacity", Range(0, 1)) = 0.8

        [Header(Subsurface)]
        _SSSColor ("Glow Colour", Color) = (0.5, 1.0, 0.75, 1)
        _SSSStrength ("Glow Strength", Range(0, 3)) = 0.8
        _SSSDistortion ("Glow Spread", Range(0, 1)) = 0.4
        _SSSPower ("Glow Tightness", Range(1, 16)) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardJelly"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _BodyColor;
                half _Opacity;
                half _CoreOpacity;
                float _Refraction;
                float _Blur;
                float _Wobble;
                float _WobbleSpeed;
                half _Wrap;
                half _ShadowStrength;
                half _Shininess;
                half _Specular;
                half _Reflection;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                half _Solid;
                half4 _SolidColor;
                half _SolidOpacity;
                half4 _SSSColor;
                half _SSSStrength;
                half _SSSDistortion;
                half _SSSPower;
            CBUFFER_END

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
                float fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                Varyings output;
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // Looking straight in crosses the most jelly; grazing the edge crosses the least.
                half thickness = saturate(dot(normalWS, viewDir));

                // What's behind, bent by the surface and a slow wobble inside the jelly.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float3 normalVS = TransformWorldToViewNormal(normalWS);
                float t = _Time.y * _WobbleSpeed;
                float3 p = input.positionWS * 6.0;
                float2 wobble = float2(sin(p.x + t) + sin(p.z * 1.3 - t * 0.8),
                                       sin(p.y * 1.1 + t * 1.2) + sin(p.x * 0.7 - t)) * _Wobble;
                // Hardened: much less bending and no inner wobble.
                float refraction = _Refraction * (1.0 - 0.7 * _Solid);
                wobble *= 1.0 - _Solid;
                float2 refractedUV = screenUV - normalVS.xy * refraction * (1.0 - thickness * 0.5) + wobble;
                // Thick jelly scatters: average a small ring of samples so edges seen through it (like the
                // blob's own shadow on the floor) blur instead of cutting a hard line across it.
                const float2 ring[6] = { float2(1, 0), float2(0.5, 0.866), float2(-0.5, 0.866),
                                         float2(-1, 0), float2(-0.5, -0.866), float2(0.5, -0.866) };
                float2 blur = _Blur * float2(1.0, _ScaledScreenParams.x / _ScaledScreenParams.y);
                half3 behind = 0;
                for (int i = 0; i < 6; i++) behind += SampleSceneColor(refractedUV + ring[i] * blur);
                behind /= 6.0;
                half3 transmitted = behind * lerp(_ShallowColor.rgb, _DeepColor.rgb, thickness);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS), input.positionWS, half4(1, 1, 1, 1));
                // Light gets through jelly, so shadows on it (including its own) are only partial.
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength) * mainLight.distanceAttenuation;
                half wrapped = saturate((dot(normalWS, mainLight.direction) + _Wrap) / (1.0 + _Wrap));
                half3 bodyColor = lerp(_BodyColor.rgb, _SolidColor.rgb, _Solid);
                half3 body = bodyColor * (SampleSH(normalWS) + mainLight.color * wrapped * shadow);

                half opacity = lerp(lerp(_Opacity, _CoreOpacity, thickness * thickness), _SolidOpacity, _Solid);
                half3 color = lerp(transmitted, body, opacity);

                // Light shining through from behind, strongest at the thinner parts.
                half3 throughDir = normalize(mainLight.direction + normalWS * _SSSDistortion);
                half translucency = pow(saturate(dot(viewDir, -throughDir)), _SSSPower) * _SSSStrength;
                color += _SSSColor.rgb * mainLight.color * translucency * (1.0 - thickness * 0.5);

                half fresnel = pow(1.0 - thickness, _RimPower);
                half3 reflection = GlossyEnvironmentReflection(reflect(-viewDir, normalWS), input.positionWS, 0.05h, 1.0h);
                color += reflection * fresnel * _Reflection + _RimColor.rgb * fresnel * _RimStrength * (1.0 + 0.5 * _Solid);

                // Hardened facets catch a tighter, brighter glint.
                half3 halfDir = normalize(mainLight.direction + viewDir);
                half shininess = _Shininess * (1.0 + 2.0 * _Solid);
                half highlight = pow(saturate(dot(normalWS, halfDir)), shininess) * _Specular * (1.0 + 0.5 * _Solid) * shadow;
                color += mainLight.color * highlight;

                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
    }
}
