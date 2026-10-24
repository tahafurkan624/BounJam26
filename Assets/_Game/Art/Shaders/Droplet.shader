// Water thrown off the liquid blob: droplets in flight, and the splat rings they leave where they land. Drawn on
// particle billboards, but the vertex shader builds every quad itself from the particle's centre, size and velocity,
// so the shape never depends on how the particle renderer orients its quads:
// - droplet: a round drop facing the camera that stretches back along its motion into a teardrop streak. It never
//   gets thinner than a couple of pixels (smaller ones are drawn that big but fainter), so it doesn't flicker.
// - splat (_Splat on): a ring lying flat on the surface the drop hit (the particle's tiny velocity is that surface's
//   normal). It bursts outward, thins and fades, and darkens a wet patch inside itself for a moment.
// Both are little lenses over the scene behind (URP opaque texture), tinted like the blob's water and darker at the edges so
// they read against the light floor, with a wet highlight that stays at least a pixel big. Queued after the jelly,
// which writes depth, and depth tested, so they never draw over the blob or the ship from behind.
//
// Vertex streams the particle renderers must send, in this order (SplashKit sets them up):
//   Position, Color, UV, AgePercent, StableRandom.x, Center, Size.x, Velocity
//   -> TEXCOORD0 = (corner uv, age, random), TEXCOORD1 = (centre, size), TEXCOORD2 = velocity
Shader "BounJam/Droplet"
{
    Properties
    {
        [ToggleUI] _Splat ("Splat Ring (lies on the surface it hit)", Float) = 0

        [Header(Colour)]
        _ShallowColor ("Through Tint (middle)", Color) = (0.62, 0.9, 0.95, 1)
        _DeepColor ("Edge Tint (dark rim)", Color) = (0.05, 0.36, 0.45, 1)
        _BodyColor ("Body Colour (lit)", Color) = (0.24, 0.62, 0.70, 1)
        _Opacity ("Body Opacity (middle)", Range(0, 1)) = 0.3
        _EdgeDarkness ("Edge Darkness", Range(0, 1)) = 0.8
        _CenterAlpha ("Alpha (middle)", Range(0, 1)) = 0.8
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.6

        [Header(Refraction)]
        _Refraction ("Lens Strength (radii)", Range(0, 4)) = 1.5

        [Header(Highlight)]
        _Shininess ("Highlight Sharpness (close up)", Range(8, 512)) = 140
        _Specular ("Highlight Strength", Range(0, 4)) = 1.6
        _GlintPixels ("Smallest Highlight (px)", Range(0, 3)) = 0.8
        _Reflection ("Reflection Strength", Range(0, 2)) = 0.6
        _RimColor ("Rim Colour", Color) = (0.85, 0.97, 1.0, 1)
        _RimPower ("Rim Tightness", Range(0.5, 8)) = 4
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.35

        [Header(Droplet)]
        _Stretch ("Streak Length (s of motion)", Range(0, 0.1)) = 0.03
        _MaxStretch ("Longest Streak (m)", Range(0, 1)) = 0.3
        _MinPixelRadius ("Smallest Radius (px)", Range(0, 4)) = 1.6

        [Header(Splat)]
        _RingWidth ("Ring Width (share of radius)", Range(0.02, 0.5)) = 0.2
        _WetOpacity ("Wet Patch Opacity", Range(0, 1)) = 0.3
        _SplatDepthFade ("Overhang Fade (m)", Range(0.005, 0.5)) = 0.03
    }

    SubShader
    {
        // After the jelly (plain Transparent), which writes depth: drops behind the blob or the ship fail the depth
        // test instead of being painted over them, and drops in front are drawn over the finished jelly. Also after
        // the water (Transparent+50), which doesn't write depth and would otherwise paint over drops above it.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "ForwardDroplet"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            // Nine interpolators: more than the default target allows.
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            // Forward+ leaves the per-object light data at zero; without this keyword the sun never reaches the drop.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Splat;
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _BodyColor;
                half _Opacity;
                half _EdgeDarkness;
                half _CenterAlpha;
                half _ShadowStrength;
                float _Refraction;
                half _Shininess;
                half _Specular;
                float _GlintPixels;
                half _Reflection;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                float _Stretch;
                float _MaxStretch;
                float _MinPixelRadius;
                float _RingWidth;
                half _WetOpacity;
                float _SplatDepthFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // the renderer's own billboard corner; unused, the quad is rebuilt below
                half4 color : COLOR;
                float4 uvAgeRandom : TEXCOORD0; // xy corner (0..1), z age (share of lifetime), w stable random 0..1
                float4 centerSize : TEXCOORD1;  // xyz particle centre, w size (m)
                float3 velocity : TEXCOORD2;    // m/s; a splat's carries its surface normal
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 shape : TEXCOORD0;       // xy point in the shape's own space (radius 1), z tail length, w age
                float3 positionWS : TEXCOORD1;
                float3 centerWS : TEXCOORD2;
                float3 axisX : TEXCOORD3;       // world directions of the shape's axes: z faces the camera (droplet)
                float3 axisY : TEXCOORD4;       // or points out of the surface (splat)
                float3 axisZ : TEXCOORD5;
                float4 screen : TEXCOORD6;      // xy radius in screen uv, z shape units per pixel, w stable random
                float2 depthFog : TEXCOORD7;    // x eye depth, y fog factor
            };

            float TailRadius(float tail)
            {
                return lerp(0.6, 0.3, saturate(tail * 0.25));
            }

            // Signed distance to a teardrop, in head radii: a round head of radius 1 at the origin running back (-y)
            // into a tail `tail` radii long that thins to a rounded end (an uneven capsule).
            float Teardrop(float2 p, float tail)
            {
                float r2 = TailRadius(tail);
                float h = 1.0 + tail - r2;
                p = float2(abs(p.x), -p.y);
                float b = min((1.0 - r2) / h, 0.999);
                float a = sqrt(1.0 - b * b);
                float k = dot(p, float2(-b, a));
                if (k < 0.0) return length(p) - 1.0;
                if (k > a * h) return length(p - float2(0.0, h)) - r2;
                return dot(p, float2(a, b)) - 1.0;
            }

            Varyings Vert(Attributes input)
            {
                float3 center = TransformObjectToWorld(input.centerSize.xyz);
                float3 velocity = TransformObjectToWorldDir(input.velocity, false);
                float2 corner = input.uvAgeRandom.xy * 2.0 - 1.0;
                float radius = input.centerSize.w * 0.5;

                // Pixels per metre at the particle: keeps drops a couple of pixels wide and each lens to its own size.
                float pixelsPerMetre = _ScaledScreenParams.y * 0.5 * abs(UNITY_MATRIX_P[1][1]);
                if (IsPerspectiveProjection()) pixelsPerMetre /= max(-TransformWorldToView(center).z, 0.01);

                float3 axisX, axisY, axisZ;
                float2 shape;
                float tail = 0.0;
                half fade = 1.0h;
                if (_Splat > 0.5)
                {
                    // Flat on the surface it hit: the particle's (barely moving) velocity points out of that surface.
                    // Every ring is turned at random so their lumps don't line up.
                    float normalLength = length(velocity);
                    axisZ = normalLength > 1e-7 ? velocity / normalLength : float3(0.0, 1.0, 0.0);
                    float3 helper = abs(axisZ.y) < 0.99 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                    float3 tangent = normalize(cross(helper, axisZ));
                    float3 bitangent = cross(axisZ, tangent);
                    float s, c;
                    sincos(input.uvAgeRandom.w * TWO_PI, s, c);
                    axisX = tangent * c + bitangent * s;
                    axisY = cross(axisZ, axisX);
                    shape = corner;
                }
                else
                {
                    // Facing the camera, the tail trailing back along the motion as seen on screen.
                    axisZ = normalize(GetWorldSpaceViewDir(center));
                    float3 screenVelocity = velocity - axisZ * dot(velocity, axisZ);
                    float speed = length(screenVelocity);
                    float3 cameraUp = GetWorldToViewMatrix()[1].xyz;
                    axisY = speed > 1e-4 ? screenVelocity / speed : normalize(cameraUp - axisZ * dot(cameraUp, axisZ));
                    axisX = cross(axisY, axisZ);

                    // Never thinner than a couple of pixels: smaller drops are drawn that big but fainter, so they
                    // don't flicker in and out as they cross pixels.
                    float grow = max(1.0, _MinPixelRadius / max(radius * pixelsPerMetre, 1e-4));
                    radius *= grow;
                    fade = (half)(1.0 / grow);
                    tail = min(speed * _Stretch, _MaxStretch) / radius;
                    // One pixel of margin around the shape for its soft edge.
                    float margin = 1.0 + 1.0 / max(radius * pixelsPerMetre, 1.0);
                    shape = float2(corner.x * margin, corner.y > 0.0 ? margin : -(margin + tail));
                }

                float3 positionWS = center + (axisX * shape.x + axisY * shape.y) * radius;
                float pixelRadius = radius * pixelsPerMetre;

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = half4(input.color.rgb, input.color.a * fade);
                output.shape = float4(shape, tail, input.uvAgeRandom.z);
                output.positionWS = positionWS;
                output.centerWS = center;
                output.axisX = axisX;
                output.axisY = axisY;
                output.axisZ = axisZ;
                output.screen = float4(pixelRadius / _ScaledScreenParams.xy, 1.0 / max(pixelRadius, 1e-4), input.uvAgeRandom.w);
                output.depthFog = float2(-TransformWorldToView(positionWS).z, ComputeFogFactor(output.positionCS.z));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.shape.xy;
                float age = input.shape.w;
                float pixelRadius = 1.0 / input.screen.z;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                float3 n = float3(0.0, 0.0, 1.0);  // normal in the shape's own space: xy along it, z out of it
                half coverage = 0.0h;               // how much of this pixel the drop or ring covers
                half wet = 0.0h;                    // splat: the wet patch inside the ring
                half fade = 1.0h;
                float lensRadius = 1.0;             // width of the bulge that bends the view, in shape units

                if (_Splat > 0.5)
                {
                    float r = length(p);
                    float pixel = max(fwidth(r), 1e-4);
                    // Bursts out, then slows; a lumpy crown, thicker in a few places that differ for every splat.
                    float rest = saturate(1.0 - age);
                    float spread = 1.0 - rest * rest * rest;
                    float ringRadius = lerp(0.3, 0.92, spread);
                    float angle = atan2(p.y, p.x);
                    float seed = input.screen.w * TWO_PI;
                    float lumps = 0.7 + 0.3 * sin(angle * 5.0 + seed) * sin(angle * 3.0 - seed * 1.7);
                    float halfWidth = _RingWidth * lumps * lerp(1.0, 0.4, spread);
                    float offset = r - ringRadius;
                    coverage = (half)saturate((halfWidth - abs(offset)) / pixel + 0.5);
                    // Round in cross-section, like a tube of water lying on the floor. (Any direction will do at the
                    // very centre, but not none: a zero normal turns the whole pixel into NaN.)
                    float across = clamp(offset / halfWidth, -1.0, 1.0);
                    float2 outward = r > 1e-4 ? p / r : float2(1.0, 0.0);
                    n = float3(outward * across, sqrt(saturate(1.0 - across * across)));
                    lensRadius = halfWidth;
                    wet = (half)(saturate((ringRadius - halfWidth - r) / pixel + 0.5) * rest) * _WetOpacity;

                    // Where it hangs out over an edge there's nothing right behind it: fade that part out.
                    float rawDepth = SampleSceneDepth(screenUV);
                    float sceneDepth = IsPerspectiveProjection() ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
                    fade = (half)saturate(2.0 - (sceneDepth - input.depthFog.x) / _SplatDepthFade);
                    coverage *= (half)(rest * sqrt(rest));
                }
                else
                {
                    float tail = input.shape.z;
                    float d = Teardrop(p, tail);
                    coverage = (half)saturate(0.5 - d * pixelRadius);
                    // In-plane outward direction from the distance field; across it the drop is round like a tube,
                    // and a sphere at the head.
                    const float e = 0.02;
                    float2 g = float2(Teardrop(p + float2(e, 0.0), tail), Teardrop(p + float2(0.0, e), tail)) - d;
                    g *= rsqrt(max(dot(g, g), 1e-10));
                    float halfWidth = p.y >= 0.0 ? 1.0 : lerp(1.0, TailRadius(tail), saturate(-p.y / (1.0 + tail)));
                    float toEdge = 1.0 - saturate(-d / halfWidth);
                    n = float3(g * toEdge, sqrt(saturate(1.0 - toEdge * toEdge)));
                    // Gone at the end of its life if it never hit anything.
                    fade = (half)saturate((1.0 - age) * 8.0);
                }
                clip(max(coverage, wet) - 0.001h);

                float3 normalWS = normalize(input.axisX * n.x + input.axisY * n.y + input.axisZ * n.z);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half edge = 1.0h - (half)n.z;       // 0 in the thick middle, 1 at the rim

                // A drop is a strong little lens: what's behind shows up shrunk and flipped, from a few radii around it.
                float3 normalVS = TransformWorldToViewNormal(normalWS);
                half3 behind = SampleSceneColor(screenUV - normalVS.xy * input.screen.xy * lensRadius * _Refraction);

                // Lit at its centre: a drop is too small to shadow itself, and light gets through water anyway.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.centerWS), input.centerWS, half4(1, 1, 1, 1));
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength) * mainLight.distanceAttenuation;
                half wrapped = saturate((dot(normalWS, mainLight.direction) + 0.5h) / 1.5h);
                half3 lit = SampleSH(normalWS) + mainLight.color * wrapped * shadow;

                // Light through the middle is only tinted; the rim bends the darker surroundings in instead of
                // catching the sun, which outlines the drop against the light floor.
                half3 color = behind * _ShallowColor.rgb;
                color = lerp(color, _BodyColor.rgb * lit, _Opacity);
                half3 rimShade = _DeepColor.rgb * (SampleSH(normalWS) + mainLight.color * 0.25h * shadow);
                color = lerp(color, rimShade, _EdgeDarkness * smoothstep(0.3h, 1.0h, edge));

                half fresnel = pow(edge, _RimPower);
                // The screen-uv overload is the one that exists with and without Forward+.
                half3 reflection = GlossyEnvironmentReflection(reflect(-viewDir, normalWS), input.positionWS, 0.05h, 1.0h,
                    GetNormalizedScreenSpaceUV(input.positionCS));
                color += reflection * fresnel * _Reflection + _RimColor.rgb * fresnel * _RimStrength;

                // Sharp close up; on a drop a few pixels wide it widens so the glint still covers about a pixel. At
                // least 1: pow(0, 0) is NaN, and a NaN pixel turns into a black square under bloom.
                float spot = saturate(_GlintPixels / pixelRadius);
                float spotCos = clamp(sqrt(1.0 - spot * spot), 0.05, 0.9999);
                half sharpness = (half)clamp(-0.693 / log(spotCos), 1.0, (float)_Shininess);
                half3 halfDir = normalize(mainLight.direction + viewDir);
                color += mainLight.color * pow(saturate(dot(normalWS, halfDir)), sharpness) * _Specular * shadow;

                half alpha = coverage * lerp(_CenterAlpha, 1.0h, edge);
                if (_Splat > 0.5)
                {
                    // The wet patch: the floor straight behind, darkened and tinted.
                    half3 wetColor = SampleSceneColor(screenUV) * _DeepColor.rgb * 1.5h;
                    color = lerp(wetColor, color, coverage);
                    alpha = max(alpha, wet);
                }
                alpha *= fade * input.color.a;
                color *= input.color.rgb;
                return half4(MixFog(color, input.depthFog.y), alpha);
            }
            ENDHLSL
        }
    }
}
