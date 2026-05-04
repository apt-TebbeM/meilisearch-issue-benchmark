namespace Meilisearch.Benchmark;

public static class MeilisearchClientExtensions
{
    public static async Task<TaskResource> EnsureTaskIsDoneAsync(this MeilisearchClient client, TaskInfo result,
        CancellationToken cancellationToken = default)
    {
        bool isTaskDone = false;
        TaskResource? taskResource;
        do
        {
            taskResource = await client.GetTaskAsync(result.TaskUid, cancellationToken);
            isTaskDone = taskResource.Status == TaskInfoStatus.Failed ||
                         taskResource.Status == TaskInfoStatus.Succeeded;
            await Task.Delay(50, cancellationToken);
        } while (!isTaskDone || cancellationToken.IsCancellationRequested);

        return taskResource;
    }
}