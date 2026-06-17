using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;

namespace OcuNet;

public static class OcuNetDriverExtensions
{
    #region Mouse interaction with the first available element of a collection

    public static async Task MoveToAnyAsync(this OcuNetDriver driver, IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAnyAsync(elements, waitFor, searchRect).ConfigureAwait(false);
        await MoveToAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task SingleClickAnyAsync(this OcuNetDriver driver, IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAnyAsync(elements, waitFor, searchRect).ConfigureAwait(false);
        await SingleClickAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task DoubleClickAnyAsync(this OcuNetDriver driver, IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAnyAsync(elements, waitFor, searchRect).ConfigureAwait(false);
        await DoubleClickAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task TripleClickAnyAsync(this OcuNetDriver driver, IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAnyAsync(elements, waitFor, searchRect).ConfigureAwait(false);
        await TripleClickAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task RightClickAnyAsync(this OcuNetDriver driver, IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAnyAsync(elements, waitFor, searchRect).ConfigureAwait(false);
        await RightClickAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task DragFromAnyAsync(this OcuNetDriver driver, IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAnyAsync(elements, waitFor, searchRect).ConfigureAwait(false);
        await DragFromAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task DropToAnyAsync(this OcuNetDriver driver, IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAnyAsync(elements, waitFor, searchRect).ConfigureAwait(false);
        await DropToAsync(driver, result).ConfigureAwait(false);
    }

    #endregion

    #region Mouse interaction with points

    public static async Task MoveToAsync(this OcuNetDriver driver, Point coordinates)
    {
        var (x, y) = coordinates;
        await driver.MoveToAsync(x, y).ConfigureAwait(false);
    }

    public static async Task SingleClickAsync(this OcuNetDriver driver, Point coordinates)
    {
        var (x, y) = coordinates;
        await driver.SingleClickAsync(x, y).ConfigureAwait(false);
    }

    public static async Task DoubleClickAsync(this OcuNetDriver driver, Point coordinates)
    {
        var (x, y) = coordinates;
        await driver.DoubleClickAsync(x, y).ConfigureAwait(false);
    }

    public static async Task TripleClickAsync(this OcuNetDriver driver, Point coordinates)
    {
        var (x, y) = coordinates;
        await driver.TripleClickAsync(x, y).ConfigureAwait(false);
    }

    public static async Task RightClickAsync(this OcuNetDriver driver, Point coordinates)
    {
        var (x, y) = coordinates;
        await driver.RightClickAsync(x, y).ConfigureAwait(false);
    }

    public static async Task DragFromAsync(this OcuNetDriver driver, Point coordinates)
    {
        var (x, y) = coordinates;
        await driver.DragFromAsync(x, y).ConfigureAwait(false);
    }

    public static async Task DropToAsync(this OcuNetDriver driver, Point coordinates)
    {
        var (x, y) = coordinates;
        await driver.DropToAsync(x, y).ConfigureAwait(false);
    }

    #endregion

    #region Mouse interaction with search result

    public static async Task MoveToAsync(this OcuNetDriver driver, SearchResult searchResult) => await MoveToAsync(driver, searchResult.Location.Center).ConfigureAwait(false);

    public static async Task SingleClickAsync(this OcuNetDriver driver, SearchResult searchResult) => await SingleClickAsync(driver, searchResult.Location.Center).ConfigureAwait(false);

    public static async Task DoubleClickAsync(this OcuNetDriver driver, SearchResult searchResult) => await DoubleClickAsync(driver, searchResult.Location.Center).ConfigureAwait(false);

    public static async Task TripleClickAsync(this OcuNetDriver driver, SearchResult searchResult) => await TripleClickAsync(driver, searchResult.Location.Center).ConfigureAwait(false);

    public static async Task RightClickAsync(this OcuNetDriver driver, SearchResult searchResult) => await RightClickAsync(driver, searchResult.Location.Center).ConfigureAwait(false);

    public static async Task DragFromAsync(this OcuNetDriver driver, SearchResult searchResult) => await DragFromAsync(driver, searchResult.Location.Center).ConfigureAwait(false);

    public static async Task DropToAsync(this OcuNetDriver driver, SearchResult searchResult) => await DropToAsync(driver, searchResult.Location.Center).ConfigureAwait(false);

    #endregion

    #region Mouse interaction with an element

    public static async Task MoveToAsync(this OcuNetDriver driver, IElement element, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAsync(element, waitFor, searchRect).ConfigureAwait(false);
        await MoveToAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task SingleClickAsync(this OcuNetDriver driver, IElement element, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAsync(element, waitFor, searchRect).ConfigureAwait(false);
        await SingleClickAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task DoubleClickAsync(this OcuNetDriver driver, IElement element, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAsync(element, waitFor, searchRect).ConfigureAwait(false);
        await DoubleClickAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task TripleClickAsync(this OcuNetDriver driver, IElement element, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAsync(element, waitFor, searchRect).ConfigureAwait(false);
        await TripleClickAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task RightClickAsync(this OcuNetDriver driver, IElement element, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAsync(element, waitFor, searchRect).ConfigureAwait(false);
        await RightClickAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task DragFromAsync(this OcuNetDriver driver, IElement element, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAsync(element, waitFor, searchRect).ConfigureAwait(false);
        await DragFromAsync(driver, result).ConfigureAwait(false);
    }

    public static async Task DropToAsync(this OcuNetDriver driver, IElement element, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAsync(element, waitFor, searchRect).ConfigureAwait(false);
        await DropToAsync(driver, result).ConfigureAwait(false);
    }

    #endregion

    #region Element visibility

    public static async Task<bool> IsVisibleAsync(this OcuNetDriver driver, IElement element, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAsync(element, waitFor, searchRect, NoSingleResultBehavior.Ignore).ConfigureAwait(false);
        return result.Success;
    }

    public static async Task<bool> IsAnyVisibleAsync(this OcuNetDriver driver, IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAnyAsync(elements, waitFor, searchRect, NoSingleResultBehavior.Ignore).ConfigureAwait(false);
        return result.Success;
    }

    public static async Task<bool> AreAllVisibleAsync(this OcuNetDriver driver, IEnumerable<IElement> elements, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAllAsync(elements, waitFor, searchRect, NoSingleResultBehavior.Ignore).ConfigureAwait(false);
        return result.All(x => x.Success);
    }

    #endregion

    #region Key press with single key code

    public static async Task KeyPressAsync(this OcuNetDriver driver, VirtualKeyCode keyCode, TimeSpan? sleepAfter = default)
    {
        await driver.KeyPressAsync(new[] { keyCode }, sleepAfter).ConfigureAwait(false);
    }

    public static async Task KeyDownAsync(this OcuNetDriver driver, VirtualKeyCode keyCode, TimeSpan? sleepAfter = default)
    {
        await driver.KeyDownAsync(new[] { keyCode }, sleepAfter).ConfigureAwait(false);
    }

    public static async Task KeyUpAsync(this OcuNetDriver driver, VirtualKeyCode keyCode, TimeSpan? sleepAfter = default)
    {
        await driver.KeyUpAsync(new[] { keyCode }, sleepAfter).ConfigureAwait(false);
    }

    #endregion

    #region Text-based actions

    public static async Task<SearchResult> WaitForAsync(this OcuNetDriver driver, string text, TimeSpan? waitFor = default, Rectangle? searchRect = default, TextOptions textOptions = TextOptions.BlackAndWhite)
    {
        return await driver.WaitForAsync(new TextElement(text, textOptions), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task MoveToAsync(this OcuNetDriver driver, string text, TimeSpan? waitFor = default, Rectangle? searchRect = default, TextOptions textOptions = TextOptions.BlackAndWhite)
    {
        await MoveToAsync(driver, new TextElement(text, textOptions), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task SingleClickAsync(this OcuNetDriver driver, string text, TimeSpan? waitFor = default, Rectangle? searchRect = default, TextOptions textOptions = TextOptions.BlackAndWhite)
    {
        await SingleClickAsync(driver, new TextElement(text, textOptions), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task DoubleClickAsync(this OcuNetDriver driver, string text, TimeSpan? waitFor = default, Rectangle? searchRect = default, TextOptions textOptions = TextOptions.BlackAndWhite)
    {
        await DoubleClickAsync(driver, new TextElement(text, textOptions), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task TripleClickAsync(this OcuNetDriver driver, string text, TimeSpan? waitFor = default, Rectangle? searchRect = default, TextOptions textOptions = TextOptions.BlackAndWhite)
    {
        await TripleClickAsync(driver, new TextElement(text, textOptions), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task RightClickAsync(this OcuNetDriver driver, string text, TimeSpan? waitFor = default, Rectangle? searchRect = default, TextOptions textOptions = TextOptions.BlackAndWhite)
    {
        await RightClickAsync(driver, new TextElement(text, textOptions), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task DragFromAsync(this OcuNetDriver driver, string text, TimeSpan? waitFor = default, Rectangle? searchRect = default, TextOptions textOptions = TextOptions.BlackAndWhite)
    {
        await DragFromAsync(driver, new TextElement(text, textOptions), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task DropToAsync(this OcuNetDriver driver, string text, TimeSpan? waitFor = default, Rectangle? searchRect = default, TextOptions textOptions = TextOptions.BlackAndWhite)
    {
        await DropToAsync(driver, new TextElement(text, textOptions), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task<bool> IsVisibleAsync(this OcuNetDriver driver, string text, TimeSpan? waitFor = default, Rectangle? searchRect = default, TextOptions textOptions = TextOptions.BlackAndWhite)
    {
        return await IsVisibleAsync(driver, new TextElement(text, textOptions), waitFor, searchRect).ConfigureAwait(false);
    }

    #endregion

    #region System.Drawing.Image-based actions

    public static async Task<SearchResult> WaitForAsync(this OcuNetDriver driver, Image image, TimeSpan? waitFor = default, Rectangle? searchRect = default, decimal threshold = ImageElement.DefaultThreshold, bool grayscale = false)
    {
        return await driver.WaitForAsync(new ImageElement("image", image.GetBytes(ImageFormat.Png), threshold, grayscale), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task MoveToAsync(this OcuNetDriver driver, Image image, TimeSpan? waitFor = default, Rectangle? searchRect = default, decimal threshold = ImageElement.DefaultThreshold, bool grayscale = false)
    {
        await MoveToAsync(driver, new ImageElement("image", image.GetBytes(ImageFormat.Png), threshold, grayscale), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task SingleClickAsync(this OcuNetDriver driver, Image image, TimeSpan? waitFor = default, Rectangle? searchRect = default, decimal threshold = ImageElement.DefaultThreshold, bool grayscale = false)
    {
        await SingleClickAsync(driver, new ImageElement("image", image.GetBytes(ImageFormat.Png), threshold, grayscale), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task DoubleClickAsync(this OcuNetDriver driver, Image image, TimeSpan? waitFor = default, Rectangle? searchRect = default, decimal threshold = ImageElement.DefaultThreshold, bool grayscale = false)
    {
        await DoubleClickAsync(driver, new ImageElement("image", image.GetBytes(ImageFormat.Png), threshold, grayscale), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task TripleClickAsync(this OcuNetDriver driver, Image image, TimeSpan? waitFor = default, Rectangle? searchRect = default, decimal threshold = ImageElement.DefaultThreshold, bool grayscale = false)
    {
        await TripleClickAsync(driver, new ImageElement("image", image.GetBytes(ImageFormat.Png), threshold, grayscale), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task RightClickAsync(this OcuNetDriver driver, Image image, TimeSpan? waitFor = default, Rectangle? searchRect = default, decimal threshold = ImageElement.DefaultThreshold, bool grayscale = false)
    {
        await RightClickAsync(driver, new ImageElement("image", image.GetBytes(ImageFormat.Png), threshold, grayscale), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task DragFromAsync(this OcuNetDriver driver, Image image, TimeSpan? waitFor = default, Rectangle? searchRect = default, decimal threshold = ImageElement.DefaultThreshold, bool grayscale = false)
    {
        await DragFromAsync(driver, new ImageElement("image", image.GetBytes(ImageFormat.Png), threshold, grayscale), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task DropToAsync(this OcuNetDriver driver, Image image, TimeSpan? waitFor = default, Rectangle? searchRect = default, decimal threshold = ImageElement.DefaultThreshold, bool grayscale = false)
    {
        await DropToAsync(driver, new ImageElement("image", image.GetBytes(ImageFormat.Png), threshold, grayscale), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task<bool> IsVisibleAsync(this OcuNetDriver driver, Image image, TimeSpan? waitFor = default, Rectangle? searchRect = default, decimal threshold = ImageElement.DefaultThreshold, bool grayscale = false)
    {
        return await IsVisibleAsync(driver, new ImageElement("image", image.GetBytes(ImageFormat.Png), threshold, grayscale), waitFor, searchRect).ConfigureAwait(false);
    }

    #endregion

    #region Finding elements locations without throwing exceptions

    public static async Task<Rectangle[]> FindLocationsAsync(this OcuNetDriver driver, IElement element, TimeSpan? waitFor = default, Rectangle? searchRect = default)
    {
        var result = await driver.WaitForAsync(element, waitFor, searchRect, NoSingleResultBehavior.Ignore).ConfigureAwait(false);
        return result.Locations.ToArray();
    }

    public static async Task<Rectangle[]> FindLocationsAsync(this OcuNetDriver driver, string text, TimeSpan? waitFor = default, Rectangle? searchRect = default, TextOptions textOptions = TextOptions.BlackAndWhite)
    {
        return await FindLocationsAsync(driver, new TextElement(text, textOptions), waitFor, searchRect).ConfigureAwait(false);
    }

    public static async Task<Rectangle[]> FindLocationsAsync(this OcuNetDriver driver, Image image, TimeSpan? waitFor = default, Rectangle? searchRect = default, decimal threshold = ImageElement.DefaultThreshold, bool grayscale = false)
    {
        return await FindLocationsAsync(driver, new ImageElement("image", image.GetBytes(ImageFormat.Png), threshold, grayscale), waitFor, searchRect).ConfigureAwait(false);
    }

    #endregion
}
