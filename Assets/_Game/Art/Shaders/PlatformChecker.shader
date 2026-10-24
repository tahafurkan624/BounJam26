// Level platform blocks: a world-space checker across the tops, so neighbouring blocks (and stretched ones at the
// edge of a floor) line up seamlessly. Set both colours the same for plain sides. Main light + shadows + ambient only,
// like the greybox grid.
Shader "BounJam/Platform Checker"
{
    Properties
    {
        _ColorA ("Color A", Color) = (1, 0.95, 0.81, 1)
        _ColorB ("Color B", Color) = (1, 0.85, 0.58, 1)
        _CellSize ("Check Size (m)", Float) = 0.5
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
            // Forward+ leaves the per-object light data at zero; without this keyword the sun never reaches the floor.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;
                float _CellSize;
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

            // 0 on A squares, 1 on B squares, box-filtered over the pixel footprint so it fades to grey far away
            // instead of shimmering.
            float Checker(float2 p)
            {
                float2 cell = p / _CellSize;
                float2 w = fwidth(cell) + 0.001;
                float2 i = 2.0 * (abs(frac((cell - 0.5 * w) * 0.5) - 0.5) - abs(frac((cell + 0.5 * w) * 0.5) - 0.5)) / w;
                return 0.5 - 0.5 * i.x * i.y;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                half3 albedo = lerp(_ColorA.rgb, _ColorB.rgb, Checker(input.positionWS.xz));

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
