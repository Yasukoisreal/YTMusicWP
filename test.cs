using System;
using Newtonsoft.Json.Linq;

class Program {
    static void Main() {
        string json = "{ \"value\": [ { \"type\": \"song\" } ] }";
        var token = JToken.Parse(json);
        if (token is JObject) {
            var obj = (JObject)token;
            var arr = obj["value"];
            Console.WriteLine(arr is JArray);
            if (arr is JArray) Console.WriteLine("It is JArray!");
        }
    }
}
