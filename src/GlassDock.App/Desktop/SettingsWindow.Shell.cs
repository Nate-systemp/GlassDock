using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GlassDock.App.Desktop;

public sealed partial class SettingsWindow
{
    private readonly DesktopGlassBackdrop settingsBackdrop = new();
    private readonly GlassSurface settingsGlass = new() { UseDesktopBackdrop = true, IsHitTestVisible = false };
    private GlassVisualPreview? glassPreview;
    private readonly Dictionary<string, Button> navigation = [];
    private readonly Dictionary<DockAppearanceMode, Button> appearanceButtons = [];
    private InteractiveGlassWindowHost? settingsHost;
    private string selectedPage = "Appearance";
    private bool shellReady;
    private bool sliderEditing;
    private bool editQueued;
    private bool editingReady;

    private SolidColorBrush Brush(string key) => (SolidColorBrush)SettingsRoot.Resources[key];

    private void InitializeSettingsShell()
    {
        SettingsRoot.Children.Insert(0, settingsGlass);
        Grid.SetColumnSpan(settingsGlass, 2); Grid.SetRowSpan(settingsGlass, 3);
        settingsHost = new(WinRT.Interop.WindowNative.GetWindowHandle(this))
        {
            EnableHostBackdropBrush = true, UseDockLayeredTransparency = true,
            PreserveWindowChrome = true, MinimumWidthDips = 880, MinimumHeightDips = 640
        };
        settingsHost.Configure();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(SettingsTitleBar);
        // Native client must be configured before the retained backdrop connects.
        SystemBackdrop = settingsBackdrop;
        SettingsRoot.SizeChanged += (_, _) => UpdateSettingsBounds();
        SettingsRoot.Loaded += (_, _) =>
        {
            UpdateSettingsBounds();
            if (editingReady) return;
            populating = true;
            foreach (var box in new[] { IconSizeBox, MagnificationScaleBox, IconSpacingBox }) AddSettingSlider(box);
            populating = false;
            editingReady = true;
        };
        PreviewHost.SizeChanged += (_, _) => { if (shellReady) UpdateDockPreview(); };
        settingsBackdrop.RenderingModeChanged += SettingsRenderingChanged;
        foreach (var page in SettingsCatalog.Pages)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            content.Children.Add(new FontIcon { Glyph = page.Glyph, FontSize = 18 });
            content.Children.Add(new TextBlock { Text = page.Title, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Tag = page.Id, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(12,11,12,11), BorderThickness = new(0), CornerRadius = new(9) };
            AutomationProperties.SetName(button, page.Title);
            button.Click += NavigationClick; navigation[page.Id] = button; NavigationItems.Children.Add(button);
        }
        BuildAppearanceTiles();
        BuildDockPreview();
        BuildGlassEditor();
        shellReady = true;
        settingsSession.Changed += SettingsSessionChanged;
        ApplySettingsAppearance();
        ShowSettingsPage(selectedPage);
        // User edits reuse the existing transactional save path and normalization.
        foreach (var box in new[] { IconSizeBox, MagnificationScaleBox, IconSpacingBox, BottomMarginBox,
            AutoHideDelayBox, PeekDelayBox })
            box.ValueChanged += (_, _) => SaveEditedControl();
        DockDisplayModeBox.SelectionChanged += (_, _) => SaveEditedControl();
        DockSizeBox.SelectionChanged += (_, _) => SaveEditedControl();
        PinnedPlacementBox.SelectionChanged += (_, _) => SaveEditedControl();
        FloatingDockGapBox.ValueChanged += (_, _) => SaveEditedControl();
        HoverWaveToggle.Toggled += (_, _) => SaveEditedControl();
        CircularHighlightsToggle.Toggled += (_, _) => GlassEdited();
        ClearCircularHighlightsToggle.Toggled += (_, _) => GlassEdited();
        HoverToExpandOnlyToggle.Toggled += (_, _) => SaveEditedControl();
        LaunchAtStartupToggle.Toggled += (_, _) => SaveEditedControl();
        Closed += (_, _) =>
        {
            settingsSession.Changed -= SettingsSessionChanged;
            settingsBackdrop.RenderingModeChanged -= SettingsRenderingChanged;
            SystemBackdrop = null;
            settingsHost?.Dispose();
            glassPreview?.Dispose();
        };
    }

