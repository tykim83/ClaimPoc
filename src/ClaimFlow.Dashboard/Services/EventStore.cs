using Microsoft.Azure.Cosmos;

namespace ClaimFlow.Dashboard.Services;

// Read model for the append-only docs Tasks writes (WriteEventActivity.EventRecord).
// Property names are lowercase to match the stored JSON.
public record EventDoc(
    string id,
    string correlationId,
    string eventType,
    string? status,
    string? stage,
    string? orchestratorId,
    string? traceId,
    DateTime timestamp);

// Read-only view over the Cosmos event log. The dashboard never writes.
public class EventStore(CosmosClient cosmosClient)
{
    private const string DatabaseName = "claimflow";
    private const string ContainerName = "events";

    private Container Container => cosmosClient.GetContainer(DatabaseName, ContainerName);

    // The latest events, with any combination of correlationId / status / eventType
    // filters. Empty/null filters are ignored. When correlationId is set it becomes
    // a single-partition point query; otherwise it's cross-partition (newest first).
    public Task<List<EventDoc>> QueryAsync(string? correlationId, string? status, string? eventType, int limit, CancellationToken ct = default)
    {
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(correlationId)) conditions.Add("c.correlationId = @cid");
        if (!string.IsNullOrWhiteSpace(status)) conditions.Add("c.status = @status");
        if (!string.IsNullOrWhiteSpace(eventType)) conditions.Add("c.eventType = @type");
        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        var query = new QueryDefinition($"SELECT TOP @limit * FROM c {where} ORDER BY c.timestamp DESC")
            .WithParameter("@limit", limit);
        if (!string.IsNullOrWhiteSpace(correlationId)) query.WithParameter("@cid", correlationId);
        if (!string.IsNullOrWhiteSpace(status)) query.WithParameter("@status", status);
        if (!string.IsNullOrWhiteSpace(eventType)) query.WithParameter("@type", eventType);

        PartitionKey? pk = string.IsNullOrWhiteSpace(correlationId) ? null : new PartitionKey(correlationId);
        return ReadAsync(query, pk, ct);
    }

    // Dev-only: wipe every event doc. The container itself (and its partition-key
    // config) is owned by AppHost, so we delete items rather than drop the container.
    public async Task<int> ClearAllAsync(CancellationToken ct = default)
    {
        var toDelete = new List<IdAndPk>();
        using (var iterator = Container.GetItemQueryIterator<IdAndPk>(
            new QueryDefinition("SELECT c.id, c.correlationId FROM c")))
        {
            while (iterator.HasMoreResults)
            {
                toDelete.AddRange(await iterator.ReadNextAsync(ct));
            }
        }

        var deleted = 0;
        await Parallel.ForEachAsync(
            toDelete,
            new ParallelOptions { MaxDegreeOfParallelism = 25, CancellationToken = ct },
            async (doc, token) =>
            {
                await Container.DeleteItemAsync<EventDoc>(doc.id, new PartitionKey(doc.correlationId), cancellationToken: token);
                Interlocked.Increment(ref deleted);
            });

        return deleted;
    }

    private record IdAndPk(string id, string correlationId);

    private async Task<List<EventDoc>> ReadAsync(QueryDefinition query, PartitionKey? partitionKey, CancellationToken ct)
    {
        var options = partitionKey is { } pk ? new QueryRequestOptions { PartitionKey = pk } : null;
        var results = new List<EventDoc>();
        using var iterator = Container.GetItemQueryIterator<EventDoc>(query, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            foreach (var doc in await iterator.ReadNextAsync(ct))
            {
                results.Add(doc);
            }
        }
        return results;
    }
}
