using AngleSharp.Dom;

namespace WebCrawler.Crawlers;

public static class ElementExtensions
{
    public const string NonContentSelectors =
        "script, style, noscript, template, figure, figcaption, " +
        "iframe, img, audio, video, button";

    // Mutates this element. Clone the article first when preserving raw HTML.
    public static IElement RemoveElements(
        this IElement body, string selectors = NonContentSelectors)
    {
        foreach (var element in body.QuerySelectorAll(selectors).ToArray())
        {
            // Keep words separated when an embed sits between text nodes.
            element.Replace(body.Owner!.CreateTextNode(" "));
        }

        return body;
    }
}
