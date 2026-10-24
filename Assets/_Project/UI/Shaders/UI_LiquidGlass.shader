// Liquid Glass for uGUI (URP). Drawn by GlassGraphic, which packs the shape into the vertex data:
//   uv0.xy  position in the rect, canvas units, relative to its centre
//   uv0.z   the graphic's own alpha (glass opacity, or the fill's opacity in solid mode)
//   uv1     (half width, half height, corner radius, mode)   mode 0 = glass, 1 = solid fill
//   uv2     (shadow strength, highlight strength, tint amount, blur multiplier)
//   color   rgb = tint (glass) or fill colour (solid), a = fade from CanvasGroups and button tints
// The canvas is Screen Space - Overlay, drawn after post-processing; GlassSceneCapture copies the finished frame into
// _GlassSceneTexture for it. The shape is a rounded box SDF, so corners stay crisp at any size: frosted blur of the
// scene, light bent in along the edges, a bright rim on the upper edge, a soft top-left sheen and a drop shadow.
Shader "BounJam/UI/Liquid Glass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _BlurRadius ("Blur Radius (screen px)", Float) = 16
        _Refraction ("Edge Refraction (screen px)", Float) = 14
        _EdgeWidth ("Edge Width (canvas units)", Float) = 22
        _Saturation ("Saturation", Float) = 1.35
        _ShadowOffset ("Shadow Offset", Float) = 10
        _ShadowSoftness ("Shadow Softness", Float) = 22

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent"
            "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "LiquidGlass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_GlassSceneTexture);
            SAMPLER(sampler_GlassSceneTexture);
            float4 _GlassScreenParams; // width, height, 1/width, 1/height

            CBUFFER_START(UnityPerMaterial)
                float _BlurRadius;
                float _Refraction;
                float _EdgeWidth;
                float _Saturation;
                float _ShadowOffset;
                float _ShadowSoftness;
            CBUFFER_END
            float4 _ClipRect;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float3 local : TEXCOORD0;
                float4 shape : TEXCOORD1;
                float4 look : TEXCOORD2;
                float2 canvasPos : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.local = v.uv0.xyz;
                o.shape = v.uv1;
                o.look = v.uv2;
                o.canvasPos = v.positionOS.xy;
                return o;
            }

            float RoundBox(float2 p, float2 halfSize, float radius)
            {
                float2 q = abs(p) - halfSize + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            float2 RoundBoxNormal(float2 p, float2 halfSize, float radius)
            {
                const float e = 0.75;
                float2 g = float2(RoundBox(p + float2(e, 0), halfSize, radius) - RoundBox(p - float2(e, 0), halfSize, radius),
                                  RoundBox(p + float2(0, e), halfSize, radius) - RoundBox(p - float2(0, e), halfSize, radius));
                return g / max(length(g), 1e-4);
            }

            float3 FrostedScene(float2 uv, float radiusPx)
            {
                float2 texel = _GlassScreenParams.zw;
                float3 sum = 0;
                // Golden-angle spiral: 16 taps fill the disc evenly.
                UNITY_UNROLL
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * 2.39996;
                    float r = sqrt((i + 0.5) / 16.0) * radiusPx;
                    float2 tap = saturate(uv + float2(cos(angle), sin(angle)) * r * texel);
                    sum += SAMPLE_TEXTURE2D_LOD(_GlassSceneTexture, sampler_GlassSceneTexture, tap, 0).rgb;
                }
                return sum / 16.0;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.local.xy;
                float fill = i.local.z;
                float2 halfSize = i.shape.xy;
                float radius = min(i.shape.z, min(halfSize.x, halfSize.y));
                float mode = i.shape.w;
                float shadowStrength = i.look.x;
                float highlight = i.look.y;
                float tintAmount = i.look.z;
                float blurMul = i.look.w;

                float d = RoundBox(p, halfSize, radius);
                float aa = max(fwidth(d), 1e-3);
                float shapeA = saturate(0.5 - d / aa);

                float sd = RoundBox(p - float2(0, -_ShadowOffset), halfSize, radius);
                float shadowA = shadowStrength * (1.0 - smoothstep(-_ShadowSoftness * 0.25, _ShadowSoftness, sd)) * (1.0 - shapeA);

                float3 col;
                float body = fill;
                if (mode > 0.5)
                {
                    // Solid fill: flat colour with a faint top sheen so bars and pills read as wet plastic, and a
                    // bright hairline rim (the CSS border) that stays visible on see-through fills.
                    float sheen = saturate(p.y / max(halfSize.y, 1.0)) * 0.18 * highlight;
                    col = i.color.rgb + sheen;
                    float rim = saturate(1.0 + d / 2.0) * highlight;
                    col = lerp(col, 1.0.xxx, rim * 0.7);
                    body = max(fill, rim * 0.8);
                }
                else
                {
                    // Screen UV from the pixel position; the overlay UI draws straight to the back buffer.
                    float2 uv = i.positionCS.xy * _GlassScreenParams.zw;
                    #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1.0 - uv.y;
                    #endif
                    float2 n = RoundBoxNormal(p, halfSize, radius);
                    float edge = saturate(1.0 + d / _EdgeWidth);
                    float2 texel = _GlassScreenParams.zw;
                    // Light bends inwards near the rim, like a thick lens.
                    float2 bent = uv - n * (edge * edge) * _Refraction * texel;
                    float3 scene = FrostedScene(bent, _BlurRadius * blurMul);
                    float luma = dot(scene, float3(0.299, 0.587, 0.114));
                    scene = lerp(luma.xxx, scene, _Saturation);

                    col = lerp(scene, i.color.rgb, tintAmount);
                    // Vertical body gradient and a top-left sheen (the CSS ::after highlight).
                    float vy = saturate(p.y / max(halfSize.y, 1.0) * 0.5 + 0.5);
                    col += vy * 0.10 * highlight;
                    float2 sheenP = (p - float2(-0.55 * halfSize.x, halfSize.y)) / float2(halfSize.x * 1.3, halfSize.y * 1.2);
                    float sheen = saturate(1.0 - length(sheenP));
                    col += sheen * sheen * 0.32 * highlight;
                    // Bright rim, strongest on the upper edge.
                    float rim = saturate(1.0 + d / 2.5);
                    col = lerp(col, 1.0.xxx, rim * (0.45 + 0.4 * saturate(n.y)) * highlight);
                    // Soft inner glow along the bottom edge.
                    col += edge * saturate(-n.y) * 0.08 * highlight;
                }

                float bodyA = shapeA * body;
                float a = bodyA + shadowA * (1.0 - shapeA);
                float3 rgb = (col * bodyA) / max(a, 1e-4);
                a *= i.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                float2 inside = step(_ClipRect.xy, i.canvasPos) * step(i.canvasPos, _ClipRect.zw);
                a *= inside.x * inside.y;
                #endif

                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
