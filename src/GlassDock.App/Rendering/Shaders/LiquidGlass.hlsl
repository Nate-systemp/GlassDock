#define D2D_INPUT_COUNT 1
#define D2D_INPUT0_COMPLEX
#define D2D_REQUIRES_SCENE_POSITION
#include "d2d1effecthelpers.hlsli"
#include "ControlCenterGlassResources.hlsli"

float4 Dock;       // left, top, width, height in monitor pixels
float4 Wave;       // center x, half width, amplitude, corner radius
float4 Optics;     // refraction pixels, edge width pixels, falloff, diffusion pixels
float4 Lighting;   // tint opacity, specular intensity, falloff, highlight angle radians
float2 FrameSize;
float4 Lens; // magnification, edge dispersion pixels, local capsule clip enabled, opacity
// Opt-in Control Center material. Zero preserves the dock/drag-lens shader.
float4 Surface; // 1 panel / 2 raised card, pointer xy in monitor pixels, pointer influence

float distanceToGlass(float2 p)
{
    float u = (p.x - Wave.x) / max(1, Wave.y);
    float q = saturate(1 - u * u);
    float rise = Wave.z * q * q * q;
    float2 halfSize = Dock.zw * .5;
    // The top follows the same C2 wave as DockWaveGeometry. Bottom stays fixed.
    float2 center = Dock.xy + halfSize - float2(0, rise * .5);
    halfSize.y += rise * .5;
    float2 d = abs(p - center) - halfSize + Wave.w;
    return length(max(d, 0)) + min(max(d.x, d.y), 0) - Wave.w;
}

