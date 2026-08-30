using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using YTMusicWP.Services;

class Program {
    static async Task Main() {
        InnerTubeClient.Init();
        InnerTubeClient.LoadCookieAuthFromSettings();
        
        var json = await InnerTubeClient.CookieInnerTubePostAsync("browse", new JObject { ["browseId"] = "FEmusic_library_corpus_artists" });
        Console.WriteLine(json.ToString().Substring(0, Math.Min(1000, json.ToString().Length)));
    }
}
