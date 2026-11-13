#define D2D_INPUT_COUNT 1
#define D2D_INPUT0_COMPLEX
#define D2D_REQUIRES_SCENE_POSITION
#include "d2d1effecthelpers.hlsli"

float4 Dock;
float4 Wave;
float4 Optics;
float4 Lighting;
float2 FrameSize;
float4 Lens;
float4 Surface;
float4 Profile;
float4 Reflections;

float distanceToGlass(float2 p)
{
    float u = (p.x - Wave.x) / max(1, Wave.y);
    float q = saturate(1 - u * u);
    float rise = Wave.z * q * q * q;
    float2 halfSize = Dock.zw * .5;
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
    float profile = saturate(1 + dist / max(1, Optics.y));
    profile = lerp(profile, smoothstep(0, 1, profile), Profile.w);
    float edge = pow(saturate(profile), Optics.z);
    edge = saturate(edge + (Profile.x - 1) * .2 * (1 - edge) * saturate(-dist / max(1, Dock.w * .5)));
    float2 center = Dock.xy + Dock.zw * .5;
    float2 sampleAt = clamp(center + (p - center) / (1 + Lens.x + Profile.y) - normal * Optics.x * lerp(1, edge, Profile.z), .5, FrameSize - .5);
    float d = Optics.w;
    float4 core = D2DSampleInputAtPosition(0, sampleAt);
    float dispersion = Lens.y * edge * edge * saturate(2 * Optics.x / Optics.y);
    [branch] if (dispersion > 0)
    {
        float2 split = normal * dispersion;
        core.r = D2DSampleInputAtPosition(0, clamp(sampleAt + split, .5, FrameSize - .5)).r;
        core.b = D2DSampleInputAtPosition(0, clamp(sampleAt - split, .5, FrameSize - .5)).b;
    }
    float4 color = core * .6;
    color += D2DSampleInputAtPosition(0, clamp(sampleAt + float2(d, 0), .5, FrameSize - .5)) * .1;
    color += D2DSampleInputAtPosition(0, clamp(sampleAt - float2(d, 0), .5, FrameSize - .5)) * .1;
    color += D2DSampleInputAtPosition(0, clamp(sampleAt + float2(0, d), .5, FrameSize - .5)) * .1;
    color += D2DSampleInputAtPosition(0, clamp(sampleAt - float2(0, d), .5, FrameSize - .5)) * .1;
    float luminance = dot(color.rgb, float3(.2126, .7152, .0722));
    float3 tint = lerp(float3(.65, .76, .9), float3(.92, .96, 1), luminance);
    color.rgb = lerp(color.rgb, tint, Lighting.x);
    float gray = dot(color.rgb, float3(.2126, .7152, .0722));
    color.rgb = lerp(gray.xxx, color.rgb, Surface.x);
    color.rgb = saturate((color.rgb * Surface.y - .5) * Surface.z + .5);
    float2 direction = float2(-cos(Lighting.w), -sin(Lighting.w));
    float directional = dot(normal, direction);
    float axis = dot((p - center) / max(Dock.zw, 1), direction);
    color.rgb += Reflections.x * exp(-pow((axis - .15) / Reflections.y, 2));
    color.rgb += Reflections.z * directional * edge * (1 - edge) * .35;
    color.rgb = lerp(D2DSampleInputAtPosition(0, p).rgb, color.rgb, Surface.w);
    float light = pow(saturate(dot(normal, normalize(float2(-.45, -1)))), Lighting.z);
    if (Lens.z > .5) color.rgb += light * pow(edge, 3) * Lighting.y;
    color.a = 1;
    if (Lens.z > .5)
    {
        color *= saturate(.5 - dist) * Lens.w;
    }
    color *= Reflections.w;
    return color;
}
