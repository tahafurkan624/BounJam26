// The main menu's exit effect: the whole picture goes liquid and slips away, as if the player were losing
// consciousness. Drawn by a full-screen uGUI graphic on an overlay canvas over everything else. It reads the finished
// frame from _GlassSceneTexture (GlassSceneCapture), which holds the 3D scene and the Screen Space - Camera canvas
// (so the menu's liquid buttons melt too), and redraws it warped:
//   _Progress 0 -> 1   rippling refraction and a slow swirl, columns of the picture dripping down, a growing blur
//                      with the colour channels sliding apart, the colour draining out, and a vignette closing to black
//   _MeltTime          seconds since the effect started (unscaled), for the motion
Shader "BounJam/UI/Screen Melt"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Progress ("Progress", Range(0, 1)) = 0
        _MeltTime ("Time", Float) = 0
        _Tint ("Tint", Color) = (0.55, 0.95, 0.9, 1)
        _BlurRadius ("Blur Radius (screen px at the end)", Float) = 38
        _Drip ("Drip Length (share of the screen at the end)", Float) = 0.45
        _Ripple ("Ripple Strength", Float) = 0.035

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
            Name "ScreenMelt"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_GlassSceneTexture);
            SAMPLER(sampler_GlassSceneTexture);
            float4 _GlassScreenParams; // width, height, 1/width, 1/height

            CBUFFER_START(UnityPerMaterial)
                float _Progress;
                float _MeltTime;
                float4 _Tint;
                float _BlurRadius;
                float _Drip;
                float _Ripple;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                return o;
            }

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n = i.x + i.y * 57.0;
                return lerp(lerp(Hash(n), Hash(n + 1.0), f.x), lerp(Hash(n + 57.0), Hash(n + 58.0), f.x), f.y);
            }

            float3 Scene(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(_GlassSceneTexture, sampler_GlassSceneTexture, saturate(uv), 0).rgb;
            }

            // Golden-angle spiral blur with the channels pulled apart along the drip.
            float3 SmearedScene(float2 uv, float radiusPx, float split)
            {
                float2 texel = _GlassScreenParams.zw;
                float3 sum = 0;
                UNITY_UNROLL
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * 2.39996;
                    float r = sqrt((i + 0.5) / 12.0) * radiusPx;
                    float2 tap = uv + float2(cos(angle), sin(angle)) * r * texel;
                    sum.r += Scene(tap + float2(split, split * 1.6) * texel).r;
                    sum.g += Scene(tap).g;
                    sum.b += Scene(tap - float2(split, split * 0.6) * texel).b;
                }
                return sum / 12.0;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float p = saturate(_Progress);
                float t = _MeltTime;
                // Screen UV with v = 0 at the bottom of the picture, whichever way the target is stored.
                float2 uv = i.positionCS.xy * _GlassScreenParams.zw;
                #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
                #endif
                float aspect = _GlassScreenParams.x * _GlassScreenParams.w;
                float2 centred = (uv - 0.5) * float2(aspect, 1.0);
                float dist = length(centred);

                float ease = p * p * (3.0 - 2.0 * p);
                float2 warped = uv;

                // A slow swirl, strongest in the middle, as the head starts to spin.
                float swirl = ease * 0.55 * saturate(1.0 - dist * 1.2) * sin(t * 0.9 + 0.6);
                float s = sin(swirl), c = cos(swirl);
                warped = float2(c * centred.x - s * centred.y, s * centred.x + c * centred.y) / float2(aspect, 1.0) + 0.5;

                // Liquid refraction: two drifting noise layers bend the picture like water running over the lens.
                float2 flow = float2(Noise(uv * 6.0 + float2(t * 0.7, -t * 1.3)), Noise(uv * 6.0 + float2(-t * 1.1, t * 0.5) + 17.0)) - 0.5;
                flow += 0.5 * (float2(Noise(uv * 13.0 - t * 1.7), Noise(uv * 13.0 + t * 1.9 + 41.0)) - 0.5);
                warped += flow * _Ripple * (0.35 + ease);

                // Drips: each thin column of the picture slides down by its own amount, the upper part furthest.
                float column = floor(uv.x * 46.0 + Noise(float2(uv.y * 3.0, t * 0.2)) * 1.5);
                float runLength = 0.35 + 0.65 * Hash(column * 1.37 + 2.0);
                float drip = ease * ease * _Drip * runLength * (0.55 + 0.45 * uv.y);
                drip += sin(uv.x * 31.0 + t * 2.3) * 0.004 * ease;
                warped.y += drip;

                float3 col = SmearedScene(warped, _BlurRadius * ease * (0.4 + dist), 9.0 * ease);

                // The colour drains out and the picture takes the liquid's tint.
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, luma * _Tint.rgb * 1.15, ease * 0.75);
                // Bright streaks where the drips run (the wet edge of each column).
                float streak = smoothstep(0.92, 1.0, frac(uv.x * 46.0)) * ease * 0.25;
                col += streak * _Tint.rgb;

                // The view closes in from the edges, then goes dark.
                float close = smoothstep(1.25 - ease * 1.1, 0.15 - ease * 0.4, dist);
                col *= lerp(1.0, close, ease);
                col *= 1.0 - smoothstep(0.7, 1.0, p);

                // Fades in over the first moment so the click itself still reads.
                float a = saturate(p * 8.0) * i.color.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
