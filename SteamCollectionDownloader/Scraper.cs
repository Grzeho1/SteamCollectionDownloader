using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SteamDownloader
{
    public class Scraper
    {
        public static async Task<List<(string gameId, string workshopId, string itemName)>> ExtractWorkshopIDs(string collectionUrl)
        {
            var ids = new List<(string gameId, string workshopId, string itemName)>();

            using (HttpClient client = new HttpClient())
            {
                try
                {
                    var html = await client.GetStringAsync(collectionUrl);
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var gameIdNode = doc.DocumentNode.SelectSingleNode("//a[contains(@href, 'app/')]");
                    string gameId = gameIdNode != null ? gameIdNode.GetAttributeValue("href", "").Split('/').Last().Split('?')[0] : "";

                    var nodes = doc.DocumentNode.SelectNodes("//div[@class='collectionItem']");
                    if (nodes != null && !string.IsNullOrEmpty(gameId))
                    {
                        foreach (var node in nodes)
                        {
                            string idValue = node.GetAttributeValue("id", "");
                            if (idValue.StartsWith("sharedfile_"))
                            {
                                string workshopId = idValue.Replace("sharedfile_", "");
                                var nameNode = node.SelectSingleNode(".//div[@class='workshopItemTitle']");
                                string itemName = nameNode != null ? nameNode.InnerText.Trim() : "Unknown Item";
                                ids.Add((gameId, workshopId, itemName));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to fetch collection: {ex.Message}");
                }

                return ids;
            }
        }
    }
}