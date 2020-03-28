using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using LiteDB;
using Newtonsoft.Json.Linq;

namespace WhosLive
{
    class Twitch
    {
        public const string Endpoint = "https://api.twitch.tv/helix/";
        public const string DefaultStreamMessage = "{0} is currently live at <{1}>!";

        public static class HelixStrings
        {
            public const string Users = "users";
            public const string Streams = "streams";
            public const string Games = "games";
        }

        public Task<Dictionary<string, JToken>> TwitchQuery(string helixQuery, string login)
        {
            string query;
            switch (helixQuery)
            {
                case HelixStrings.Streams:
                    query = "?user_login";
                    break;
                case HelixStrings.Users:
                    query = "?login";
                    break;
                case HelixStrings.Games:
                    query = "?id";
                    break;

                default:
                    return null;
            }

            var helix = $"{Endpoint}{helixQuery}{query}={login}";
            WebResponse response = null;
            lock (this)
            {

                WebRequest request = WebRequest.Create(helix);
                request.Headers.Add("Client-ID", Program.TwitchClientId);

                response = request.GetResponseAsync().Result;
            }

            if ((response as HttpWebResponse)?.StatusCode != HttpStatusCode.OK)
            {
                response.Close();
                return null;
            }

            string content = "";
            using (Stream stream = response.GetResponseStream())
            {
                StreamReader sr = new StreamReader(stream);

                content = sr.ReadToEndAsync().Result;

                sr.Dispose();
            }

            response.Close();


            if (!(JObject.Parse(content)["data"] is JArray dataArr))
            {
                return null;
            }

            if (!dataArr.HasValues)
            {
                return null;
            }

            var dict = dataArr.First.Children<JProperty>().ToDictionary(k => k.Name, v => v.Value);

            if (dict.Count < 1)
                dict = null;

            return Task.FromResult(dict);

        }

        public Task<bool> IsUserValid(string login)
        {
            if (string.IsNullOrWhiteSpace(login))
                return Task.FromResult(false);
            Dictionary<string, JToken> query = TwitchQuery(HelixStrings.Users, login).Result;

            if (query == null)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
    }
}
