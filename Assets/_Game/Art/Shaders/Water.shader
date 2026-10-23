// Calm, clear water for the Water volumes (a scaled cube). Single alpha-blended pass, no refraction, so a crystal
// floating half under still shows through, tinted:
// - colour and opacity from how much water the view crosses before hitting the floor behind it (camera depth
//   texture): clear in the shallows, deep blue in the middle
// - slow crossing ripples tilt the top's normal for a moving sun glint and fresnel sky reflection
// - a soft foam line where the water meets walls, floors and anything standing in it
// Drawn after the jelly (which writes depth), so the part of the blob above the surface stays in front of it.
Shader "BounJam/Water"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow Colour", Color) = (0.30, 0.78, 0.92, 1)
        _DeepColor ("Deep Colour", Color) = (0.04, 0.26, 0.52, 1)
        _ShallowOpacity ("Opacity (shallow)", Range(0, 1)) = 0.35
        _DeepOpacity ("Opacity (deep)", Range(0, 1)) = 0.85
        _Murkiness ("Murkiness (per metre of water)", Range(0.1, 5)) = 1.2

        [Header(Surface)]
        _RippleSize ("Ripple Size (m)", Float) = 0.8
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.2
        _RippleSpeed ("Ripple Speed", Range(0, 3)) = 0.8
        _Shininess ("Highlight Sharpness", Range(8, 1024)) = 300
        _Specular ("Highlight Strength", Range(0, 4)) = 1.5
        _Reflection ("Reflection Strength", Range(0, 2)) = 0.6

        [Header(Foam)]
        _FoamColor ("Foam Colour", Color) = (0.92, 0.97, 1, 1)
        _FoamWidth ("Foam Width (m)", Range(0, 0.5)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardWater"
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
                float _RippleSize;
                half _RippleStrength;
                float _RippleSpeed;
                half _Shininess;
                half _Specular;
                half _Reflection;
                half4 _FoamColor;
                float _FoamWidth;
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

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // 1 on the top face, 0 on the sides: only the surface ripples, glints and foams.
                half top = saturate(normalWS.y * 2.0 - 1.0);

                // A few slow waves crossing at different angles; their slopes tilt the normal.
                float2 p = input.positionWS.xz / max(_RippleSize, 0.01);
                float t = _Time.y * _RippleSpeed;
                float2 slope = float2(cos(p.x * 1.0 + p.y * 0.3 + t * 1.1) * 0.5, cos(p.y * 1.1 - p.x * 0.2 - t * 0.9) * 0.5)
                             + float2(0.6, 0.8) * cos(dot(p, float2(0.6, 0.8)) * 2.3 - t * 1.7) * 0.3
                             + float2(-0.8, 0.5) * cos(dot(p, float2(-0.8, 0.5)) * 3.7 + t * 2.3) * 0.2;
                normalWS = normalize(normalWS + float3(slope.x, 0.0, slope.y) * (_RippleStrength * top));

                // How much water the view crosses before reaching what's behind it.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float behind = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float thickness = max(0.0, behind - input.eyeDepth);
                half deep = 1.0 - exp(-thickness * _Murkiness);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, 0.6h);
                half3 ambient = SampleSH(normalWS);
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep);
                half3 color = water * (ambient + mainLight.color * saturate(dot(normalWS, mainLight.direction)) * shadow);
                half alpha = lerp(_ShallowOpacity, _DeepOpacity, deep);

                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 4.0);
                half3 reflection = GlossyEnvironmentReflection(reflect(-viewDir, normalWS), input.positionWS, 0.05h, 1.0h);
                color += reflection * fresnel * _Reflection;

                half3 halfDir = normalize(mainLight.direction + viewDir);
                half highlight = pow(saturate(dot(normalWS, halfDir)), _Shininess) * _Specular * shadow * top;
                color += mainLight.color * highlight;
                alpha = saturate(alpha + fresnel * 0.3 + highlight);

                // Foam hugs the edges and wobbles along them with the ripples.
                float wobble = sin(p.x * 4.1 + t * 1.9) * sin(p.y * 3.7 - t * 1.4);
                half foam = saturate((1.0 - thickness / max(_FoamWidth, 1e-4)) * 1.6 - 0.3 + wobble * 0.25) * top;
                half3 foamLit = _FoamColor.rgb * (ambient + mainLight.color * shadow * 0.8);
                color = lerp(color, foamLit, foam);
                alpha = max(alpha, foam);

                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
