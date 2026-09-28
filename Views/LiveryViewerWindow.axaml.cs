using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ForzaToolkit.LiveryRender;
using ForzaToolkit.LiveryRender.Avalonia;
using ForzaToolkit.LiveryRender.Livery;
using ForzaToolkit.LiveryRender.Rendering;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal partial class LiveryViewerWindow : Window
{
    private static readonly ViewerQuality[] Presets = [ViewerQuality.Low, ViewerQuality.Medium, ViewerQuality.High, ViewerQuality.Ultra];

    private static readonly TimeSpan GpuReadyTimeout = TimeSpan.FromSeconds(5);

    private readonly AppSettingsData _settings;
    private readonly TaskCompletionSource _gpuReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BackgroundTaskTracker _loads = new();
    private bool _closed;

    public LiveryRenderer Renderer { get; }

    public LiveryViewerWindow(LiveryRenderer renderer, AppSettingsData settings)
    {
        InitializeComponent();
        Renderer = renderer;
        _settings = settings;

        int qualityIndex = Math.Clamp(settings.ViewerQualityIndex, 0, Presets.Length - 1);
        Viewer.Source = renderer;
        Viewer.Quality = Presets[qualityIndex];
        Viewer.ClearColor = ToRgba(this.GetThemeBrush("BgBrush"));
        QualityBox.SelectedIndex = qualityIndex;

        ApplyLocalizedTexts();

        QualityBox.SelectionChanged += (_, _) =>
        {
            int index = Math.Max(0, QualityBox.SelectedIndex);
            Viewer.Quality = Presets[index];
            _settings.ViewerQualityIndex = index;
            AppSettingsService.Save(_settings);
        };
        Viewer.LoadingChanged += (_, busy) => Busy.IsVisible = busy;
        Viewer.GpuReady += (_, gpu) =>
        {
            if (gpu.MaxLiveryScale < 4) QualityUltraItem.IsEnabled = false;
            _gpuReady.TrySetResult();
        };
        Viewer.StatsUpdated += (_, s) => StatusText.Text = string.Format(Strings.ViewerStatsFormat,
            s.Triangles, s.Lod, s.LiveryWidth, s.LiveryHeight, s.FrameWidth, s.FrameHeight, s.Samples, s.FrameMs);
        Viewer.RenderFailed += (_, message) =>
        {
            AppLogger.LogError($"3D viewer: OpenGL failed: {message}", new InvalidOperationException(message));
            ShowError(Strings.ViewerNoOpenGl);
            _gpuReady.TrySetResult();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _gpuReady.TrySetResult();
            Viewer.Clear();
            Viewer.Source = null;
        };
    }

    private void ApplyLocalizedTexts()
    {
        QualityLowItem.Content = Strings.ViewerQualityLow;
        QualityMediumItem.Content = Strings.ViewerQualityMedium;
        QualityHighItem.Content = Strings.ViewerQualityHigh;
        QualityUltraItem.Content = Strings.ViewerQualityUltra;
        ResetViewButton.Content = Strings.ViewerResetView;
        ControlsHintText.Text = Strings.ViewerControlsHint;
        ToolTip.SetTip(ControlsHintText, Strings.ViewerControlsHint);
    }

    public Task ShowLiveryAsync(string liveryName, LiveryDrawing drawing) =>
        _loads.Run(() => ShowLiveryCoreAsync(liveryName, drawing), $"3D viewer: show '{liveryName}'");

    public Task<bool> WaitForLoadsAsync(TimeSpan timeout) => _loads.WaitAllAsync(timeout);

    private async Task ShowLiveryCoreAsync(string liveryName, LiveryDrawing drawing)
    {
        Title = string.Format(Strings.ViewerTitleFormat, liveryName);
        TitleBarText.Text = Title;
        ErrorText.IsVisible = false;
        WarningText.IsVisible = false;
        StatusText.Text = "";
        Viewer.ResetView();

        await Task.WhenAny(_gpuReady.Task, Task.Delay(GpuReadyTimeout));
        if (_closed) return;

        RenderResult<LiveryCanvas> result;
        try
        {
            result = await Viewer.ShowAsync(drawing);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"3D viewer: failed to show '{liveryName}'", ex);
            ShowError(Strings.RenderErrorGeneric);
            return;
        }

        if (result.Status == RenderStatus.Failed)
        {
            if (result.Error is { Code: RenderErrorCode.Cancelled }) return;
            AppLogger.LogError($"3D viewer: failed to show '{liveryName}': {result.Error}",
                new InvalidOperationException(result.Error?.Message));
            ShowError(result.Error is { } error ? GameFolderText.RenderError(error) : Strings.RenderErrorGeneric);
            return;
        }

        if (result.Status == RenderStatus.Partial && result.Warnings.Count > 0)
        {
            foreach (var warning in result.Warnings)
                AppLogger.LogError($"3D viewer: '{liveryName}' drawn partially: {warning}",
                    new InvalidOperationException(warning.Message));
            WarningText.Text = string.Join("; ", result.Warnings.Select(GameFolderText.RenderError).Distinct());
            WarningText.IsVisible = true;
        }
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.IsVisible = true;
    }

    private static Rgba? ToRgba(IBrush brush) =>
        brush is ISolidColorBrush solid ? new Rgba(solid.Color.R, solid.Color.G, solid.Color.B, 255) : null;

    private void ResetViewButton_Click(object? sender, RoutedEventArgs e) => Viewer.ResetView();

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.HandleTitleBarDragOrMaximize(e, MaximizeIcon);

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => this.ToggleMaximize(MaximizeIcon);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
