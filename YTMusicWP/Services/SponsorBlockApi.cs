using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using YTMusicWP.Models;

namespace YTMusicWP.Services
{
    internal static class SponsorBlockApi
    {
        private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };

        public static async Task<List<SponsorBlockSegment>> GetSkipSegmentsAsync(string videoId)
        {
            var segments = new List<SponsorBlockSegment>();
            try
            {
                // We only care about sponsor, interaction, selfpromo, and music_offtopic (silence/non-music in MVs)
                string url = $"https://sponsor.ajay.app/api/skipSegments?videoID={videoId}" +
                             "&category=sponsor&category=interaction&category=selfpromo&category=music_offtopic";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "YTMusicWP/1.0");
                
                var response = await _client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var jsonStr = await response.Content.ReadAsStringAsync();
                    JsonArray jsonArray;
                    if (JsonArray.TryParse(jsonStr, out jsonArray))
                    {
                        foreach (var itemVal in jsonArray)
                        {
                            if (itemVal.ValueType != JsonValueType.Object) continue;
                            var item = itemVal.GetObject();
                            
                            var segment = new SponsorBlockSegment();
                            if (item.ContainsKey("actionType") && item["actionType"].ValueType == JsonValueType.String)
                                segment.ActionType = item["actionType"].GetString();
                            
                            if (item.ContainsKey("category") && item["category"].ValueType == JsonValueType.String)
                                segment.Category = item["category"].GetString();
                            
                            if (item.ContainsKey("segment") && item["segment"].ValueType == JsonValueType.Array)
                            {
                                var segArr = item["segment"].GetArray();
                                if (segArr.Count >= 2 && segArr[0].ValueType == JsonValueType.Number && segArr[1].ValueType == JsonValueType.Number)
                                {
                                    segment.Start = segArr[0].GetNumber();
                                    segment.End = segArr[1].GetNumber();
                                    segments.Add(segment);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SponsorBlock Error: " + ex.Message);
            }
            return segments;
        }
    }
}
