// World-space grid for greybox geometry: lines stay 1 cell = _CellSize metres on any scaled cube,
// ramp or ProBuilder shape, so distances and speeds are easy to read. Main light + shadows + ambient only.
Shader "BounJam/Greybox Grid"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.62, 0.63, 0.66, 1)
        _LineColor ("Line Color", Color) = (0.5, 0.51, 0.54, 1)
        _CellSize ("Cell Size (m)", Float) = 1
        _LineWidth ("Line Width (fraction of cell)", Range(0.005, 0.25)) = 0.04
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // Forward+ leaves the per-object light data at zero; without this keyword the sun never reaches the surface.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LineColor;
                float _CellSize;
                float _LineWidth;
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

            // 1 on a grid line, 0 between lines, anti-aliased with screen-space derivatives.
            float GridLines(float2 p)
            {
                float2 cell = p / _CellSize;
                float2 aa = fwidth(cell);
                float2 distanceToLine = abs(frac(cell + 0.5) - 0.5);
                float2 lines = 1.0 - smoothstep(_LineWidth * 0.5 - aa, _LineWidth * 0.5 + aa, distanceToLine);
                return max(lines.x, lines.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);

                // Triplanar: project the grid along whichever world axis the surface faces most.
                float3 weights = pow(abs(normalWS), 4.0);
                weights /= weights.x + weights.y + weights.z;
                float grid = weights.x * GridLines(input.positionWS.yz)
                           + weights.y * GridLines(input.positionWS.xz)
                           + weights.z * GridLines(input.positionWS.xy);
                half3 albedo = lerp(_BaseColor.rgb, _LineColor.rgb, grid);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS), input.positionWS, half4(1, 1, 1, 1));
                half diffuse = saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 color = albedo * (SampleSH(normalWS) + mainLight.color * diffuse);
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
}
