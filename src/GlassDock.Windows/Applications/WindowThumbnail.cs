using System.Runtime.InteropServices;

using GlassDock.Core.Applications;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Applications;

/// <summary>
/// A DWM-managed live relationship.
/// No capture buffers or background frame loop.
/// </summary>
public sealed class WindowThumbnail : IDisposable
{
    private nint thumbnail;
    private readonly nint source;
    private readonly uint sourceProcess;

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Properties
    {
        public uint Flags;

        public NativeMethods.Rect Destination;
        public NativeMethods.Rect Source;

        public byte Opacity;

        public int Visible;
        public int ClientOnly;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(
        nint destination,
        nint source,
        out nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(
        nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(
        nint thumbnail,
        out Size size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(
        nint thumbnail,
        ref Properties properties);

    public WindowThumbnail(
        nint destination,
        ApplicationWindow window)
    {
        source = (nint)window.Handle;
        ApplicationNative.GetWindowThreadProcessId(source, out sourceProcess);
        if (WindowsApplicationService.IsEligible(window))
        {
            if (DwmRegisterThumbnail(
                destination,
                source,
                out thumbnail) < 0) thumbnail = 0;
        }
    }

    public bool Update(
        PreviewRect rect,
        double dpi,
        double opacity,
        bool visible)
    {
        if (!ValidateSource() || !double.IsFinite(dpi) || dpi <= 0 ||
            rect.Width <= 0 || rect.Height <= 0)
            return false;

        if (DwmQueryThumbnailSourceSize(
                thumbnail,
                out var size) < 0)
        {
            return false;
        }

        if (size.Width <= 0 ||
            size.Height <= 0)
        {
            return false;
        }

        var ratio = Math.Min(
            rect.Width * dpi / size.Width,
            rect.Height * dpi / size.Height);

        var width =
            size.Width * ratio;

        var height =
            size.Height * ratio;

        var x =
            rect.X * dpi +
            (
                rect.Width * dpi -
                width
            ) / 2;

        var y =
            rect.Y * dpi +
            (
                rect.Height * dpi -
                height
            ) / 2;

        var properties =
            new Properties
            {
                //
                // DWM_TNP_RECTDESTINATION
                // DWM_TNP_OPACITY
                // DWM_TNP_VISIBLE
                // DWM_TNP_SOURCECLIENTAREAONLY
                //
                Flags =
                    1 |
                    4 |
                    8 |
                    16,

                Opacity =
                    (byte)(
                        Math.Clamp(
                            opacity,
                            0,
                            1) * 255),

                Visible =
                    visible ? 1 : 0,

                ClientOnly = 0,

                Destination =
                    new NativeMethods.Rect
                    {
                        Left =
                            (int)Math.Round(x),

                        Top =
                            (int)Math.Round(y),

                        Right =
                            (int)Math.Round(
                                x + width),

                        Bottom =
                            (int)Math.Round(
                                y + height)
                    }
            };

        return DwmUpdateThumbnailProperties(
            thumbnail,
            ref properties) >= 0;
    }

    /// <summary>
    /// Full-size desktop mirror.
    ///
    /// Normal windows:
    /// trims invisible resize margins.
    ///
    /// Minimized windows:
    /// can disable source cropping so compositor-heavy
    /// apps such as Edge/OBS can use their complete DWM
    /// thumbnail surface.
    /// </summary>
    public bool UpdateDesktop(
        PreviewRect frame,
        PreviewRect windowBounds,
        bool cropSource = true)
    {
        if (!ValidateSource())
            return false;

        if (windowBounds.Width <= 0 ||
            windowBounds.Height <= 0)
        {
            return false;
        }

        if (DwmQueryThumbnailSourceSize(
                thumbnail,
                out var size) < 0)
        {
            return false;
        }

        if (size.Width <= 0 ||
            size.Height <= 0)
        {
            return false;
        }

        //
        // MINIMIZED WINDOW PATH
        //
        // Do NOT force a source crop.
        //
        // Edge/Chromium and apps such as OBS may expose
        // a DWM thumbnail surface whose dimensions do not
        // correspond exactly to the restored HWND bounds.
        //
        // Let DWM use the entire source surface.
        //
        if (!cropSource)
        {
            var minimizedProperties =
                new Properties
                {
                    Flags =
                        1 |     // DWM_TNP_RECTDESTINATION
                        4 |     // DWM_TNP_OPACITY
                        8 |     // DWM_TNP_VISIBLE
                        16,     // DWM_TNP_SOURCECLIENTAREAONLY

                    Opacity = 255,
                    Visible = 1,
                    ClientOnly = 0,

                    Destination =
                        new NativeMethods.Rect
                        {
                            Left = 0,
                            Top = 0,

                            Right =
                                Math.Max(
                                    1,
                                    (int)Math.Round(
                                        frame.Width)),

                            Bottom =
                                Math.Max(
                                    1,
                                    (int)Math.Round(
                                        frame.Height))
                        }
                };

            return DwmUpdateThumbnailProperties(
                thumbnail,
                ref minimizedProperties) >= 0;
        }

        //
        // NORMAL WINDOW PATH
        //
        // Crop invisible DWM resize margins.
        //
        var source =
            new NativeMethods.Rect
            {
                Left = 0,
                Top = 0,
                Right = size.Width,
                Bottom = size.Height
            };

        if (size.Width !=
                (int)Math.Round(frame.Width) ||
            size.Height !=
                (int)Math.Round(frame.Height))
        {
            source.Left =
                (int)Math.Clamp(
                    Math.Round(
                        (
                            frame.X -
                            windowBounds.X
                        ) *
                        size.Width /
                        windowBounds.Width),
                    0,
                    size.Width);

            source.Top =
                (int)Math.Clamp(
                    Math.Round(
                        (
                            frame.Y -
                            windowBounds.Y
                        ) *
                        size.Height /
                        windowBounds.Height),
                    0,
                    size.Height);

            source.Right =
                (int)Math.Clamp(
                    Math.Round(
                        (
                            frame.X +
                            frame.Width -
                            windowBounds.X
                        ) *
                        size.Width /
                        windowBounds.Width),
                    source.Left,
                    size.Width);

            source.Bottom =
                (int)Math.Clamp(
                    Math.Round(
                        (
                            frame.Y +
                            frame.Height -
                            windowBounds.Y
                        ) *
                        size.Height /
                        windowBounds.Height),
                    source.Top,
                    size.Height);
        }

        if (source.Right <= source.Left ||
            source.Bottom <= source.Top)
        {
            return false;
        }

        var properties =
            new Properties
            {
                Flags =
                    1 |     // RECTDESTINATION
                    2 |     // RECTSOURCE
                    4 |     // OPACITY
                    8 |     // VISIBLE
                    16,     // SOURCECLIENTAREAONLY

                Opacity = 255,
                Visible = 1,
                ClientOnly = 0,

                Source = source,

                Destination =
                    new NativeMethods.Rect
                    {
                        Left = 0,
                        Top = 0,

                        Right =
                            Math.Max(
                                1,
                                (int)Math.Round(
                                    frame.Width)),

                        Bottom =
                            Math.Max(
                                1,
                                (int)Math.Round(
                                    frame.Height))
                    }
            };

        return DwmUpdateThumbnailProperties(
            thumbnail,
            ref properties) >= 0;
    }

    public void Dispose()
    {
        if (thumbnail != 0)
        {
            DwmUnregisterThumbnail(
                thumbnail);
        }

        thumbnail = 0;
    }

    private bool ValidateSource()
    {
        if (thumbnail == 0) return false;
        ApplicationNative.GetWindowThreadProcessId(source, out var owner);
        if (ApplicationNative.IsWindow(source) && owner == sourceProcess) return true;
        Dispose();
        return false;
    }

    internal void SetVisible(bool visible)
    {
        if (thumbnail == 0) return;
        var properties = new Properties { Flags = 8, Visible = visible ? 1 : 0 };
        DwmUpdateThumbnailProperties(thumbnail, ref properties);
    }
}
