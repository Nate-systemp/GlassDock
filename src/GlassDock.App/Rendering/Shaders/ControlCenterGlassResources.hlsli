// Shared Clear Control Center material resources. Recompile with Build-LiquidShader.ps1.
static const float GlassPanelEdgeWidth = 0.25;
static const float GlassCardEdgeWidth = 0.8;
static const float GlassCardHighlightOpacity = .2;
static const float GlassInnerGlowRadius = 20.0;
static const float GlassInnerGlowOpacity = .12;
static const float GlassPanelGlowRadius = 36.0;
static const float GlassPanelGlowStrength = 1.4;
static const float2 GlassCardFill = float2(.10, .06);
static const float3 GlassPanelTint = float3(.16, .035, .045);
static const float GlassPanelTintOpacity = .02;

float GlassEdgeBrushA(float t)
{
    if (t < .25) return lerp(.60, .10, t / .25);
    if (t < .50) return lerp(.10, .04, (t - .25) / .25);
    if (t < .75) return lerp(.04, .10, (t - .50) / .25);
    return lerp(.10, .40, (t - .75) / .25);
}
float GlassEdgeBrushB(float t)
{
    if (t < .30) return lerp(.25, .03, t / .30);
    if (t < .70) return .03;
    return lerp(.03, .20, (t - .70) / .30);
}
float GlassInnerGlowBrush(float2 local, float2 size, float radius)
{
    float2 inset = min(radius * .3, min(size.x, size.y) * .25);
    float a = 1 - smoothstep(0, radius, length(local - inset));
    float b = 1 - smoothstep(0, radius, length(local - (size - inset)));
    return saturate(a + b) * GlassInnerGlowOpacity;
}
