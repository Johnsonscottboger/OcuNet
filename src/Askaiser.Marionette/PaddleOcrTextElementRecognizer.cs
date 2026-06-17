using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;

namespace Askaiser.Marionette;

internal sealed class PaddleOcrTextElementRecognizer : IElementRecognizer, IDisposable
{
    private const int UpscalingRatio = 2;

    private static readonly object EngineCreationLock = new object();

    private readonly Guid _defaultEngineId;
    private readonly ConcurrentDictionary<Guid, PaddleOcrAll> _engines;
    private readonly DriverOptions _options;
    private int _activeEngineCount;

    public PaddleOcrTextElementRecognizer(DriverOptions options)
    {
        this._defaultEngineId = Guid.NewGuid();
        this._engines = new ConcurrentDictionary<Guid, PaddleOcrAll>();
        this._options = options;
        this._activeEngineCount = 0;
    }

    public async Task<RecognizerSearchResult> Recognize(Bitmap screenshot, IElement element, CancellationToken token)
    {
        RecognizerSearchResult RecognizeInternal()
        {
            var textElement = (TextElement)element;

            using var preprocessedMat = TextPreprocessor.PreprocessToMat(screenshot, textElement.Options, UpscalingRatio);
            using var transformedScreenshot = BitmapConverter.ToBitmap(preprocessedMat);
            using var paddleInputMat = preprocessedMat.ConvertAndDispose(x => x.ToBGR());

            if (token.IsCancellationRequested)
            {
                return RecognizerSearchResult.NotFound(transformedScreenshot, element);
            }

            Guid engineId = default;

            try
            {
                var newActiveEngineCount = Interlocked.Increment(ref this._activeEngineCount);
                engineId = newActiveEngineCount > 1 ? Guid.NewGuid() : this._defaultEngineId;
                var engine = this._engines.GetOrAdd(engineId, _ =>
                {
                    lock (EngineCreationLock)
                    {
                        return this.CreateEngine();
                    }
                });

                var result = engine.Run(paddleInputMat);

                if (token.IsCancellationRequested)
                {
                    return RecognizerSearchResult.NotFound(transformedScreenshot, element);
                }

                var searchedText = string.Join(" ", textElement.Content.Split().TrimAndRemoveEmptyEntries());
                var locations = MatchRegions(result.Regions, searchedText, textElement.IgnoreCase, this._options.PaddleOcrMinimumScore);

                return token.IsCancellationRequested
                    ? RecognizerSearchResult.NotFound(transformedScreenshot, element)
                    : new RecognizerSearchResult(transformedScreenshot, element, locations.Select(x => TextPreprocessor.Downscale(x, UpscalingRatio)));
            }
            finally
            {
                Interlocked.Decrement(ref this._activeEngineCount);
                if (engineId != this._defaultEngineId && this._engines.TryRemove(engineId, out var engine))
                {
                    engine.Dispose();
                }
            }
        }

        return await Task.Run(RecognizeInternal).ConfigureAwait(false);
    }

    private PaddleOcrAll CreateEngine()
    {
        var model = ResolveModel(this._options.PaddleOcrModel);
        var device = ResolveDevice(this._options.PaddleOcrDevice);
        var engine = new PaddleOcrAll(model, device)
        {
            AllowRotateDetection = this._options.PaddleOcrAllowRotateDetection,
            Enable180Classification = this._options.PaddleOcrEnable180Classification,
        };
        return engine;
    }

    private static FullOcrModel ResolveModel(PaddleOcrModel model)
    {
        return model switch
        {
            PaddleOcrModel.EnglishV5 => LocalFullModels.EnglishV5,
            PaddleOcrModel.ChineseV5 => LocalFullModels.ChineseV5,
            PaddleOcrModel.KoreanV5 => LocalFullModels.KoreanV5,
            PaddleOcrModel.LatinV5 => LocalFullModels.LatinV5,
            PaddleOcrModel.EastSlavicV5 => LocalFullModels.EastSlavicV5,
            PaddleOcrModel.ThaiV5 => LocalFullModels.ThaiV5,
            PaddleOcrModel.GreekV5 => LocalFullModels.GreekV5,
            PaddleOcrModel.CyrillicV5 => LocalFullModels.CyrillicV5,
            PaddleOcrModel.ArabicV5 => LocalFullModels.ArabicV5,
            PaddleOcrModel.DevanagariV5 => LocalFullModels.DevanagariV5,
            PaddleOcrModel.TeluguV5 => LocalFullModels.TeluguV5,
            PaddleOcrModel.TamilV5 => LocalFullModels.TamilV5,
            _ => throw new ArgumentOutOfRangeException(nameof(model), model, null),
        };
    }

    private static Action<PaddleConfig> ResolveDevice(PaddleOcrDevice device)
    {
        return device switch
        {
            PaddleOcrDevice.Mkldnn => PaddleDevice.Mkldnn(),
            _ => PaddleDevice.PlatformDefault,
        };
    }

    private static IEnumerable<Rectangle> MatchRegions(PaddleOcrResultRegion[] regions, string searchedText, bool ignoreCase, float minimumScore)
    {
        if (regions == null)
        {
            yield break;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var region in regions)
        {
            if (region.Score < minimumScore)
            {
                continue;
            }

            var regionText = string.Join(" ", region.Text.Split().TrimAndRemoveEmptyEntries());
            if (string.IsNullOrEmpty(regionText))
            {
                continue;
            }

            if (regionText.IndexOf(searchedText, comparison) < 0)
            {
                continue;
            }

            var rect = region.Rect.BoundingRect();
            yield return new Rectangle(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
    }

    public void Dispose()
    {
        foreach (var engine in this._engines.Values)
        {
            engine.Dispose();
        }
    }
}
