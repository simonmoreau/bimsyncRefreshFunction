using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using System.Configuration;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System.Xml.Serialization;
using Microsoft.Extensions.Configuration;

namespace bimsync
{
    public static class bimsyncRefresh
    {
        [FunctionName("bimsyncRefresh")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            log.Info("C# HTTP trigger function processed a request.");

                        IConfigurationRoot configRoot = new ConfigurationBuilder()
    .SetBasePath(context.FunctionAppDirectory)
    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

            // parse query parameter
            string toRefresh = req.Properties
                .FirstOrDefault(q => string.Compare(q.Key, "refresh", true) == 0)
                .Value.ToString();

            // Get request body
            dynamic data = await req.Content.ReadAsAsync<object>();

            // Set name to query string or body data
            toRefresh = toRefresh ?? data?.refresh;

            if (toRefresh == "yes")
            {
                //Find existing token
                TokenWithDate token = ReadToken();

                if (DateTime.Now - token.RefreshDate > new TimeSpan(0,59,00))
                {
                    //Refresh it
                    token = RefreshToken(token);

                    if (token.token.access_token != null)
                    {
                        //Write the new token
                        WriteTokenDown(token);

                        log.Info("The token is now refreshed.");

                        return req.CreateResponse(HttpStatusCode.OK, token.token.access_token);
                    }
                    else
                    {
                        return req.CreateResponse(HttpStatusCode.InternalServerError, "Error while fetching the token");
                    }
                }
                else
                {
                    //Return the current token, still valid
                    return req.CreateResponse(HttpStatusCode.OK, token.token.access_token);
                }
            }
            else if (toRefresh == "start")
            {
                WriteTokenTemp();

                return req.CreateResponse(HttpStatusCode.OK, "The token has been written");
            }
            else
            {
                return req.CreateResponse(HttpStatusCode.OK, "The token has not being refreshed");
            }
        }

        private static void WriteTokenTemp()
        {
            Token token = new Token();

            token.access_token = "P3tbiwGZwgyPQtnRbWuw8x";
            token.token_type = "bearer";
            token.expires_in = 3599;
            token.refresh_token = "HlIEh7o1nFjuYEjUlZZysn";

            TokenWithDate tokenWithDate = new TokenWithDate();
            tokenWithDate.token = token;
            tokenWithDate.RefreshDate = DateTime.Now - new TimeSpan(0,58,0);

            WriteTokenDown(tokenWithDate);
        }


        public static TokenWithDate RefreshToken(TokenWithDate token,IConfigurationRoot configRoot)
        {

            string client_id = configRoot["client_id"];
            string client_secret = configRoot["client_secret"];
            //string client_id = GetEnvironmentVariable("client_id", EnvironmentVariableTarget.Process);
            //string client_secret = GetEnvironmentVariable("client_secret", EnvironmentVariableTarget.Process);

            HttpClient client = new HttpClient();
            client.BaseAddress = new System.Uri("https://api.bimsync.com");

            //Refresh token
            RestRequest refrechTokenRequest = new RestRequest("oauth2/token", Method.POST);
            //refrechTokenRequest.AddHeader("Authorization", "Bearer " + token.access_token);

            HttpWebRequest request = new HttpWebRequest();

            refrechTokenRequest.AddParameter("refresh_token", token.token.refresh_token);
            refrechTokenRequest.AddParameter("grant_type", "refresh_token");
            refrechTokenRequest.AddParameter("client_id", client_id);
            refrechTokenRequest.AddParameter("client_secret", client_secret);

            IRestResponse<Token> responseToken = client.Execute<Token>(refrechTokenRequest);

            if (responseToken.ErrorException != null)
            {
                string message = "Error retrieving your access token. " + responseToken.ErrorException.Message;
                return new TokenWithDate();
            }

            TokenWithDate newToken = new TokenWithDate();
            newToken.token = responseToken.Data;
            newToken.RefreshDate = DateTime.Now;
            return newToken;
        }

        public static void WriteTokenDown(TokenWithDate token)
        {
            var folder = Environment.ExpandEnvironmentVariables(@"%HOME%\data\MyFunctionAppData");
            var fullPath = Path.Combine(folder, "tokenFile.txt");
            Directory.CreateDirectory(folder); // noop if it already exists

            TextWriter writer = null;
            try
            {
                var serializer = new XmlSerializer(typeof(TokenWithDate));
                writer = new StreamWriter(fullPath, false);
                serializer.Serialize(writer, token);
            }
            finally
            {
                if (writer != null)
                    writer.Close();
            }
        }

        public static TokenWithDate ReadToken()
        {
            var folder = Environment.ExpandEnvironmentVariables(@"%HOME%\data\MyFunctionAppData");
            var fullPath = Path.Combine(folder, "tokenFile.txt");
            Directory.CreateDirectory(folder); // noop if it already exists

            TextReader reader = null;
            try
            {
                var serializer = new XmlSerializer(typeof(TokenWithDate));
                reader = new StreamReader(fullPath);
                return (TokenWithDate)serializer.Deserialize(reader);
            }
            finally
            {
                if (reader != null)
                    reader.Close();
            }
        }

    }

    [SettingsSerializeAs(SettingsSerializeAs.Xml)]
    public class Token
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public string refresh_token { get; set; }
        
    }

    public class TokenWithDate
    {
        public Token token { get; set; }
        public DateTime RefreshDate { get; set; }
    }
}