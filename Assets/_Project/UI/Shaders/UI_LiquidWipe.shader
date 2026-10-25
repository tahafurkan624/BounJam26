// Full-screen scene change: the game's turquoise liquid rises over the screen, the next scene loads behind it, and it
// drains away. Drawn by a full-screen uGUI graphic on an overlay canvas over everything else (ScreenWipe).
//   _Level      height of the surface in screen space, 0 = bottom edge, 1 = top edge (the waves reach past it)
//   _WipeTime   seconds since the wipe started (unscaled), for the waves
// Just under the surface the liquid is clear and bends the picture behind it (_GlassSceneTexture, from
// GlassSceneCapture); deeper down it turns solid colour. A bright foam line runs along the surface.
Shader "BounJam/UI/Liquid Wipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Level ("Surface Level", Float) = 0
        _WipeTime ("Time", Float) = 0
        [HDR] _Shallow ("Shallow Colour", Color) = (0.54, 0.97, 0.89, 1)
        _Deep ("Deep Colour", Color) = (0.02, 0.42, 0.45, 1)
        _Clear ("Clear Depth (share of the screen)", Float) = 0.22
        _Wave ("Wave Height (share of the screen)", Float) = 0.035

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
            Name "LiquidWipe"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_GlassSceneTexture);
            SAMPLER(sampler_GlassSceneTexture);
            float4 _GlassScreenParams; // width, height, 1/width, 1/height

            CBUFFER_START(UnityPerMaterial)
                float _Level;
                float _WipeTime;
                float4 _Shallow;
                float4 _Deep;
                float _Clear;
                float _Wave;
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

            half4 frag(Varyings i) : SV_Target
            {
                float t = _WipeTime;
                float2 uv = i.positionCS.xy * _GlassScreenParams.zw;
                #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
                #endif
                float aspect = _GlassScreenParams.x * _GlassScreenParams.w;
                float x = uv.x * aspect;

                // A sloshing surface: two travelling waves and a slow tilt.
                float wave = sin(x * 5.0 + t * 4.2) * 0.55 + sin(x * 11.0 - t * 6.3) * 0.25 + sin(x * 2.1 - t * 1.7) * 0.6;
                float surface = _Level + wave * _Wave;
                float depth = surface - uv.y; // > 0 under the surface
                float aa = max(fwidth(depth), 1e-4);
                float inside = saturate(depth / aa + 0.5);
                if (inside <= 0.0) return half4(0, 0, 0, 0);

                // Under the surface the picture is bent by the moving water and tinted.
                float2 bend = (float2(Noise(uv * 9.0 + float2(t * 0.8, t * 1.4)), Noise(uv * 9.0 - float2(t * 1.2, t * 0.6) + 23.0)) - 0.5) * 0.03;
                float3 scene = SAMPLE_TEXTURE2D_LOD(_GlassSceneTexture, sampler_GlassSceneTexture, saturate(uv + bend), 0).rgb;
                float clear = saturate(1.0 - depth / max(_Clear, 1e-3));
                float3 shallow = lerp(_Shallow.rgb, scene * _Shallow.rgb * 1.2, clear * 0.55);
                float3 col = lerp(_Deep.rgb, shallow, saturate(1.0 - depth * 1.6));

                // Caustic shimmer and a few rising bubbles.
                float caustic = Noise(uv * float2(aspect, 1.0) * 14.0 + float2(t * 0.9, -t * 1.6));
                col += smoothstep(0.62, 0.95, caustic) * 0.12 * saturate(1.0 - depth * 2.5);
                float2 cell = float2(floor(x * 9.0), 0.0);
                float rise = frac(Hash(cell.x * 3.1) + t * (0.25 + Hash(cell.x * 7.7) * 0.35));
                float2 bubble = float2((cell.x + 0.5 + (Hash(cell.x) - 0.5) * 0.6) / 9.0 / aspect, surface - (1.0 - rise) * 1.2);
                float2 toBubble = (uv - bubble) * float2(aspect, 1.0);
                float ring = abs(length(toBubble) - 0.012);
                col += (1.0 - smoothstep(0.0, 0.003, ring)) * 0.5 * step(0.0, surface - bubble.y);

                // The foam line along the surface.
                float foam = 1.0 - smoothstep(0.0, 0.012, depth);
                col = lerp(col, float3(0.93, 1.0, 0.98), foam * 0.85);

                return half4(col, inside * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
