using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace WebCrawler.Crawlers;

public static class BfmArticleParser
{
    public const string ExcludedSelectors =
        ".about-article, .adriverBanner, .banner-mobile-wrap, " +
        "[id^='adfox_'], [id^='bn6_'], #video_banner_in_content";
}
