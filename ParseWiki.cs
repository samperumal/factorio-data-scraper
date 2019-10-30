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

            selectorList.Add(new KeyValuePair<string, Func<IElement, Uri, Task>>("div.infobox div.tabbertab table>tbody", ParseIntermediateResource));
            selectorList.Add(new KeyValuePair<string, Func<IElement, Uri, Task>>("table.wikitable", ParseTableResource));
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
                    break;
                }
            }

            if (tbody == null) Console.WriteLine(url);
        }

        async Task ParseGenericResource(IElement element, Uri uri)
        {
            var td = element.QuerySelector("tr td");

            var resource = await ParseFactorioIcon(td, uri);

            AddProduct(resource);
        }

        async Task ParseIntermediateResource(IElement element, Uri uri)
        {
            var recipeText = element.QuerySelector("tr.border-top td p")?.TextContent?.Trim();

            bool recipeCheck = String.Equals(recipeText, "recipe", StringComparison.InvariantCultureIgnoreCase);
            if (!recipeCheck) throw new InvalidDataException("No Recipe Found!");

            var recipeElements = element.QuerySelector("tr + tr > td.infobox-vrow-value")?.QuerySelectorAll("div.factorio-icon");

            var list = new List<dynamic>();

            foreach (var div in recipeElements)
            {
                var resource = await ParseFactorioIcon(div, uri);
                list.Add(resource);
                AddProduct(resource);
            }

            var output = list.Last();
            dynamic time = null;

            for (int i = 0; i + 1 < list.Count; i++)
            {
                var input = list[i];
                if (input.id == "/Time")
                {
                    time = input;
                    continue;
                }
            }

            var recipe = new
            {
                inputs = list.Where(e => e != output && e != time).Select(RecipePart),
                outputs = new [] { RecipePart(output) },
                time = time?.image?.text
            };

            Recipes.Add(recipe);
        }

        async Task ParseTableResource(IElement element, Uri uri)
        {
            string url = uri.ToString();

            if (url == "https://wiki.factorio.com/Barrel")
            {
                //ParseBarrel(url);
            }
            else if (url == "https://wiki.factorio.com/Solid_fuel")
            {
                //ParseSolidFuel(url);
            }
            else
            {
                // Console.WriteLine(uri);
            }
        }

        internal Task ParseBarrel(string v)
        {
            return null;
        }

        internal Task ParseSolidFuel(string v)
        {
            return null;
        }

        async internal Task ParseOil(string rootUrl)
        {
            var uri = new Uri(rootUrl);
            var document = await LoadFromCache(rootUrl);

            var table = document.QuerySelector("table.wikitable");

            foreach (var row in table?.QuerySelectorAll("tr").Skip(1))
            {
                var columns = row.QuerySelectorAll("td").ToList();
                var process = await ParseFactorioIconImage(columns[0], uri);
                var processText = columns[0].TextContent;

                var inputs = columns[1]
                    .QuerySelectorAll("div.factorio-icon")
                    .Select(async div => await ParseFactorioIcon(div, uri))
                    .Select(t => t.Result)
                    .ToList();

                foreach (var resource in inputs)
                    AddProduct(resource);

                var outputs = columns[2].QuerySelectorAll("div.factorio-icon")
                    .Select(async div => await ParseFactorioIcon(div, uri))
                    .Select(t => t.Result)
                    .ToList();

                foreach (var resource in outputs)
                    AddProduct(resource);

                var recipe = new
                {
                    inputs = inputs.Skip(1).Select(RecipePart),
                    outputs = outputs.Select(RecipePart),
                    time = inputs.First()?.image?.text,
                    process = new {
                        process.imgRelativeUrl,
                        process.imgUrl,
                        text = processText
                    },
                    machine = await ParseFactorioIconImage(columns[3], uri),
                    tech = await ParseFactorioIconImage(columns[4], uri)
                };

                Recipes.Add(recipe);
            }
        }

        dynamic RecipePart(dynamic e) {
            return new { e.id, time = e?.image?.text };
        }

        internal void AddProduct(dynamic resource)
        {
            if (resource != null && resource?.id != null && !Products.ContainsKey(resource.id))
                Products.Add(resource.id, resource);
        }

        async Task<dynamic> ParseFactorioIcon(IElement element, Uri uri)
        {
            var a = element.QuerySelector("a");
            if (a != null)
            {
                var id = a.GetAttribute("href") ?? "";
                var title = a.GetAttribute("title");
                var image = await ParseFactorioIconImage(element, uri);

                return new
                {
                    id,
                    title,
                    image
                };
            }
            else return null;
        }

        async Task<dynamic> ParseFactorioIconImage(IElement element, Uri uri)
        {
            var imgRelativeUrl = element.QuerySelector("img")?.GetAttribute("src");
            var imgUrl = imgRelativeUrl != null ? new Uri(uri, imgRelativeUrl) : null;
            var text = element.QuerySelector("div.factorio-icon-text")?.TextContent;

            if (imgUrl != null)
                await CacheDownload(imgUrl, imgRelativeUrl);

            return new
            {
                imgRelativeUrl,
                imgUrl,
                text
            };
        }

        private async Task CacheDownload(Uri imgUrl, string localPath)
        {
            var path = Path.Combine(CacheDir, "." + localPath);

            var dir = Path.GetDirectoryName(path);

            Directory.CreateDirectory(dir);

            if (!File.Exists(path))
            {
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
        }

        readonly List<KeyValuePair<string, Func<IElement, Uri, Task>>> selectorList;
        private IEnumerable<string> productExclusions;

        public Dictionary<string, dynamic> Products { get; protected set; } = new Dictionary<string, dynamic>();

        public List<dynamic> Recipes { get; protected set; } = new List<dynamic>();
    }
}