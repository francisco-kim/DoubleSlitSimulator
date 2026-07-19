using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace DoubleSlitWeb.Interop;

/// <summary>
///     Fast-path canvas rendering: the RGBA buffer is handed to JavaScript as a
///     view over WASM memory (no serialisation) and blitted with putImageData.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class CanvasInterop
{
    public const string ModuleName = "doubleSlitCanvas";

    private static Task? _moduleImport;

    public static Task EnsureModuleLoadedAsync() =>
        _moduleImport ??= JSHost.ImportAsync(ModuleName, "../js/doubleSlitCanvas.js");

    [JSImport("initCanvas", ModuleName)]
    public static partial void InitCanvas(string canvasId, int width, int height);

    [JSImport("renderFrame", ModuleName)]
    public static partial void RenderFrame(
        string canvasId,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> rgba);

    [JSImport("clearCanvas", ModuleName)]
    public static partial void ClearCanvas(string canvasId);

    /// <summary>Draws the static top-view scene (gun, barrier, screen, detectors).</summary>
    [JSImport("drawScene", ModuleName)]
    public static partial void DrawScene(string canvasId, string geometryJson);

    /// <summary>Queues one electron's flight animation; JS stamps the impact dot.</summary>
    [JSImport("animateElectron", ModuleName)]
    public static partial void AnimateElectron(string shotJson);

    [JSImport("clearDots", ModuleName)]
    public static partial void ClearDots(string dotsCanvasId, string fxCanvasId);

    [JSImport("drawCurves", ModuleName)]
    public static partial void DrawCurves(
        string canvasId,
        [JSMarshalAs<JSType.MemoryView>] Span<double> theory,
        [JSMarshalAs<JSType.MemoryView>] Span<double> histogram,
        bool showTheory,
        bool showHistogram);

    [JSImport("downloadCanvasPng", ModuleName)]
    public static partial void DownloadCanvasPng(string canvasId, string filename);
}
