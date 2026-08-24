using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OcuNet.Commands;

namespace OcuNet;

public sealed class OcuNetDriver : IDisposable
{
    private readonly IMonitorService _monitorService;
    private readonly IMouseController _mouseController;
    private readonly IDisposable[] _disposables;
    private readonly WaitForCommandHandler _waitForHandler;
    private readonly WaitForAnyCommandHandler _waitForAnyHandler;
    private readonly WaitForAllCommandHandler _waitForAllHandler;
    private readonly MoveToLocationCommandHandler _moveToLocationHandler;
    private readonly SingleClickLocationCommandHandler _singleClickLocationHandler;
    private readonly DoubleClickLocationCommandHandler _doubleClickLocationHandler;
    private readonly TripleClickLocationCommandHandler _tripleClickLocationHandler;
    private readonly RightClickLocationCommandHandler _rightClickLocationHandler;
    private readonly DragLocationCommandHandler _dragLocationHandler;
    private readonly DropLocationCommandHandler _dropLocationHandler;
    private readonly MouseWheelCommandHandler _mouseWheelHandler;
    private readonly TypeTextCommandHandler _typeTextHandler;
    private readonly KeyPressCommandHandler _keyPressHandler;
    private readonly KeyDownCommandHandler _keyDownHandler;
    private readonly KeyUpCommandHandler _keyUpHandler;

    private int _monitorIndex;
    private MouseSpeed _mouseSpeed;
    private TimeSpan _defaultWaitForDuration;
    private TimeSpan _defaultKeyboardSleepAfterDuration;
    private AttachedWindow? _attachedWindow;

    private static readonly TimeSpan WindowSearchPollInterval = TimeSpan.FromMilliseconds(250);

    internal OcuNetDriver(
        DriverOptions options,
        IFileWriter fileWriter,
        IMonitorService monitorService,
        IElementRecognizer elementRecognizer,
        IMouseController mouseController,
        IKeyboardController keyboardController,
        params IDisposable[] disposables)
    {
        this._monitorService = monitorService;
        this._mouseController = mouseController;
        this._disposables = disposables;

        this._waitForHandler = new WaitForCommandHandler(options, fileWriter, monitorService, elementRecognizer);
        this._waitForAnyHandler = new WaitForAnyCommandHandler(options, fileWriter, monitorService, elementRecognizer);
        this._waitForAllHandler = new WaitForAllCommandHandler(options, fileWriter, monitorService, elementRecognizer);
        this._moveToLocationHandler = new MoveToLocationCommandHandler(mouseController);
        this._singleClickLocationHandler = new SingleClickLocationCommandHandler(mouseController);
        this._doubleClickLocationHandler = new DoubleClickLocationCommandHandler(mouseController);
        this._tripleClickLocationHandler = new TripleClickLocationCommandHandler(mouseController);
        this._rightClickLocationHandler = new RightClickLocationCommandHandler(mouseController);
        this._dragLocationHandler = new DragLocationCommandHandler(mouseController);
        this._dropLocationHandler = new DropLocationCommandHandler(mouseController);
        this._mouseWheelHandler = new MouseWheelCommandHandler(mouseController);
        this._typeTextHandler = new TypeTextCommandHandler(keyboardController);
        this._keyPressHandler = new KeyPressCommandHandler(keyboardController);
        this._keyDownHandler = new KeyDownCommandHandler(keyboardController);
        this._keyUpHandler = new KeyUpCommandHandler(keyboardController);

        this._monitorIndex = 0;
        this._mouseSpeed = options.MouseSpeed;
        this._defaultWaitForDuration = options.DefaultWaitForDuration;
        this._defaultKeyboardSleepAfterDuration = options.DefaultKeyboardSleepAfterDuration;
    }

    /// <summary>
    /// Gets the window currently attached to this driver, or <c>null</c> when no window is attached.
    /// </summary>
    public AttachedWindow? CurrentWindow => this._attachedWindow;

    public static OcuNetDriver Create()
    {
        return Create(new DriverOptions());
    }

    public static OcuNetDriver Create(DriverOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var monitorService = new MonitorService(options.ScreenshotCacheDuration);
        var imageRecognizer = new ImageElementRecognizer();
        IElementRecognizer textRecognizer = options.OcrEngine == OcrEngine.Tesseract
            ? new TextElementRecognizer(options)
            : new PaddleOcrTextElementRecognizer(options);
        var elementRecognizer = new AggregateElementRecognizer(imageRecognizer, textRecognizer);
        var mouseController = new MouseController();
        var keyboardController = new KeyboardController();
        var fileWriter = new RealFileWriter();

        return new OcuNetDriver(options, fileWriter, monitorService, elementRecognizer, mouseController, keyboardController, (IDisposable)textRecognizer);
    }

