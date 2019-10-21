using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;

namespace factorio
{
    public class ParseWiki
    {
        public string CacheDir { get; set; } = "cache";

        public ParseWiki()
        {
            selectorList = new List<KeyValuePair<string, Func<IElement, Uri, Task>>>();

            selectorList.Add(new KeyValuePair<string, Func<IElement, Uri, Task>>("div.infobox div.tabbertab table>tbody", null));
            selectorList.Add(new KeyValuePair<string, Func<IElement, Uri, Task>>("table.wikitable", null));
            selectorList.Add(new KeyValuePair<string, Func<IElement, Uri, Task>>("div.infobox table>tbody", ParseGenericResource));
        }

        async Task<IDocument> LoadFromCache(string url)
        {
            var uri = new Uri(url);

            var localPath = Path.ChangeExtension(Path.Combine(CacheDir, "." + uri.LocalPath), ".html");
            localPath = localPath.Replace(":", "_");

            var localDir = Path.GetDirectoryName(localPath);

            if (!Directory.Exists(localDir))
                Directory.CreateDirectory(localDir);

            var config = Configuration.Default.WithDefaultLoader();

            var context = BrowsingContext.New(config);

            if (!File.Exists(localPath))
            {
                var document = await context.OpenAsync(url);

                File.WriteAllText(localPath, document.DocumentElement.OuterHtml);

                return document;
            }
            else
            {
                return await context.OpenAsync(req => req.Content(File.ReadAllText(localPath)));
            }
        }

        internal async Task ParseRoot(string rootUrl)
        {
            LoadExclusions();

            var document = await LoadFromCache(rootUrl);

            var links = document.QuerySelectorAll("div.mw-category a");

            var rootUri = new Uri(rootUrl);

            foreach (var link in links)
            {
                var href = link.GetAttribute("href");
                var uri = new Uri(rootUri, href);
                await ParseProduct(uri);
            }
        }

        private void LoadExclusions()
        {
            productExclusions = File.ReadAllLines("productExclusions.txt");
        }

        private async Task ParseProduct(Uri uri)
        {
            var url = uri.ToString();

            if (productExclusions.Contains(url)) return;

            var document = await LoadFromCache(url);

            if (url == "https://wiki.factorio.com/Steam")
            {
                // Handle steam differently
                return;
            }

            IElement tbody = null;

            foreach (var selector in selectorList)
            {
                tbody = document.QuerySelector(selector.Key);
                if (tbody != null)
                {
                    if (selector.Value != null)
                    {
                        await selector.Value(tbody, uri);
                    }
                    //Console.WriteLine("{0} : {1}", selector.Key, url);
                    break;
                }
            }

            if (tbody != null)
            {
                //Console.WriteLine(tbody.InnerHtml);                
            }
            else Console.WriteLine(url);
        }

        public async Task ParseGenericResource(IElement element, Uri uri)
        {
            var a = element.QuerySelector("tr td div.factorio-icon a");

            if (a != null)
            {
                var id = a.GetAttribute("href");
                var title = a.GetAttribute("title");
                var imgRelativeUrl = a.QuerySelector("img")?.GetAttribute("src");
                var imgUrl = imgRelativeUrl != null ? new Uri(uri, imgRelativeUrl) : null;

                if (imgUrl != null)
                    await CacheDownload(imgUrl, imgRelativeUrl);

                Products.Add(new
                {
                    id,
                    title,
                    imgRelativeUrl,
                    imgUrl
                });
            }
            Console.WriteLine(uri);
        }

        private async Task CacheDownload(Uri imgUrl, string localPath)
        {
            var path = Path.Combine(CacheDir, "." + localPath);

            var dir = Path.GetDirectoryName(path);

            Directory.CreateDirectory(dir);

            using (var client = new HttpClient())
            {
                using (var response = await client.GetAsync(imgUrl))
                {
                    using (var source = await response.Content.ReadAsStreamAsync())
                    {
                        using (var target = File.OpenWrite(path))
                        {
                            await source.CopyToAsync(target);
                        }
                    }
                }
            }

        }

        readonly List<KeyValuePair<string, Func<IElement, Uri, Task>>> selectorList;
        private IEnumerable<string> productExclusions;

        public List<dynamic> Products {get; protected set;} = new List<dynamic>();
    }
}