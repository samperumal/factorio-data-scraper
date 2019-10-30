using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace factorio
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                var parser = new ParseWiki();
                await parser.ParseRoot("https://wiki.factorio.com/Category:Intermediate_products");
                await parser.ParseOil("https://wiki.factorio.com/Oil_processing");

                File.WriteAllText("products.json", JsonConvert.SerializeObject(new {
                    products = parser.Products.Values,
                    recipes = parser.Recipes
                }, Formatting.Indented));

            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }

            Console.WriteLine("Done!");
        }
    }
}