    public async Task<MonitorDescription[]> GetMonitorsAsync()
    {
        return await this._monitorService.GetMonitors().ConfigureAwait(false);
    }

    public async Task<MonitorDescription> GetCurrentMonitorAsync()
    {
        return await this._monitorService.GetMonitor(this._monitorIndex).ConfigureAwait(false);
    }

    public async Task<Bitmap> GetScreenshotAsync()
    {
        var monitor = await this.GetCurrentMonitorAsync().ConfigureAwait(false);
        return await this._monitorService.GetScreenshot(monitor).ConfigureAwait(false);
    }

    public async Task SaveScreenshotAsync(Stream destinationStream)
    {
        using var screenshot = await this.GetScreenshotAsync().ConfigureAwait(false);
        screenshot.Save(destinationStream, ImageFormat.Png);
    }

    public async Task SaveScreenshotAsync(string destinationPath)
    {
        using var screenshot = await this.GetScreenshotAsync().ConfigureAwait(false);
        screenshot.Save(destinationPath, ImageFormat.Png);
    }

    /// <summary>
    /// Saves a screenshot cropped to the currently attached window's bounds (see
    /// <see cref="AttachWindowAsync(string, TimeSpan?, WindowTitleMatchMode, bool, double)"/>).
    /// The window bounds are refreshed from the operating system at capture time, so a
    /// moved or resized window is always followed. Throws <see cref="WindowNotFoundException"/>
    /// when no window is attached or the attached window is no longer available.
    /// </summary>
    public async Task SaveWindowScreenshotAsync(Stream destinationStream)
    {
        using var screenshot = await this.GetWindowScreenshotAsync().ConfigureAwait(false);
        screenshot.Save(destinationStream, ImageFormat.Png);
    }

    /// <summary>
    /// Saves a screenshot cropped to the currently attached window's bounds (see
    /// <see cref="AttachWindowAsync(string, TimeSpan?, WindowTitleMatchMode, bool, double)"/>).
    /// The window bounds are refreshed from the operating system at capture time, so a
    /// moved or resized window is always followed. Throws <see cref="WindowNotFoundException"/>
    /// when no window is attached or the attached window is no longer available.
    /// </summary>
    public async Task SaveWindowScreenshotAsync(string destinationPath)
    {
        using var screenshot = await this.GetWindowScreenshotAsync().ConfigureAwait(false);
        screenshot.Save(destinationPath, ImageFormat.Png);
    }

    private async Task<Bitmap> GetWindowScreenshotAsync()
    {
        if (this._attachedWindow == null)
        {
            throw new WindowNotFoundException(
                Messages.OcuNetDriver_Throw_NoAttachedWindowForScreenshot,
                string.Empty,
                Array.Empty<WindowInfo>());
        }

        var monitor = await this.GetCurrentMonitorAsync().ConfigureAwait(false);
        var windowBounds = this._attachedWindow.Bounds;

        // Convert the absolute screen rectangle to monitor-relative coordinates and
        // clamp it to the monitor (the window may extend beyond its screen edge).
        var left = Math.Max(0, windowBounds.Left - monitor.Left);
        var top = Math.Max(0, windowBounds.Top - monitor.Top);
        var right = Math.Min(monitor.Width, windowBounds.Right - monitor.Left);
        var bottom = Math.Min(monitor.Height, windowBounds.Bottom - monitor.Top);

        if (right <= left || bottom <= top)
        {
            throw new InvalidOperationException(
                Messages.OcuNetDriver_Throw_WindowOutsideMonitor.FormatInvariant(this._attachedWindow.Title));
        }

        var screenshot = await this._monitorService.GetScreenshot(monitor).ConfigureAwait(false);
        using (screenshot)
        {
            return screenshot.Crop(new Rectangle(left, top, right, bottom));
        }
    }

    public Point GetMousePositionAsync()
    {
        return this._mouseController.GetCurrentPosition();
    }