D2D_PS_ENTRY(main)
{
    float2 p = D2DGetScenePosition().xy;
    float dist = distanceToGlass(p);
    float2 normal = float2(distanceToGlass(p + float2(.5, 0)) - distanceToGlass(p - float2(.5, 0)),
                           distanceToGlass(p + float2(0, .5)) - distanceToGlass(p - float2(0, .5)));
    normal /= max(length(normal), .001);
    float edge = pow(saturate(1 + dist / Optics.y), Optics.z);
    float2 center = Dock.xy + Dock.zw * .5;
    float2 sampleAt = clamp(center + (p - center) / (1 + Lens.x) - normal * Optics.x * (.025 + .975 * edge), .5, FrameSize - .5);
    float3 surfaceNormal = float3(0, 0, 1);
    float bevel = 0;
    [branch] if (Surface.x > .5)
    {
        // A circular bevel supplies a continuous 3D normal. The flat interior
        // has no lateral displacement; all geometry is in physical pixels.
        float width = max(1, min(Optics.y, min(Dock.z, Dock.w) * .45));
        bevel = saturate(1 + dist / width);
        float angle = bevel * 1.5707963;
        surfaceNormal = float3(normal * sin(angle), cos(angle));
        edge = pow(bevel, Optics.z);
        float3 transmitted = refract(float3(0, 0, -1), surfaceNormal, 1.0 / 1.46);
        float2 bend = transmitted.xy / max(.25, -transmitted.z);
        sampleAt = clamp(p + bend * Optics.x * (bevel * bevel), .5, FrameSize - .5);
    }
    float d = Optics.w;
    float4 core = D2DSampleInputAtPosition(0, sampleAt);
    // Wavelength displacement follows the same wave-aware optical normal.
    // The strength/edge-width ratio is DPI invariant and vanishes at zero refraction.
    float dispersion = Lens.y * edge * edge * saturate(2 * Optics.x / Optics.y);
    [branch] if (dispersion > 0)
    {
        float2 split = normal * dispersion;
        core.r = D2DSampleInputAtPosition(0, clamp(sampleAt + split, .5, FrameSize - .5)).r;
        core.b = D2DSampleInputAtPosition(0, clamp(sampleAt - split, .5, FrameSize - .5)).b;
    }
    // Disperse the dominant tap, retaining the neutral diffusion halo for readability.
    float4 color = core * .6;
    color += D2DSampleInputAtPosition(0, clamp(sampleAt + float2(d, 0), .5, FrameSize - .5)) * .1;
    color += D2DSampleInputAtPosition(0, clamp(sampleAt - float2(d, 0), .5, FrameSize - .5)) * .1;
    color += D2DSampleInputAtPosition(0, clamp(sampleAt + float2(0, d), .5, FrameSize - .5)) * .1;
    color += D2DSampleInputAtPosition(0, clamp(sampleAt - float2(0, d), .5, FrameSize - .5)) * .1;
    float luminance = dot(color.rgb, float3(.2126, .7152, .0722));
    [branch] if (Surface.x > .5)
    {
        // Two diagonal gradient layers on ONE continuous rounded contour.
        // distanceToGlass uses the same radius and bounds as the surface clip.
        float2 uv = saturate((p - Dock.xy) / Dock.zw);
        float edgeWidth = Surface.x > 1.5 ? GlassCardEdgeWidth : GlassPanelEdgeWidth;
        float ring = 1 - smoothstep(edgeWidth - .5, edgeWidth + .5, -dist);
        float a = GlassEdgeBrushA((uv.x + uv.y) * .5) * ring;
        float b = GlassEdgeBrushB((1 - uv.x + uv.y) * .5) * ring;
        float edgeOpacity = 1 - (1 - a) * (1 - b);
        float highlightOpacity = Surface.x > 1.5 ? GlassCardHighlightOpacity : 1;
        edgeOpacity *= highlightOpacity;
        // Retain wallpaper color while gently limiting bright-scene glare.
        float sceneLuminance = dot(color.rgb, float3(.2126, .7152, .0722));
        float tint = GlassPanelTintOpacity + smoothstep(.55, .95, sceneLuminance) * .18;
        color.rgb = lerp(color.rgb, GlassPanelTint, tint);
        if (Surface.x > 1.5)
            color.rgb = lerp(color.rgb, float3(1, 1, 1), lerp(GlassCardFill.x, GlassCardFill.y, uv.y));
        float glow = GlassInnerGlowBrush(p - Dock.xy, Dock.zw, GlassInnerGlowRadius) * highlightOpacity;
        if (Surface.x < 1.5)
        {
            float2 local = p - Dock.xy;
            glow = GlassInnerGlowBrush(local, Dock.zw, GlassPanelGlowRadius) * GlassPanelGlowStrength;
            glow += GlassInnerGlowBrush(float2(Dock.z - local.x, local.y), Dock.zw, GlassPanelGlowRadius) * .65;
        }
        color.rgb = lerp(color.rgb, float3(1, 1, 1), glow);
        color.rgb = lerp(color.rgb, float3(1, 1, 1), edgeOpacity);
        // 5. Antialiased subpixel clipping for cards, without bottom drop shadows
        float coverage = Surface.x > 1.5 ? saturate(.5 - dist) : 1;
        color = float4(color.rgb * coverage, coverage);
    }
    else
    {
    float3 tint = lerp(float3(.65, .76, .9), float3(.92, .96, 1), luminance);
    color.rgb = lerp(color.rgb, tint, Lighting.x);
    float light = pow(saturate(dot(normal, normalize(float2(-.45, -1)))), Lighting.z);
    if (Lens.z > .5) color.rgb += light * pow(edge, 3) * Lighting.y;
    // Main/popup specular is drawn by LiquidGlassRim on the exact retained
    // CanvasGeometry clip. Direct2D computes subpixel coverage along the Beziers.
    // The optical field here remains responsible only for refraction/dispersion.
    color.a = 1;
    if (Lens.z > .5)
    {
        color *= saturate(.5 - dist) * Lens.w;
    }
    }
    // Exact antialiased clipping is supplied by the dock's existing vector path.
    return color;
}
