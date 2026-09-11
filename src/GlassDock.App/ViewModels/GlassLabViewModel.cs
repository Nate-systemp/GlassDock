using GlassDock.Core.Materials;

namespace GlassDock.App.ViewModels;

/// <summary>Session-only laboratory state. No persistence or product settings.</summary>
public sealed class GlassLabViewModel
{
    public GlassMaterial Material { get; private set; } = GlassMaterialPresets.Create(GlassMaterialPreset.Frosted);
    public string PresetName { get; private set; } = "Frosted";
    public event EventHandler<bool>? MaterialChanged;

    public void SelectPreset(GlassMaterialPreset preset)
    {
        Material = GlassMaterialPresets.Create(preset);
        PresetName = preset.ToString();
        MaterialChanged?.Invoke(this, true);
    }

    public void Update(GlassMaterial material)
    {
        Material = material.Normalize();
        PresetName = "Custom";
        MaterialChanged?.Invoke(this, false);
    }
}
