using System.Numerics;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using Microsoft.UI.Xaml;

namespace GlassDock.App.Controls;

internal interface IUtilityPanel : IDisposable
{
    FrameworkElement View { get; }
    IReadOnlyList<UtilityGlassSurface> GlassSurfaces { get; }
    int GlassTargetsRevision { get; }
    event EventHandler? GlassChanged;
    Task RefreshAsync();
    void SetAvailableHeight(double height);
    void ConfigureGlass(GlassMaterial material, LiquidGlassMaterial optics, float scale, bool highlights);
    bool PositionGlass(FrameworkElement relativeTo, Vector2 origin, bool active);
}
