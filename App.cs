
#:package Meilisearch@0.18.0
#:package Bogus@35.6.5
#:property PublishAot=false

using Bogus;
using Meilisearch;

var meiliSearchUrlOne = "http://localhost:7701";
var meiliSearchUrlSusSlow = "http://localhost:7702";
var meilisearchMasterKey = "masterKey";
var documentCount = 100_000;
var createDocument = args.Length != 0 && args[0] == "createDocuments";
var indexCount = 2;

if (args.Length == 0)
{
    Console.WriteLine("No arguments provided, defaulting to running benchmarks without creating documents. To create documents, provide any argument.");
}
else
{
    Console.WriteLine($"Args {args.Select(x => x).Aggregate((a, b) => $"{a} {b}")}");
}

var firstIndex = new TestIndex(meiliSearchUrlOne, meilisearchMasterKey, "version_1_41_0", createDocument, indexCount);
var slowIndex = new TestIndex(meiliSearchUrlSusSlow, meilisearchMasterKey, "version_1_42_1", createDocument, indexCount);

await firstIndex.CreateIndexAsync();
await firstIndex.FillIndex(documentCount);

await slowIndex.CreateIndexAsync();
await slowIndex.FillIndex(documentCount);

var testIndexes = new List<TestIndex>()
{
    firstIndex, slowIndex
};

foreach (var testIndex in testIndexes)
{
    Console.WriteLine($"Benchmarking {testIndex.Name}");
    await testIndex.GetArticlesFromAsync(1, 100);
    await testIndex.GetArticlesFromAsync(1, 100);
    await testIndex.GetArticlesFromAsync(100, 100);
    await testIndex.GetArticlesFromAsync(100, 100);
    await testIndex.GetArticlesFromAsync(200, 100);
    await testIndex.GetArticlesFromAsync(200, 100);

    await testIndex.GetArticlesFromAsync(1, 200);
    await testIndex.GetArticlesFromAsync(1, 200);
    await testIndex.GetArticlesFromAsync(200, 200);
    await testIndex.GetArticlesFromAsync(200, 200);
    await testIndex.GetArticlesFromAsync(400, 200);
    await testIndex.GetArticlesFromAsync(400, 200);

    await testIndex.GetArticlesFromAsync(1, 1_000);
    await testIndex.GetArticlesFromAsync(1, 1_000);
    await testIndex.GetArticlesFromAsync(1_000, 1_000);
    await testIndex.GetArticlesFromAsync(1_000, 1_000);
    await testIndex.GetArticlesFromAsync(2_000, 1_000);
    await testIndex.GetArticlesFromAsync(2_000, 1_000);

    await testIndex.GetArticlesFromAsync(1, 5_000);
    await testIndex.GetArticlesFromAsync(1, 5_000);
    await testIndex.GetArticlesFromAsync(5_000, 5_000);
    await testIndex.GetArticlesFromAsync(5_000, 5_000);
    await testIndex.GetArticlesFromAsync(10_000, 5_000);
    await testIndex.GetArticlesFromAsync(10_000, 5_000);

    await testIndex.GetArticlesFromAsync(1, 10_000);
    await testIndex.GetArticlesFromAsync(1, 10_000);
    await testIndex.GetArticlesFromAsync(1, 10_000);
    await testIndex.GetArticlesFromAsync(1, 10_000);
    await testIndex.GetArticlesFromAsync(1, 10_000);
    await testIndex.GetArticlesFromAsync(1, 10_000);
    await testIndex.GetArticlesFromAsync(1, 10_000);
}


//Searching for all articles from 100-9999


public class TestArticle
{
    public required string ArticleNumber { get; set; }
    public string Description { get; set; }
    public string Title { get; set; }
    public List<string> Allergens { get; set; }
}

public class TestIndex
{
    private readonly MeilisearchClient _client;
    private readonly string _indexName;
    private readonly bool _createDocuments;
    private readonly int _indexCount;

    public TestIndex(string meiliSearchUrl, string meilisearchMasterKey, string testIndexName, bool createDocument,
        int indexCount = 1)
    {
        _client = new MeilisearchClient(meiliSearchUrl, meilisearchMasterKey);
        _indexName = testIndexName;
        _createDocuments = createDocument;
        _indexCount = indexCount;
    }

    public string Name => _indexName;

    public async Task CreateIndexAsync()
    {
        for (var i = 0; i < _indexCount; i++)
        {
            var indexName = $"{_indexName}_{i}";

            Console.WriteLine($"Creating {indexName}");

            var taskInfo = await _client.CreateIndexAsync(indexName, "articleNumber");
            await _client.EnsureTaskIsDoneAsync(taskInfo);
            var index = await _client.GetIndexAsync(indexName);

            await index.UpdateSearchableAttributesAsync(["title", "name"]);
            await index.UpdateFilterableAttributesAsync(["articleNumber", "allergens"]);
        }
    }

    public async Task FillIndex(int documentCount)
    {
        if (!_createDocuments)
        {
            Console.WriteLine($"Skipping document creation for {_indexName} as createDocuments is set to false");
            return;
        }

        for (int indexCount = 0; indexCount < _indexCount; indexCount++)
        {
            var indexName = $"{_indexName}_{indexCount}";

            Console.WriteLine($"Filling {documentCount} documents into {indexName}");

            var index = await _client.GetIndexAsync(indexName);
            var startingArticleNumber = 1;

            Randomizer.Seed = new Random(8675309);
            var article = new Faker<TestArticle>()
                    .RuleFor(a => a.Title, f => f.Random.Word())
                    .RuleFor(a => a.ArticleNumber, f => (startingArticleNumber++).ToString())
                    .RuleFor(a => a.Description, f => f.Lorem.Paragraph(4))
                    .RuleFor(a => a.Allergens,
                        f => f.Random.ListItems(["Gluten", "Lactose", "Nuts", "Soy"], 2))
                ;

            var generatedArticle = new List<TestArticle>();

            for (int i = 0; i < documentCount; i++)
            {
                generatedArticle.Add(article.Generate());
            }

            var addTask = await index.AddDocumentsAsync(generatedArticle);
            await _client.EnsureTaskIsDoneAsync(addTask);
        }
    }

    public async Task GetArticlesFromAsync(int start, int count)
    {
        var articleNumbers = Enumerable.Range(start, count).Select(x => x.ToString()).ToList();
        var filterString = $"articleNumber IN ['{string.Join("', '", articleNumbers)}']";

        var federatedQuery = new FederatedMultiSearchQuery()
        {
            Queries = []
        };

        for (var indexCount = 0; indexCount < _indexCount; indexCount++)
        {
            var indexName = $"{_indexName}_{indexCount}";
            federatedQuery.Queries.Add(new FederatedSearchQuery()
            {
                IndexUid = indexName,
                Filter = filterString,
                Q = ""
            });
        }

        var iSearchResult = await _client.FederatedMultiSearchAsync<TestArticle>(federatedQuery);

        if (iSearchResult is SearchResult<TestArticle> result && result.Hits.Count > 0)
        {
            Console.WriteLine(
                $"{_indexName} filterCount: {articleNumbers.Count} \"ProcessingTimeMs: {result.ProcessingTimeMs}ms\"");
        }
    }
}


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