using DiGi.Core.IO.Table.Classes;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DiGi.GIS.WebAPI.UI
{
    public static partial class Query
    {
        /// <summary>
        /// Fetches the building data rows of the given buildings from the GIS Web API into one table.
        /// <para>A reference is unique only per county part (Coding - GIS Administrative Data), so the buildings are grouped by county part and each part is asked for its own references. The upstream <c>tablebybuildingdatabyreferencesparameter</c> refuses more than <see cref="Constants.Default.BuildingDataReferenceBatchSize"/> references per request, so a part is asked in batches of that size. The requests run one after another, never fanned out (Coding - Deployed WebAPI, section 4). A building with county part 0 (unknown) is asked without a part, and the upstream then resolves it.</para>
        /// <para>The body property names must equal those of the GIS Web API's <c>BuildingDataByReferencesParameter</c> (<c>CountyId</c>, <c>ColumnUniqueIds</c>, <c>References</c>): the client has no compile-time link to the API, and an unknown name is silently ignored (Coding - WebAPI Contracts). The upstream always projects <c>County Id</c>, so every row can be matched back to its building by (reference, county part).</para>
        /// <para>An upstream 404 means no row matched; the batch then contributes nothing. Any other failure is retried once, and a second failure fails the whole fetch, so the caller never chooses a representative from a silently partial node.</para>
        /// </summary>
        /// <param name="httpClient">The HTTP client used for the request. This value can be null.</param>
        /// <param name="buildings">The buildings to fetch, by reference and county part. This value can be null.</param>
        /// <param name="columnUniqueIds">The column slugs to project.</param>
        /// <param name="commandTimeout">The upstream command timeout in seconds. Defaults to 600.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by the caller to cancel the asynchronous operation.</param>
        /// <returns>The merged table (empty when no building matched), or <see langword="null"/> when the input is empty or a batch failed after its retry.</returns>
        public static async Task<Table?> BuildingDataTableByReferencesAsync(this HttpClient? httpClient, IEnumerable<(string Reference, int CountyId)>? buildings, List<string> columnUniqueIds, int commandTimeout = 600, CancellationToken cancellationToken = default)
        {
            if (httpClient is null || buildings is null || columnUniqueIds is null || columnUniqueIds.Count == 0)
            {
                return null;
            }

            Dictionary<int, List<string>> references_ByCountyId = [];
            foreach ((string reference, int countyId) in buildings)
            {
                if (string.IsNullOrWhiteSpace(reference))
                {
                    continue;
                }

                if (!references_ByCountyId.TryGetValue(countyId, out List<string>? references))
                {
                    references = [];
                    references_ByCountyId[countyId] = references;
                }

                references.Add(reference);
            }

            if (references_ByCountyId.Count == 0)
            {
                return null;
            }

            string requestUri = $"{Constants.Default.BuildingDataTableByReferencesUri}?commandtimeout={commandTimeout}";

            Table? result = null;
            foreach (KeyValuePair<int, List<string>> keyValuePair in references_ByCountyId)
            {
                List<string> references = [.. keyValuePair.Value.Distinct()];
                for (int index = 0; index < references.Count; index += Constants.Default.BuildingDataReferenceBatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    List<string> references_Batch = references.GetRange(index, System.Math.Min(Constants.Default.BuildingDataReferenceBatchSize, references.Count - index));

                    Table? table = await BatchAsync(keyValuePair.Key, references_Batch);
                    if (table is null)
                    {
                        return null;
                    }

                    if (table.RowCount != 0)
                    {
                        result = Modify.Append(result, table);
                    }
                }
            }

            return result ?? new Table();

            // One batch, parsed; an empty table when nothing matched; null after a failed retry.
            async Task<Table?> BatchAsync(int countyId, List<string> references_Batch)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        Dictionary<string, object?> body = new()
                        {
                            ["CountyId"] = countyId > 0 ? countyId : null,
                            ["ColumnUniqueIds"] = columnUniqueIds,
                            ["References"] = references_Batch
                        };

                        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
                        request.Content = JsonContent.Create(body, options: JsonSerializerOptions.Default);

                        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            return new Table();
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                            Table? table = await Create.TableAsync(stream, cancellationToken);
                            if (table is not null)
                            {
                                return table;
                            }
                        }
                    }
                    catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        // Transient failure (connection reset, timeout): fall through to the retry.
                    }

                    if (attempt == 0)
                    {
                        await Task.Delay(2000, cancellationToken);
                    }
                }

                return null;
            }
        }
    }
}
