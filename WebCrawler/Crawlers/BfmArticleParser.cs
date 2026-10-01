using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace WebCrawler.Crawlers;

public record BfmArticle(string Title, string About, string Content);

public static class BfmArticleParser
{
    public static BfmArticle Parse(IDocument document)
    {
        var article = document.QuerySelector(".current-article")
            ?? throw new InvalidOperationException("BFM article body was not found.");
        var title = Normalize(document.QuerySelector("h1.news-title")?.TextContent ?? "");
        var about = Normalize(article.QuerySelector(".about-article")?.TextContent ?? "");

        // Work on a copy so callers can still use the original HTML.
        var body = (IElement)article.Clone(true);
        foreach (var element in body.QuerySelectorAll(
                     ".about-article, script, style, noscript, figure, figcaption, " +
                     "iframe, img, audio, video, button, .adriverBanner, " +
                     ".banner-mobile-wrap, [id^='adfox_'], [id^='bn6_'], #video_banner_in_content"))
        {
            // Embeds may sit between two sentences inside a single paragraph.
            element.Replace(body.Owner!.CreateTextNode(" "));
        }

        var text = new StringBuilder();
        AppendText(body, text);
        var paragraphs = text.ToString().Split('\n')
            .Select(Normalize)
            .Where(paragraph => paragraph.Length > 0);

        return new BfmArticle(title, about, string.Join("\n\n", paragraphs));
    }

    private static void AppendText(INode node, StringBuilder text)
    {
        if (node is IText)
        {
            // HTML source whitespace is not a paragraph boundary.
            text.Append(Regex.Replace(node.TextContent, @"\s+", " "));
            return;
        }

        if (node is not IElement element)
            return;

        var block = element.LocalName is "p" or "div" or "blockquote" or "li"
            or "h2" or "h3" or "h4" or "ul" or "ol" or "br";
        if (block)
            text.Append('\n');

        foreach (var child in element.ChildNodes)
            AppendText(child, text);

        if (block)
            text.Append('\n');
    }

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