    /// <summary>
    /// Enumerates all visible top-level windows with a non-empty title, in z-order.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "It looks more coherent to only use instance methods here.")]
    public Task<WindowInfo[]> ListWindowsAsync()
    {
        return WindowNative.GetWindowsAsync();
    }

    /// <summary>
    /// Finds a window whose title matches <paramref name="titlePattern"/> and attaches it to this
    /// driver. While a window is attached, every element lookup is automatically constrained to the
    /// window's current bounds; call <see cref="DetachWindowAsync"/> or dispose the returned
    /// <see cref="AttachedWindow"/> to restore full-screen lookups.
    /// </summary>
    /// <param name="titlePattern">
    /// The window title pattern. Interpretation depends on <paramref name="matchMode"/>:
    /// case-insensitive substring (default), regular expression, or fuzzy pattern.
    /// </param>
    /// <param name="waitFor">
    /// How long to poll for the window to appear (for example while an application is still
    /// launching). <c>null</c> performs a single attempt.
    /// </param>
    /// <param name="matchMode">The title matching mode.</param>
    /// <param name="activate">When <c>true</c>, restores and activates the window after attaching.</param>
    /// <param name="fuzzyThreshold">
    /// The minimum similarity (0 to 1) required in <see cref="WindowTitleMatchMode.Fuzzy"/> mode.
    /// </param>
    /// <returns>The attached window; disposing it detaches it.</returns>
    /// <exception cref="ArgumentException">The pattern is empty or not a valid regular expression.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fuzzyThreshold"/> is outside [0, 1].</exception>
    /// <exception cref="WindowNotFoundException">No window matched, or several windows matched.</exception>
    public async Task<AttachedWindow> AttachWindowAsync(
        string titlePattern,
        TimeSpan? waitFor = default,
        WindowTitleMatchMode matchMode = WindowTitleMatchMode.Substring,
        bool activate = true,
        double fuzzyThreshold = 0.6)
    {
        if (string.IsNullOrWhiteSpace(titlePattern))
        {
            throw new ArgumentException(Messages.OcuNetDriver_Throw_EmptyWindowTitlePattern, nameof(titlePattern));
        }

        if (fuzzyThreshold < 0 || fuzzyThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fuzzyThreshold), Messages.OcuNetDriver_Throw_InvalidFuzzyThreshold);
        }

        if (matchMode == WindowTitleMatchMode.Regex && !WindowTitleMatcher.TryCompileRegex(titlePattern, out _))
        {
            throw new ArgumentException(Messages.OcuNetDriver_Throw_InvalidWindowTitleRegex.FormatInvariant(titlePattern), nameof(titlePattern));
        }

        var effectiveWaitFor = waitFor.GetValueOrDefault(TimeSpan.Zero);
        if (effectiveWaitFor < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waitFor), Messages.Throw_NegativeWaitFor);
        }

        var stopwatch = Stopwatch.StartNew();

        WindowInfo match;
        IReadOnlyList<WindowInfo> windows;
        while (true)
        {
            windows = await WindowNative.GetWindowsAsync().ConfigureAwait(false);
            var matches = WindowTitleMatcher.MatchAll(windows, titlePattern, matchMode, fuzzyThreshold);

            if (matches.Count == 1)
            {
                match = matches[0];
                break;
            }

            if (matches.Count > 1)
            {
                var titles = string.Join(", ", matches.Select(window => $"'{window.Title}'"));
                throw new WindowNotFoundException(
                    Messages.WindowNotFound_Throw_MultipleMatches.FormatInvariant(titlePattern, titles),
                    titlePattern,
                    matches);
            }

            if (stopwatch.Elapsed >= effectiveWaitFor)
            {
                throw CreateWindowNotFound(titlePattern, effectiveWaitFor, windows);
            }

            await Task.Delay(WindowSearchPollInterval).ConfigureAwait(false);
        }

        var attachedWindow = new AttachedWindow(this, match);
        this._attachedWindow = attachedWindow;

        if (activate)
        {
            await attachedWindow.ActivateAsync().ConfigureAwait(false);
        }

        await this.FollowWindowMonitorAsync(attachedWindow.Bounds).ConfigureAwait(false);

        return attachedWindow;
    }

    /// <summary>
    /// Detaches the currently attached window, restoring full-screen lookups.
    /// </summary>
    public Task DetachWindowAsync()
    {
        this.DetachWindow();
        return Task.CompletedTask;
    }

    internal void DetachWindow()
    {
        this._attachedWindow = null;
    }

    private static WindowNotFoundException CreateWindowNotFound(string titlePattern, TimeSpan waitFor, IReadOnlyList<WindowInfo> windows)
    {
        if (windows.Count == 0)
        {
            return new WindowNotFoundException(
                Messages.WindowNotFound_Throw_NoMatch.FormatInvariant(titlePattern, waitFor),
                titlePattern,
                windows);
        }

        var closest = windows
            .OrderByDescending(window => WindowTitleMatcher.GetSimilarity(titlePattern, window.Title))
            .First();
        var similarity = WindowTitleMatcher.GetSimilarity(titlePattern, closest.Title);
        var titles = string.Join(", ", windows.Select(window => $"'{window.Title}'"));

        return new WindowNotFoundException(
            Messages.WindowNotFound_Throw_NoMatch_WithWindows.FormatInvariant(titlePattern, waitFor, closest.Title, similarity, titles),
            titlePattern,
            windows);
    }

    private async Task FollowWindowMonitorAsync(Rectangle windowBounds)
    {
        var monitors = await this.GetMonitorsAsync().ConfigureAwait(false);
        if (monitors.Length == 0)
        {
            return;
        }

        var center = windowBounds.Center;
        var monitor = monitors.FirstOrDefault(candidate =>
                center.X >= candidate.Left && center.X < candidate.Right &&
                center.Y >= candidate.Top && center.Y < candidate.Bottom)
            ?? monitors.FirstOrDefault(candidate => candidate.IsPrimary)
            ?? monitors[0];

        this.SetCurrentMonitor(monitor);
    }

    internal Rectangle? GetEffectiveSearchRectangle(Rectangle? searchRect)
    {
        if (searchRect != null || this._attachedWindow == null)
        {
            return searchRect;
        }

        return this._attachedWindow.Bounds;
    }

    internal async Task<SearchResult> WaitForAsync(IElement element, TimeSpan? waitFor, Rectangle? searchRect, NoSingleResultBehavior noSingleResultBehavior)
    {
        if (element == null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var effectiveWaitFor = waitFor.GetValueOrDefault(this._defaultWaitForDuration);
        var effectiveSearchRect = this.GetEffectiveSearchRectangle(searchRect);
        return await this._waitForHandler.Execute(new WaitForCommand(new[] { element }, effectiveWaitFor, effectiveSearchRect, this._monitorIndex, noSingleResultBehavior)).ConfigureAwait(false);
    }

    public async Task<SearchResult> WaitForAsync(IElement element, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        return await this.WaitForAsync(element, waitFor, searchRect, NoSingleResultBehavior.Throw).ConfigureAwait(false);
    }

    internal async Task<SearchResult> WaitForAnyAsync(IEnumerable<IElement> elements, TimeSpan? waitFor, Rectangle? searchRect, NoSingleResultBehavior noSingleResultBehavior)
    {
        if (elements == null)
        {
            throw new ArgumentNullException(nameof(elements));
        }

        var enumeratedElements = new List<IElement>(elements);
        if (enumeratedElements.Count == 0)
        {
            throw new ArgumentException(Messages.OcuNetDriver_Throw_ElementsEmpty, nameof(elements));
        }

        var effectiveWaitFor = waitFor.GetValueOrDefault(this._defaultWaitForDuration);
        var effectiveSearchRect = this.GetEffectiveSearchRectangle(searchRect);
        return await this._waitForAnyHandler.Execute(new WaitForCommand(enumeratedElements, effectiveWaitFor, effectiveSearchRect, this._monitorIndex, noSingleResultBehavior)).ConfigureAwait(false);
    }

    public async Task<SearchResult> WaitForAnyAsync(IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        return await this.WaitForAnyAsync(elements, waitFor, searchRect, NoSingleResultBehavior.Throw).ConfigureAwait(false);
    }

    internal async Task<SearchResultCollection> WaitForAllAsync(IEnumerable<IElement> elements, TimeSpan? waitFor, Rectangle? searchRect, NoSingleResultBehavior noSingleResultBehavior)
    {
        if (elements == null)
        {
            throw new ArgumentNullException(nameof(elements));
        }

        var enumeratedElements = new List<IElement>(elements);
        if (enumeratedElements.Count == 0)
        {
            throw new ArgumentException(Messages.OcuNetDriver_Throw_ElementsEmpty, nameof(elements));
        }

        var effectiveWaitFor = waitFor.GetValueOrDefault(this._defaultWaitForDuration);
        var effectiveSearchRect = this.GetEffectiveSearchRectangle(searchRect);
        return await this._waitForAllHandler.Execute(new WaitForCommand(enumeratedElements, effectiveWaitFor, effectiveSearchRect, this._monitorIndex, noSingleResultBehavior)).ConfigureAwait(false);
    }

    public async Task<SearchResultCollection> WaitForAllAsync(IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        return await this.WaitForAllAsync(elements, waitFor, searchRect, NoSingleResultBehavior.Throw).ConfigureAwait(false);
    }

    public async Task MoveToAsync(int x, int y)
    {
        await this._moveToLocationHandler.Execute(new MouseLocationCommand(x, y, this._mouseSpeed)).ConfigureAwait(false);
    }

    public async Task SingleClickAsync(int x, int y)
    {
        await this._singleClickLocationHandler.Execute(new MouseLocationCommand(x, y, this._mouseSpeed)).ConfigureAwait(false);
    }

    public async Task DoubleClickAsync(int x, int y)
    {
        await this._doubleClickLocationHandler.Execute(new MouseLocationCommand(x, y, this._mouseSpeed)).ConfigureAwait(false);
    }

    public async Task TripleClickAsync(int x, int y)
    {
        await this._tripleClickLocationHandler.Execute(new MouseLocationCommand(x, y, this._mouseSpeed)).ConfigureAwait(false);
    }

    public async Task RightClickAsync(int x, int y)
    {
        await this._rightClickLocationHandler.Execute(new MouseLocationCommand(x, y, this._mouseSpeed)).ConfigureAwait(false);
    }

    public async Task DragFromAsync(int x, int y)
    {
        await this._dragLocationHandler.Execute(new MouseLocationCommand(x, y, this._mouseSpeed)).ConfigureAwait(false);
    }

    public async Task DropToAsync(int x, int y)
    {
        await this._dropLocationHandler.Execute(new MouseLocationCommand(x, y, this._mouseSpeed)).ConfigureAwait(false);
    }

    public async Task ScrollUpAsync(int scrollTicks = 1)
    {
        if (scrollTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scrollTicks), Messages.OcuNetDriver_Throw_ScrollTicksNotGreaterThanZero);
        }

        for (var i = 0; i < scrollTicks; i++)
        {
            await this._mouseWheelHandler.Execute(new MouseWheelCommand(IsUp: true)).ConfigureAwait(false);
        }
    }

    public async Task ScrollDownAsync(int scrollTicks = 1)
    {
        if (scrollTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scrollTicks), Messages.OcuNetDriver_Throw_ScrollTicksNotGreaterThanZero);
        }

        for (var i = 0; i < scrollTicks; i++)
        {
            await this._mouseWheelHandler.Execute(new MouseWheelCommand(IsUp: false)).ConfigureAwait(false);
        }
    }

    public Task ScrollUpUntilVisibleAsync(IElement element, TimeSpan waitFor, int scrollTicks = 1, Rectangle? searchRect = default)
    {
        return this.ScrollUntilVisibleAsync(element, waitFor, isUp: true, scrollTicks, searchRect);
    }

    public Task ScrollDownUntilVisibleAsync(IElement element, TimeSpan waitFor, int scrollTicks = 1, Rectangle? searchRect = default)
    {
        return this.ScrollUntilVisibleAsync(element, waitFor, isUp: false, scrollTicks, searchRect);
    }

    private async Task ScrollUntilVisibleAsync(IElement element, TimeSpan waitFor, bool isUp, int scrollTicks, Rectangle? searchRect)
    {
        if (waitFor <= TimeSpan.Zero)
        {
            throw new ArgumentException(Messages.Throw_NegativeWaitFor, nameof(waitFor));
        }

        for (var sw = Stopwatch.StartNew(); sw.Elapsed < waitFor;)
        {
            if (await this.IsVisibleAsync(element, TimeSpan.Zero, searchRect).ConfigureAwait(false))
            {
                return;
            }

            var scrollTask = isUp ? this.ScrollUpAsync(scrollTicks) : this.ScrollDownAsync(scrollTicks);
            await scrollTask.ConfigureAwait(false);
        }

        throw new ElementNotFoundException(element, waitFor);
    }

    public async Task TypeTextAsync(string text, TimeSpan? sleepAfter = default)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (text.Length > 0)
        {
            await this._typeTextHandler.Execute(new KeyboardTextCommand(text)).ConfigureAwait(false);

            var effectiveSleepAfter = sleepAfter.GetValueOrDefault(this._defaultKeyboardSleepAfterDuration);
            if (effectiveSleepAfter > TimeSpan.Zero)
            {
                await this.SleepAsync(effectiveSleepAfter).ConfigureAwait(false);
            }
        }
    }

    public async Task KeyPressAsync(VirtualKeyCode[] keyCodes, TimeSpan? sleepAfter = default)
    {
        EnsureNotNullOrEmpty(keyCodes);
        await this._keyPressHandler.Execute(new KeyboardKeysCommand(keyCodes)).ConfigureAwait(false);

        var effectiveSleepAfter = sleepAfter.GetValueOrDefault(this._defaultKeyboardSleepAfterDuration);
        if (effectiveSleepAfter > TimeSpan.Zero)
        {
            await this.SleepAsync(effectiveSleepAfter).ConfigureAwait(false);
        }
    }

    public async Task KeyDownAsync(VirtualKeyCode[] keyCodes, TimeSpan? sleepAfter = default)
    {
        EnsureNotNullOrEmpty(keyCodes);
        await this._keyDownHandler.Execute(new KeyboardKeysCommand(keyCodes)).ConfigureAwait(false);

        var effectiveSleepAfter = sleepAfter.GetValueOrDefault(this._defaultKeyboardSleepAfterDuration);
        if (effectiveSleepAfter > TimeSpan.Zero)
        {
            await this.SleepAsync(effectiveSleepAfter).ConfigureAwait(false);
        }
    }

    public async Task KeyUpAsync(VirtualKeyCode[] keyCodes, TimeSpan? sleepAfter = default)
    {
        EnsureNotNullOrEmpty(keyCodes);
        await this._keyUpHandler.Execute(new KeyboardKeysCommand(keyCodes)).ConfigureAwait(false);

        var effectiveSleepAfter = sleepAfter.GetValueOrDefault(this._defaultKeyboardSleepAfterDuration);
        if (effectiveSleepAfter > TimeSpan.Zero)
        {
            await this.SleepAsync(effectiveSleepAfter).ConfigureAwait(false);
        }
    }

    private static void EnsureNotNullOrEmpty(params VirtualKeyCode[] keyCodes)
    {
        if (keyCodes == null)
        {
            throw new ArgumentNullException(nameof(keyCodes));
        }

        if (keyCodes.Length == 0)
        {
            throw new ArgumentException(Messages.OcuNetDriver_Throw_EmptyKeyCodes, nameof(keyCodes));
        }
    }

    public OcuNetDriver SetMouseSpeed(MouseSpeed speed)
    {
        if (!DriverOptions.ValidMouseSpeeds.Contains(speed))
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        this._mouseSpeed = speed;
        return this;
    }

    public OcuNetDriver SetCurrentMonitor(int monitorIndex)
    {
        if (monitorIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorIndex));
        }

        this._monitorIndex = monitorIndex;
        return this;
    }

    public OcuNetDriver SetCurrentMonitor(MonitorDescription monitor)
    {
        return this.SetCurrentMonitor(monitor.Index);
    }

    public OcuNetDriver SetDefaultWaitForDuration(TimeSpan defaultWaitFor)
    {
        if (defaultWaitFor < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultWaitFor), Messages.DriverOptions_Throw_NegativeDefaultWaitForDuration);
        }

        this._defaultWaitForDuration = defaultWaitFor;
        return this;
    }

    public OcuNetDriver SetDefaultKeyboardSleepAfterDuration(TimeSpan defaultKeyboardSleepAfter)
    {
        if (defaultKeyboardSleepAfter < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultKeyboardSleepAfter), Messages.DriverOptions_Throw_NegativeDefaultKeyboardSleepAfterDuration);
        }

        this._defaultKeyboardSleepAfterDuration = defaultKeyboardSleepAfter;
        return this;
    }

    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "It looks more coherent to only use instance methods here.")]
    public Task SleepAsync(int millisecondsDelay) => Task.Delay(millisecondsDelay);

    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "It looks more coherent to only use instance methods here.")]
    public Task SleepAsync(TimeSpan delay) => Task.Delay(delay);

    public void Dispose()
    {
        foreach (var disposable in this._disposables)
        {
            disposable.Dispose();
        }
    }
}
