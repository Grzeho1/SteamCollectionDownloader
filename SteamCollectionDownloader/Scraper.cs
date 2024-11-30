using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SteamDownloader
{
    public class Scraper
    {

        public static async Task<List<string>> ExtractWorkshopIDs(string collectionUrl)
        {
            var ids = new List<string>();

            using (HttpClient client = new HttpClient())
            {

                Console.WriteLine("Downloading ids from collection");
                var html = await client.GetStringAsync(collectionUrl);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // hledaní id v html
                var nodes = doc.DocumentNode.SelectNodes("//div[@class='collectionItem']/@id");

                // hledaní gameId v html
                var gameIdNode = doc.DocumentNode.SelectSingleNode("//a[contains(@href, 'app/')]");

                string gameId = gameIdNode != null ? gameIdNode.GetAttributeValue("href", "").Split('/').Last().Split('?')[0] : "";

                if (nodes != null && !string.IsNullOrEmpty(gameId))
                {
                    foreach (var node in nodes)
                    {
                        string idValue = node.GetAttributeValue("id", "");

                        if (idValue.StartsWith("sharedfile_"))
                        {
                            // osekat prefix a extrahovat jen id
                            string formattedId = $"{gameId} {idValue.Replace("sharedfile_", "")}";
                            ids.Add(formattedId);

                        }
                    }
                }
                else
                {
                    Console.WriteLine("No id in collection.");
                }


            }

            return ids;
        }
    }
}
