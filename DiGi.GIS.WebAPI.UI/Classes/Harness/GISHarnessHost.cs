using DiGi.Harness.Classes;
using DiGi.Harness.Interfaces;
using DiGi.Harness.WebAPI.Classes;
using DiGi.Harness.WebAPI.Interfaces;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DiGi.GIS.WebAPI.UI.Classes
{
    /// <summary>
    /// The GIS side of the AI assistant (DiGi.GIS.WebAPI.UI#88): who the user is, the GIS Web API tools bound to that user's token, and the GIS system prompt. Registered as a singleton.
    /// <para>The user is the email of the session behind the <c>digi_user_token</c> cookie, checked on every request against <see cref="Constants.Default.UserSessionUri"/>, so a cookie the user service no longer accepts gets no chat. The tools call <see cref="Constants.Default.GISWebAPIUri"/> with that same token, so the assistant can read only what the user could read.</para>
    /// <para>The tools see a filtered copy of <see cref="Constants.Default.GISSwaggerUri"/>: <c>/gis/</c> paths only, every GET, and only the POST endpoints listed in <see cref="Constants.Default.HarnessGISFileName"/> (read-only queries). The document is fetched on first use and refreshed after <see cref="Constants.Default.HarnessSwaggerRefreshMinutes"/>; a failed refresh keeps the previous copy.</para>
    /// </summary>
    public sealed class GISHarnessHost : IHarnessHost, IDisposable
    {
        private readonly IHttpClientFactory httpClientFactory;
        private readonly TimeProvider timeProvider;
        private readonly SemaphoreSlim semaphoreSlim = new(1, 1);

        private Tuple<JsonObject?, string, DateTimeOffset>? document;

        /// <summary>
        /// Initializes a new instance of the <see cref="GISHarnessHost"/> class.
        /// </summary>
        /// <param name="httpClientFactory">The factory of the HTTP clients used for the user check, the swagger and the tool calls.</param>
        /// <param name="timeProvider">The clock deciding when the swagger is refreshed.</param>
        public GISHarnessHost(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
        {
            this.httpClientFactory = httpClientFactory;
            this.timeProvider = timeProvider;
        }

        /// <summary>
        /// Releases the lock guarding the swagger refresh; called by the service provider when the host shuts down.
        /// </summary>
        public void Dispose()
        {
            semaphoreSlim.Dispose();
        }

        /// <summary>
        /// Resolves the signed-in user from the <c>digi_user_token</c> cookie by asking the user service for the session behind it.
        /// </summary>
        /// <param name="httpContext">The request.</param>
        /// <param name="cancellationToken">The token that cancels the lookup.</param>
        /// <returns>The user (id and display name are the session email, the credential is the token), or null when there is no cookie or the service does not accept it.</returns>
        public async Task<HarnessUser?> UserAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
        {
            string? tokenString = httpContext?.Request.Cookies[Constants.Default.UserTokenCookieName];
            if (string.IsNullOrWhiteSpace(tokenString))
            {
                return null;
            }

            HttpClient httpClient = httpClientFactory.CreateClient();
            WebAPIResponse? webAPIResponse = await httpClient.ResponseAsync(HttpMethod.Get, Constants.Default.UserSessionUri, tokenString, cancellationToken);
            if (webAPIResponse is null || webAPIResponse.StatusCode != StatusCodes.Status200OK || string.IsNullOrWhiteSpace(webAPIResponse.Json))
            {
                return null;
            }

            string? email = null;
            try
            {
                if (JsonNode.Parse(webAPIResponse.Json) is JsonObject jsonObject)
                {
                    foreach (KeyValuePair<string, JsonNode?> keyValuePair in jsonObject)
                    {
                        if (string.Equals(keyValuePair.Key, "email", StringComparison.OrdinalIgnoreCase) && keyValuePair.Value is JsonValue jsonValue && jsonValue.TryGetValue(out string? email_Temp))
                        {
                            email = email_Temp;
                            break;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(email) ? null : new HarnessUser(email, email, tokenString);
        }

        /// <summary>
        /// Creates the GIS tools for one turn: <c>describe_endpoint</c> and <c>call_endpoint</c> on the filtered GIS document, calling with the user's token.
        /// </summary>
        /// <param name="httpContext">The request.</param>
        /// <param name="harnessUser">The signed-in user; <see cref="HarnessUser.Credential"/> is the bearer token of the tool calls.</param>
        /// <param name="cancellationToken">The token that cancels the setup.</param>
        /// <returns>The two tools.</returns>
        public async Task<IReadOnlyList<ITool>> ToolsAsync(HttpContext httpContext, HarnessUser harnessUser, CancellationToken cancellationToken = default)
        {
            JsonObject? swagger_Current = (await DocumentAsync(cancellationToken)).Swagger;

            HashSet<string> postPathTemplates = await PostPathTemplatesAsync(cancellationToken);

            return
            [
                new DescribeEndpointTool(swagger_Current, Constants.Default.HarnessPathPrefix),
                new CallEndpointTool(new Uri(Constants.Default.GISWebAPIUri), Constants.Default.HarnessPathPrefix, swagger_Current, postPathTemplates, harnessUser?.Credential, httpClientFactory.CreateClient()),
            ];
        }

        /// <summary>
        /// Builds the system prompt: the GIS rules from <see cref="Constants.Default.HarnessGISPromptFileName"/> followed by the index of the callable endpoints.
        /// </summary>
        /// <param name="cancellationToken">The token that cancels the build.</param>
        /// <returns>The system prompt.</returns>
        public async Task<string?> SystemPromptAsync(CancellationToken cancellationToken = default)
        {
            return (await DocumentAsync(cancellationToken)).SystemPrompt;
        }

        private async Task<(JsonObject? Swagger, string? SystemPrompt)> DocumentAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            Tuple<JsonObject?, string, DateTimeOffset>? document_Current = document;
            if (document_Current is not null && now - document_Current.Item3 < TimeSpan.FromMinutes(Constants.Default.HarnessSwaggerRefreshMinutes))
            {
                return (document_Current.Item1, document_Current.Item2);
            }

            await semaphoreSlim.WaitAsync(cancellationToken);
            try
            {
                document_Current = document;
                if (document_Current is not null && now - document_Current.Item3 < TimeSpan.FromMinutes(Constants.Default.HarnessSwaggerRefreshMinutes))
                {
                    return (document_Current.Item1, document_Current.Item2);
                }

                JsonObject? swagger = document_Current?.Item1;

                JsonObject? swagger_Fetched = null;
                try
                {
                    HttpClient httpClient = httpClientFactory.CreateClient();
                    string json = await httpClient.GetStringAsync(Constants.Default.GISSwaggerUri, cancellationToken);
                    swagger_Fetched = JsonNode.Parse(json) as JsonObject;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
                {
                    Serilog.Modify.Log(Serilog.Enums.LogEventLevel.Warning, "GISHarnessHost: {Uri} could not be read ({Error}); keeping the previous copy.", Constants.Default.GISSwaggerUri, exception.GetType().Name);
                }

                if (swagger_Fetched is not null)
                {
                    swagger = Filtered(swagger_Fetched, await PostPathTemplatesAsync(cancellationToken));
                }

                string prompt = await PromptAsync(cancellationToken);
                string? index = Harness.Create.OpenApiIndex(swagger, Constants.Default.HarnessPathPrefix);

                StringBuilder stringBuilder = new(prompt);
                stringBuilder.Append("\n\n## API index\n\n");
                stringBuilder.Append(index ?? "The API index could not be loaded. Tell the user the GIS Web API is not reachable right now and do not guess figures.");

                string systemPrompt = stringBuilder.ToString();

                // A document that could not be fetched is retried on the next turn rather than after the refresh interval.
                document = new Tuple<JsonObject?, string, DateTimeOffset>(swagger, systemPrompt, swagger_Fetched is null ? DateTimeOffset.MinValue : now);

                return (swagger, systemPrompt);
            }
            finally
            {
                semaphoreSlim.Release();
            }
        }

        private static JsonObject Filtered(JsonObject swagger, HashSet<string> postPathTemplates)
        {
            JsonObject result = [];
            foreach (KeyValuePair<string, JsonNode?> keyValuePair in swagger)
            {
                if (keyValuePair.Key != "paths")
                {
                    result[keyValuePair.Key] = keyValuePair.Value?.DeepClone();
                }
            }

            JsonObject jsonObject_Paths = [];
            if (swagger["paths"] is JsonObject jsonObject_Paths_Source)
            {
                foreach (KeyValuePair<string, JsonNode?> keyValuePair_Path in jsonObject_Paths_Source)
                {
                    if (!keyValuePair_Path.Key.StartsWith(Constants.Default.HarnessPathPrefix, StringComparison.OrdinalIgnoreCase) || keyValuePair_Path.Value is not JsonObject jsonObject_Operations)
                    {
                        continue;
                    }

                    JsonObject jsonObject_Operations_Result = [];
                    foreach (KeyValuePair<string, JsonNode?> keyValuePair_Operation in jsonObject_Operations)
                    {
                        bool allowed = keyValuePair_Operation.Key == "get" || (keyValuePair_Operation.Key == "post" && postPathTemplates.Contains(keyValuePair_Path.Key));
                        if (allowed)
                        {
                            jsonObject_Operations_Result[keyValuePair_Operation.Key] = keyValuePair_Operation.Value?.DeepClone();
                        }
                    }

                    if (jsonObject_Operations_Result.Count != 0)
                    {
                        jsonObject_Paths[keyValuePair_Path.Key] = jsonObject_Operations_Result;
                    }
                }
            }

            result["paths"] = jsonObject_Paths;
            return result;
        }

        private static async Task<HashSet<string>> PostPathTemplatesAsync(CancellationToken cancellationToken)
        {
            HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);

            string path = Path.Combine(AppContext.BaseDirectory, Constants.Default.HarnessGISFileName);
            if (!File.Exists(path))
            {
                Serilog.Modify.Log(Serilog.Enums.LogEventLevel.Warning, "GISHarnessHost: no {FileName} - the assistant can call GET endpoints only.", Constants.Default.HarnessGISFileName);
                return result;
            }

            try
            {
                if (JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))?["PostPathTemplates"] is JsonArray jsonArray)
                {
                    foreach (JsonNode? jsonNode in jsonArray)
                    {
                        if (jsonNode is JsonValue jsonValue && jsonValue.TryGetValue(out string? pathTemplate) && !string.IsNullOrWhiteSpace(pathTemplate))
                        {
                            result.Add(pathTemplate);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                Serilog.Modify.Log(Serilog.Enums.LogEventLevel.Warning, "GISHarnessHost: {FileName} is not valid JSON - the assistant can call GET endpoints only.", Constants.Default.HarnessGISFileName);
            }

            return result;
        }

        private static async Task<string> PromptAsync(CancellationToken cancellationToken)
        {
            string path = Path.Combine(AppContext.BaseDirectory, Constants.Default.HarnessGISPromptFileName);
            if (File.Exists(path))
            {
                return (await File.ReadAllTextAsync(path, cancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
            }

            Serilog.Modify.Log(Serilog.Enums.LogEventLevel.Warning, "GISHarnessHost: no {FileName} - using the built-in prompt.", Constants.Default.HarnessGISPromptFileName);
            return "You are the DiGi GIS assistant. Answer questions about Polish buildings and administrative areas by calling the GIS Web API with the tools. Never invent figures.";
        }
    }
}