    private void SaveEditedControl()
    {
        if (!editingReady || populating || saving || closed) return;
        if (sliderEditing) { UpdateDockPreview(); return; }
        if (editQueued) return;
        editQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            editQueued = false;
            if (closed || saving || populating) return;
            if (sliderEditing) { UpdateDockPreview(); return; }
            savingNonVisual = true;
            ApplyClick(this, new RoutedEventArgs());
        });
    }

    private void AddSettingSlider(NumberBox box)
    {
        if (SettingsRoot.FindName(box.Name + "Row") is not Grid row) return;
        row.ColumnSpacing = 12;
        row.ColumnDefinitions[0].Width = new(1, GridUnitType.Star);
        row.ColumnDefinitions[1].Width = new(1, GridUnitType.Star);
        row.ColumnDefinitions.Add(new() { Width = new(82) });
        Grid.SetColumn(box, 2);
        box.Header = null;
        box.SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden;
        var slider = new Slider { Minimum = box.Minimum, Maximum = box.Maximum,
            StepFrequency = box.SmallChange, Value = box.Value, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, AutomationProperties.GetName(box));
        slider.SetBinding(Slider.ValueProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = box, Path = new PropertyPath("Value"), Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay });
        slider.SetBinding(UIElement.IsHitTestVisibleProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = box, Path = new PropertyPath("IsEnabled") });
        slider.SetBinding(Control.IsEnabledProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = box, Path = new PropertyPath("IsEnabled") });
        slider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => sliderEditing = true), true);
        void Commit()
        {
            if (!sliderEditing) return;
            sliderEditing = false;
            SaveEditedControl();
        }
        slider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Commit()), true);
        slider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => Commit()), true);
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
    }

    private void SettingsSessionChanged(object? sender, GlassDockSettingsChangedEventArgs e)
    {
        void Refresh()
        {
            if (closed) return;
            Populate(settingsSession.Current);
            ApplySettingsAppearance();
        }
        if (DispatcherQueue.HasThreadAccess) Refresh(); else DispatcherQueue.TryEnqueue(Refresh);
    }

    private void ApplySettingsAppearance()
    {
        if (!shellReady || closed) return;
        var mode = settingsSession.Current.UtilityTheme == UtilityTheme.Light ? DockAppearanceMode.Light : DockAppearanceMode.Dark;
        var light = mode == DockAppearanceMode.Light;
        var glass = mode.GlassStyle() is not null;
        SettingsRoot.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        Brush("PrimaryBrush").Color = DockControlPalette.Foreground(mode);
        Brush("SecondaryBrush").Color = light ? Color.FromArgb(255,84,77,68) : Color.FromArgb(255,190,199,211);
        Brush("ShellBrush").Color = glass ? Color.FromArgb(mode == DockAppearanceMode.Clear ? (byte)100 : (byte)145, 19,27,39)
            : DockControlPalette.SolidSurface(mode);
        Brush("SidebarBrush").Color = light ? Color.FromArgb(70,255,255,255) : Color.FromArgb(35,12,19,29);
        Brush("CardBrush").Color = light ? Color.FromArgb(115,255,255,255) : Color.FromArgb(65,53,68,89);
        Brush("DividerBrush").Color = DockControlPalette.Surface(mode, light ? (byte)38 : (byte)32);
        Brush("SelectionBrush").Color = light ? Color.FromArgb(65,85,126,180) : Color.FromArgb(95,52,100,161);
        // Settings follows its independent Light/Dark theme; the dock keeps its material.
        var (shellMode, shellAppearance) = SettingsShellAppearance.Resolve(mode, settingsSession.Appearance);
        if (mode == DockAppearanceMode.Clear) Brush("ShellBrush").Color = Color.FromArgb(145,19,27,39);
        PopupStyle.Apply(settingsGlass, settingsBackdrop, shellAppearance, shellMode);
        settingsGlass.Apply(PopupMaterial.CreateForPopup(shellAppearance, shellMode) with
            { ShadowOpacity = 0, BorderOpacity = 0, EdgeHighlight = 0, CornerRadius = 0 });
        ClearMaterialNote.Visibility = glassDraft.Mode == DockAppearanceMode.Clear ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (value, button) in appearanceButtons)
        {
            button.BorderBrush = value == glassDraft.Mode ? new SolidColorBrush(Color.FromArgb(255,99,174,255)) : Brush("DividerBrush");
            button.BorderThickness = new(value == glassDraft.Mode ? 2 : 1);
            AutomationProperties.SetItemStatus(button, value == glassDraft.Mode ? "Selected" : "Not selected");
        }
        foreach (var (id, button) in navigation) SetNavigationState(button, id == selectedPage);
        var title = AppWindow.TitleBar;
        title.ButtonBackgroundColor = title.ButtonInactiveBackgroundColor = Colors.Transparent;
        title.ButtonForegroundColor = DockControlPalette.Foreground(mode);
        title.ButtonHoverBackgroundColor = DockControlPalette.Hover(mode, true);
        UpdateDockPreview(); UpdateSettingsBounds(); SettingsRenderingChanged(this, EventArgs.Empty);
    }

    private void UpdateSettingsBounds()
    {
        if (!shellReady || SettingsRoot.ActualWidth <= 0) return;
        var scale = SettingsRoot.XamlRoot?.RasterizationScale ?? 1;
        settingsBackdrop.SetBounds(SettingsRoot.ActualWidth, SettingsRoot.ActualHeight,
            SettingsRoot.ActualWidth, SettingsRoot.ActualHeight, 0, scale, 1);
    }

    private void SettingsRenderingChanged(object? sender, EventArgs e)
    {
        if (closed) return;
        RenderingStatusText.Text = $"Settings rendering: {settingsBackdrop.RenderingMode}\nAppearance: {settingsSession.Current.DockAppearanceMode}\nClear dock: GPU refraction uses its own existing capture surface; Settings does not create a capture session.";
    }

    private void BuildAppearanceTiles()
    {
        var samples = new[] { Color.FromArgb(255,36,36,36), Color.FromArgb(255,216,204,184),
            Color.FromArgb(210,121,147,177), Color.FromArgb(165,64,121,181), Color.FromArgb(70,129,183,224) };
        foreach (var mode in Enum.GetValues<DockAppearanceMode>())
        {
            AppearanceTiles.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var content = new StackPanel { Spacing = 10 };
            var scene = new Grid { Height = 42 };
            scene.Children.Add(new Border { CornerRadius = new(8), Background = new LinearGradientBrush
                { StartPoint = new(0,0), EndPoint = new(1,1), GradientStops = {
                    new() { Color = Color.FromArgb(255,33,50,78), Offset = 0 }, new() { Color = Color.FromArgb(255,146,167,190), Offset = 1 } } } });
            scene.Children.Add(new Border { Margin = new(6,12,6,4), CornerRadius = new(12), Background = new SolidColorBrush(samples[(int)mode]),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100,255,255,255)), BorderThickness = new(0.5) });
            content.Children.Add(scene);
            content.Children.Add(new TextBlock { Text = mode == DockAppearanceMode.Clear ? "Clear" : mode.ToString(), FontSize = 12, TextAlignment = TextAlignment.Center });
            var button = new Button { Content = content, Padding = new(7,10,7,10), CornerRadius = new(12),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brush("CardBrush") };
            AutomationProperties.SetName(button, mode == DockAppearanceMode.Clear ? "Clear — Liquid Glass" : mode.ToString());
            button.Click += (_, _) => SelectBuiltInGlass(mode);
            Grid.SetColumn(button, (int)mode); AppearanceTiles.Children.Add(button); appearanceButtons[mode] = button;
        }
    }

    private void BuildDockPreview()
    {
        glassPreview = new GlassVisualPreview(previewApplications);
        PreviewHost.Height = 140;
        PreviewHost.Children.Add(glassPreview);
    }

    private void UpdateDockPreview()
    {
        glassPreview?.FocusComponent(glassTarget);
        glassPreview?.Update(glassDraft, glassDraft.Mode == DockAppearanceMode.Clear
            ? ClearCircularHighlightsToggle.IsOn : CircularHighlightsToggle.IsOn);
    }

    private void SearchSettingsChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = SettingsCatalog.Search(sender.Text);
    }

    private void SearchSettingChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    { if (args.SelectedItem is SettingsSearchEntry entry) NavigateToSetting(entry); }
    private void SearchSettingSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var entry = args.ChosenSuggestion as SettingsSearchEntry ?? SettingsCatalog.Search(args.QueryText).FirstOrDefault();
        if (entry is not null) NavigateToSetting(entry);
        else SetStatus("No matching settings. Try appearance, startup, stack or monitor.", false);
    }
    private void NavigateToSetting(SettingsSearchEntry entry)
    {
        ShowSettingsPage(entry.Page);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || SettingsRoot.FindName(entry.Control) is not FrameworkElement target) return;
            target.StartBringIntoView();
            if (target is Control control) control.Focus(FocusState.Keyboard);
        });
    }

    private async Task<bool> ConfirmResetAsync(string area)
    {
        if (closed || saving) return false;
        var dialog = new ContentDialog { XamlRoot = SettingsRoot.XamlRoot, RequestedTheme = SettingsRoot.RequestedTheme,
            Title = $"Reset {area}?", Content = "This restores the selected preferences to their defaults. Your pinned apps and stacks are kept.",
            PrimaryButtonText = "Reset", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
