using System.Numerics;

namespace GlassDock.App.Rendering;

internal readonly record struct PopupCompositionFrame(Matrix4x4 Transform, float Opacity);

// Two API projections own different compositors. Both consume the exact same
// precomputed samples; interpolation runs entirely on the composition thread.
internal sealed class PopupCompositionTrack : IDisposable
{
    private readonly bool animateOpacity;
    private readonly Microsoft.UI.Composition.Visual visual;
    private readonly Microsoft.UI.Composition.CompositionPropertySet values;
    private readonly Microsoft.UI.Composition.Vector4KeyFrameAnimation a, b;
    private readonly Microsoft.UI.Composition.ExpressionAnimation matrix, opacity;
    private readonly Microsoft.UI.Composition.LinearEasingFunction linear;
    public PopupCompositionTrack(Microsoft.UI.Composition.Visual visual, bool animateOpacity = true)
    {
        this.visual = visual;
        this.animateOpacity = animateOpacity;
        var compositor = visual.Compositor;
        values = compositor.CreatePropertySet();
        values.InsertVector4("A", new Vector4(1, 0, 1, 0));
        values.InsertVector4("B", new Vector4(0, 0, 1, 1));
        matrix = compositor.CreateExpressionAnimation("Matrix4x4(v.A.X,0,0,0,v.A.Y,v.A.Z,0,v.A.W,0,0,1,0,v.B.X,v.B.Y,0,v.B.Z)");
        matrix.SetReferenceParameter("v", values);
        opacity = compositor.CreateExpressionAnimation("v.B.W");
        opacity.SetReferenceParameter("v", values);
        a = compositor.CreateVector4KeyFrameAnimation();
        b = compositor.CreateVector4KeyFrameAnimation();
        linear = compositor.CreateLinearEasingFunction();
        visual.StartAnimation("TransformMatrix", matrix);
        if (animateOpacity) visual.StartAnimation("Opacity", opacity);
    }
    public void Bind()
    {
        visual.StartAnimation("TransformMatrix", matrix);
        if (animateOpacity) visual.StartAnimation("Opacity", opacity);
    }
    public void Start(PopupCompositionFrame[] frames, TimeSpan duration, float units = 1)
    {
        a.Duration = b.Duration = duration;
        for (var i = 0; i < frames.Length; i++)
        {
            var m = Matrix4x4.CreateScale(1 / units, 1 / units, 1) * frames[i].Transform * Matrix4x4.CreateScale(units, units, 1);
            var t = (float)i / (frames.Length - 1);
            a.InsertKeyFrame(t, new Vector4(m.M11, m.M21, m.M22, m.M24), linear);
            b.InsertKeyFrame(t, new Vector4(m.M41, m.M42, m.M44, frames[i].Opacity), linear);
        }
        values.StartAnimation("A", a);
        values.StartAnimation("B", b);
    }
    public void Stop()
    {
        visual.StopAnimation("TransformMatrix");
        visual.StopAnimation("Opacity");
        values.StopAnimation("A"); values.StopAnimation("B");
    }
    public void Dispose()
    {
        Stop();
        matrix.Dispose(); opacity.Dispose(); a.Dispose(); b.Dispose(); linear.Dispose(); values.Dispose();
    }
}
// Two API projections own different compositors. Both consume the exact same
// precomputed samples; interpolation runs entirely on the composition thread.
internal sealed class BackdropCompositionTrack : IDisposable
{
    private readonly bool animateOpacity;
    private readonly global::Windows.UI.Composition.Visual visual;
    private readonly global::Windows.UI.Composition.CompositionPropertySet values;
    private readonly global::Windows.UI.Composition.Vector4KeyFrameAnimation a, b;
    private readonly global::Windows.UI.Composition.ExpressionAnimation matrix, opacity;
    private readonly global::Windows.UI.Composition.LinearEasingFunction linear;
    public BackdropCompositionTrack(global::Windows.UI.Composition.Visual visual, bool animateOpacity = true)
    {
        this.visual = visual;
        this.animateOpacity = animateOpacity;
        var compositor = visual.Compositor;
        values = compositor.CreatePropertySet();
        values.InsertVector4("A", new Vector4(1, 0, 1, 0));
        values.InsertVector4("B", new Vector4(0, 0, 1, 1));
        matrix = compositor.CreateExpressionAnimation("Matrix4x4(v.A.X,0,0,0,v.A.Y,v.A.Z,0,v.A.W,0,0,1,0,v.B.X,v.B.Y,0,v.B.Z)");
        matrix.SetReferenceParameter("v", values);
        opacity = compositor.CreateExpressionAnimation("v.B.W");
        opacity.SetReferenceParameter("v", values);
        a = compositor.CreateVector4KeyFrameAnimation();
        b = compositor.CreateVector4KeyFrameAnimation();
        linear = compositor.CreateLinearEasingFunction();
        visual.StartAnimation("TransformMatrix", matrix);
        if (animateOpacity) visual.StartAnimation("Opacity", opacity);
    }
    public void Bind()
    {
        visual.StartAnimation("TransformMatrix", matrix);
        if (animateOpacity) visual.StartAnimation("Opacity", opacity);
    }
    public void Start(PopupCompositionFrame[] frames, TimeSpan duration, float units = 1)
    {
        a.Duration = b.Duration = duration;
        for (var i = 0; i < frames.Length; i++)
        {
            var m = Matrix4x4.CreateScale(1 / units, 1 / units, 1) * frames[i].Transform * Matrix4x4.CreateScale(units, units, 1);
            var t = (float)i / (frames.Length - 1);
            a.InsertKeyFrame(t, new Vector4(m.M11, m.M21, m.M22, m.M24), linear);
            b.InsertKeyFrame(t, new Vector4(m.M41, m.M42, m.M44, frames[i].Opacity), linear);
        }
        values.StartAnimation("A", a);
        values.StartAnimation("B", b);
    }
    public void Stop()
    {
        visual.StopAnimation("TransformMatrix");
        visual.StopAnimation("Opacity");
        values.StopAnimation("A"); values.StopAnimation("B");
    }
    public void Dispose()
    {
        Stop();
        matrix.Dispose(); opacity.Dispose(); a.Dispose(); b.Dispose(); linear.Dispose(); values.Dispose();
    }
}