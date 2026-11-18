using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using GlassDock.App.Desktop;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Streams;

namespace GlassDock.App.Controls;

internal sealed class MusicControlPill : Grid, IDisposable
{
    private const double PlayingButtonWidth = 48;
    private const double IdleButtonWidth = 60;
    private const double ControlsInset = 8;
    private const double IdleControlsWidth = 3 * IdleButtonWidth + 2 * ControlsInset;
    private const double PlayingControlsWidth = 3 * PlayingButtonWidth + 2 * ControlsInset;
    private const double ArtworkSize = 38;
    private const double TrackInset = 12;
    private const double TrackGap = 9;
    private const double TitleWidth = 82;
    private const double TrackWidth = TrackInset + ArtworkSize + TrackGap + TitleWidth;
    internal const double MaximumWidth = PlayingControlsWidth + TrackWidth;
    private readonly MusicSessionService music;
    private readonly Border tint = new() { IsHitTestVisible = false };
    private readonly StackPanel track = new() { Opacity = 0, IsHitTestVisible = false, Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid artwork = new();
    private readonly Rectangle previousCover = new();
    private readonly Rectangle cover = new();
    private readonly Border coverBackdrop = new() { Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(32, 255, 255, 255)) };
    private readonly TextBlock trackTitle = new() { Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        FontFamily = new("Segoe UI Variable Text"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly RectangleGeometry trackClip = new();
    private readonly UtilityIcon fallback = new() { Width = 14, Height = 14,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel controls = new() { Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly (Button Button, UtilityIcon Icon) previous, toggle, next;
    private readonly bool animations = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private MusicPlayback playback = new();
    private Storyboard? layoutMotion, coverMotion;
    private byte[]? artworkBytes;
    private CancellationTokenSource? artworkDecode;
    private int artworkRevision;
    private double scale = 1;
    private UtilityTheme theme;
    private DockAppearanceMode mode;
    private bool? pauseIcon;
    private bool disposed;

    private double TargetWidth => (playback.Playing ? MaximumWidth : IdleControlsWidth) * scale;
    private double TargetButtonWidth => (playback.Playing ? PlayingButtonWidth : IdleButtonWidth) * scale;
    internal UtilityGlassSurface Glass { get; } = new() { Circular = false, UseDockMaterial = true };

    internal MusicControlPill(MusicSessionService music)
    {
        this.music = music;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(Glass.Element);
        Children.Add(tint);
        track.Clip = trackClip;
        artwork.Children.Add(coverBackdrop);
        artwork.Children.Add(fallback);
        artwork.Children.Add(previousCover);
        artwork.Children.Add(cover);
        track.Children.Add(artwork);
        track.Children.Add(trackTitle);
        Children.Add(track);
        Children.Add(controls);
        SizeChanged += (_, args) => ClipTrack(args.NewSize.Width);
        previous = AddButton(MusicCommand.Previous, "Previous track");
        toggle = AddButton(MusicCommand.PlayPause, "Play");
        next = AddButton(MusicCommand.Next, "Next track");
        _ = previous.Icon.SetAsync("rewind", true, 0xFFFFFFFF);
        _ = next.Icon.SetAsync("fast-forward", true, 0xFFFFFFFF);
        _ = fallback.SetAsync("music-note", true, 0xFFFFFFFF);
        Width = IdleControlsWidth;
        music.Changed += PlaybackChanged;
    }

    private (Button, UtilityIcon) AddButton(MusicCommand command, string name)
    {
        var icon = new UtilityIcon();
        var button = new Button { Content = icon, Width = IdleButtonWidth, Padding = new(0), BorderThickness = new(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            MinWidth = 0, MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBackgroundDisabled",
            "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed", "ButtonBorderBrushDisabled" })
            button.Resources[key] = button.Background;
        AutomationProperties.SetName(button, name);
        button.Click += async (_, _) => await music.ExecuteAsync(command);
        controls.Children.Add(button);
        return (button, icon);
    }

    internal void Configure(double dockScale, UtilityTheme utilityTheme, DockAppearanceMode appearance, double dockIconSize)
    {
        theme = appearance.GlassStyle() is null
            ? appearance == DockAppearanceMode.Light ? UtilityTheme.Light : UtilityTheme.Dark
            : utilityTheme;
        mode = appearance;
        var scaleChanged = scale != dockScale;
        scale = dockScale;
        var rgb = UtilityColors.Surface(theme);
        tint.Background = new SolidColorBrush(mode.GlassStyle() is null ? DockControlPalette.SolidSurface(DockAppearanceMode.Dark) :
            global::Windows.UI.Color.FromArgb(12, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        artwork.Width = artwork.Height = ArtworkSize * scale;
        var radius = 8 * scale;
        coverBackdrop.CornerRadius = new(radius);
        cover.RadiusX = cover.RadiusY = previousCover.RadiusX = previousCover.RadiusY = radius;
        track.Margin = new(TrackInset * scale, 0, 0, 0);
        track.Spacing = TrackGap * scale;
        trackTitle.Width = TitleWidth * scale;
        trackTitle.FontSize = Math.Max(11, 14 * scale);
        controls.Margin = new(0, 0, ControlsInset * scale, 0);
        fallback.Width = fallback.Height = 18 * scale;
        foreach (var (button, icon) in new[] { previous, next })
        {
            button.Width = TargetButtonWidth;
            button.Height = 44 * scale;
            icon.Width = icon.Height = 30 * scale;
        }
        toggle.Button.Width = TargetButtonWidth;
        toggle.Button.Height = 48 * scale;
        SetPlayPauseIconSize(dockIconSize);
        if (scaleChanged)
        {
            layoutMotion?.Stop();
            layoutMotion = null;
            Width = TargetWidth;
            track.Opacity = playback.Playing ? 1 : 0;
            if (!playback.Playing) ClearArtwork();
        }
        PlaybackChanged(this, EventArgs.Empty);
        ClipTrack(Width);
    }

    internal void SetPlayPauseIconSize(double size) => toggle.Icon.Width = toggle.Icon.Height = Math.Clamp(size, 34 * scale, 40 * scale);

    internal void SetUtilityHeight(double height)
    {
        if (Height == height) return;
        Height = height;
        tint.CornerRadius = new(height / 2);
        Glass.CornerRadius = (float)(height / 2);
    }

    private void PlaybackChanged(object? sender, EventArgs args)
    {
        if (disposed) return;
        var wasPlaying = playback.Playing;
        playback = music.Playback;
        UpdateControls();
        if (playback.Playing)
            trackTitle.Text = string.IsNullOrWhiteSpace(playback.Title) ? "Now playing" : playback.Title;
        if (wasPlaying != playback.Playing)
        {
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var buttonWidth = toggle.Button.ActualWidth > 0 ? toggle.Button.ActualWidth : toggle.Button.Width;
            var opacity = track.Opacity;
            layoutMotion?.Stop();
            Width = TargetWidth;
            previous.Button.Width = toggle.Button.Width = next.Button.Width = TargetButtonWidth;
            track.Opacity = playback.Playing ? 1 : 0;
            if (animations && IsLoaded)
            {
                var motion = new Storyboard();
                Animate(motion, this, "Width", width, Width);
                if (double.IsFinite(buttonWidth) && buttonWidth != TargetButtonWidth)
                    foreach (var button in new[] { previous.Button, toggle.Button, next.Button })
                        Animate(motion, button, "Width", buttonWidth, TargetButtonWidth);
                Animate(motion, track, "Opacity", opacity, track.Opacity);
                layoutMotion = motion;
                motion.Completed += (_, _) =>
                {
                    if (!ReferenceEquals(layoutMotion, motion)) return;
                    motion.Stop();
                    layoutMotion = null;
                    if (!playback.Playing) ClearArtwork();
                };
                motion.Begin();
            }
            else if (!playback.Playing) ClearArtwork();
        }

        if (playback.Playing && !ReferenceEquals(artworkBytes, playback.Artwork))
        {
            artworkBytes = playback.Artwork;
            _ = UpdateArtworkAsync(artworkBytes);
        }
        else if (!playback.Playing)
        {
            artworkBytes = null;
            artworkRevision++;
            artworkDecode?.Cancel();
        }
    }

    private void ClipTrack(double width) => trackClip.Rect =
        new(0, 0, Math.Clamp(width - 3 * toggle.Button.Width - (2 * ControlsInset + TrackInset) * scale,
            0, (TrackWidth - TrackInset) * scale), ArtworkSize * scale);

    private void UpdateControls()
    {
        previous.Button.IsEnabled = playback.CanPrevious;
        toggle.Button.IsEnabled = playback.CanToggle;
        next.Button.IsEnabled = playback.CanNext;
        if (pauseIcon == playback.Playing) return;
        pauseIcon = playback.Playing;
        AutomationProperties.SetName(toggle.Button, playback.Playing ? "Pause" : "Play");
        _ = toggle.Icon.SetAsync(playback.Playing ? "pause" : "play", true, 0xFFFFFFFF);
    }

    private async Task UpdateArtworkAsync(byte[]? bytes)
    {
        var revision = ++artworkRevision;
        artworkDecode?.Cancel();
        using var decode = new CancellationTokenSource();
        artworkDecode = decode;
        ImageBrush? brush = null;
        try
        {
            if (bytes is not null)
            {
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer()).AsTask(decode.Token);
                stream.Seek(0);
                var bitmap = new BitmapImage { DecodePixelWidth = 96, DecodePixelHeight = 96 };
                await bitmap.SetSourceAsync(stream).AsTask(decode.Token);
                brush = new() { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
            }
        }
        catch (Exception error) when (error is COMException or IOException or ArgumentException or InvalidOperationException
            or UnauthorizedAccessException or NotSupportedException or OperationCanceledException) { }
        finally { if (ReferenceEquals(artworkDecode, decode)) artworkDecode = null; }

        if (disposed || revision != artworkRevision) return;
        coverMotion?.Stop();
        previousCover.Fill = cover.Fill;
        cover.Fill = brush;
        cover.Opacity = brush is null ? 0 : 1;
        previousCover.Opacity = 0;
        if (!animations || !IsLoaded) { previousCover.Fill = null; return; }
        var motion = new Storyboard();
        if (brush is not null) Animate(motion, cover, "Opacity", 0, 1);
        if (previousCover.Fill is not null) Animate(motion, previousCover, "Opacity", 1, 0);
        if (motion.Children.Count == 0) return;
        coverMotion = motion;
        motion.Completed += (_, _) =>
        {
            if (!ReferenceEquals(coverMotion, motion)) return;
            motion.Stop();
            coverMotion = null;
            previousCover.Fill = null;
        };
        motion.Begin();
    }

    private static void Animate(Storyboard storyboard, DependencyObject element, string property, double from, double to)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void ClearArtwork()
    {
        coverMotion?.Stop();
        coverMotion = null;
        cover.Fill = previousCover.Fill = null;
        trackTitle.Text = string.Empty;
    }

    public void Dispose()
    {
        disposed = true;
        artworkRevision++;
        artworkDecode?.Cancel();
        music.Changed -= PlaybackChanged;
        layoutMotion?.Stop();
        ClearArtwork();
        artworkBytes = null;
        Glass.Dispose();
    }
}
