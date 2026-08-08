using System.Text.Json;
using Google.Cloud.Tasks.V2;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Ingestion;

public sealed class CloudTasksIngestionTaskDispatcher(
    CloudTasksClient client,
    IOptions<TasksOptions> options) : IIngestionTaskDispatcher
{
    private readonly TasksOptions _options = options.Value;

    public bool IsDurable => true;

    public async System.Threading.Tasks.Task DispatchAsync(ObjectId operationId, CancellationToken ct)
    {
        var queue = QueueName.FromProjectLocationQueue(
            _options.ProjectId, _options.Location, _options.Queue);
        var task = new Google.Cloud.Tasks.V2.Task
        {
            Name = TaskName.FromProjectLocationQueueTask(
                _options.ProjectId, _options.Location, _options.Queue, operationId.ToString()).ToString(),
            DispatchDeadline = Duration.FromTimeSpan(TimeSpan.FromMinutes(30)),
            HttpRequest = new HttpRequest
            {
                HttpMethod = Google.Cloud.Tasks.V2.HttpMethod.Post,
                Url = _options.HandlerUrl,
                Body = ByteString.CopyFromUtf8(JsonSerializer.Serialize(new { operationId = operationId.ToString() })),
                OidcToken = new OidcToken
                {
                    ServiceAccountEmail = _options.ServiceAccountEmail,
                    Audience = _options.Audience
                }
            }
        };
        task.HttpRequest.Headers.Add("Content-Type", "application/json");

        try
        {
            await client.CreateTaskAsync(queue, task, cancellationToken: ct);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.AlreadyExists)
        {
            // Stable operation ID makes enqueue idempotent.
        }
    }
}
