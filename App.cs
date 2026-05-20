#:package Meilisearch@0.18.0
#:package Bogus@35.6.5
#:property PublishAot=false

using Bogus;
using Meilisearch;
using System.Diagnostics;


var meiliSearchUrlOne = "http://localhost:7701";
var meiliSearchUrlTwo = "http://localhost:7702";
var meiliSearchUrlThree = "http://localhost:7703";
var meiliSearchUrlLatest = "http://localhost:7704";
var meilisearchMasterKey = "masterKey";
var documentCount = 100_000;
var createDocument = args.Length != 0 && args[0] == "createDocuments";
var indexCount = 2;

if (args.Length == 0)
{
    Console.WriteLine(
        "No arguments provided, defaulting to running benchmarks without creating documents. To create documents, provide any argument.");
}
else
{
    Console.WriteLine($"Args {args.Select(x => x).Aggregate((a, b) => $"{a} {b}")}");
}


var firstIndex = new TestIndex(meiliSearchUrlOne, meilisearchMasterKey, "version_1_41_0", createDocument, indexCount);
var slowIndex = new TestIndex(meiliSearchUrlTwo, meilisearchMasterKey, "version_1_42_1", createDocument, indexCount);
var threeIndex = new TestIndex(meiliSearchUrlThree, meilisearchMasterKey, "version_1_43_0", createDocument, indexCount);
var latestIndex = new TestIndex(meiliSearchUrlLatest, meilisearchMasterKey, "version_1_44_0", createDocument, indexCount);


var testIndexes = new List<TestIndex>()
{
    firstIndex, slowIndex, threeIndex, latestIndex
};

foreach (var indexToSetup in testIndexes)
{
    await indexToSetup.CreateIndexAsync();
    await indexToSetup.FillIndex(documentCount);
}

if (createDocument)
{
    Console.WriteLine("Waiting for document indexing");

    var timeSpanToWaitFor = TimeSpan.FromMinutes(1);
    var cSource = new CancellationTokenSource(timeSpanToWaitFor);
    var cancellationToken = cSource.Token;
    //display waiting dots
    var dotAmount = 4;
    var drawnDots = 0;
    Console.CursorVisible = false;
    while (!cancellationToken.IsCancellationRequested)
    {
        if (drawnDots == dotAmount)
        {
            Console.CursorLeft = 0;
            Console.Write(new string(' ', dotAmount));
            Console.CursorLeft = 0;
            drawnDots = 0;
        }

        Console.Write(".");
        await Task.Delay(150);
        drawnDots++;
    }

    Console.CursorVisible = true;
}


foreach (var testIndex in testIndexes)
{
    Console.WriteLine($"Benchmarking {testIndex.Name}");

    var benchmarkCases = new List<(int Start, int Count, int Repetitions)>
    {
        (1, 100, 7),
        (100, 100, 7),
        (200, 100, 7),
        (1, 200, 7),
        (200, 200, 7),
        (400, 200, 7),
        (1, 1_000, 7),
        (1_000, 1_000, 7),
        (2_000, 1_000, 7),
        (1, 5_000, 7),
        (5_000, 5_000, 7),
        (10_000, 5_000, 7),
        (1, 10_000, 21)
    };

    foreach (var benchmarkCase in benchmarkCases)
    {
        for (var i = 0; i < benchmarkCase.Repetitions; i++)
        {
            await testIndex.GetArticlesFromAsync(benchmarkCase.Start, benchmarkCase.Count);
        }
    }
}

BenchmarkSummaryPrinter.Print(testIndexes);


public class TestArticle
{
    public required string ArticleNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> Allergens { get; set; } = [];
}

public class TestIndex
{
    private readonly MeilisearchClient _client;
    private readonly string _indexName;
    private readonly bool _createDocuments;
    private readonly int _indexCount;
    private readonly List<BenchmarkSample> _benchmarkSamples = [];

    public TestIndex(string meiliSearchUrl, string meilisearchMasterKey, string testIndexName, bool createDocument,
        int indexCount = 1)
    {
        _client = new MeilisearchClient(meiliSearchUrl, meilisearchMasterKey);
        _indexName = testIndexName;
        _createDocuments = createDocument;
        _indexCount = indexCount;
    }

    public string Name => _indexName;
    public IReadOnlyList<BenchmarkSample> BenchmarkSamples => _benchmarkSamples;

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

        var stopwatch = Stopwatch.StartNew();

        var iSearchResult = await _client.FederatedMultiSearchAsync<TestArticle>(federatedQuery);
        stopwatch.Stop();

        if (iSearchResult is SearchResult<TestArticle> result && result.Hits.Count > 0)
        {
            _benchmarkSamples.Add(new BenchmarkSample(start, count, result.ProcessingTimeMs,
                stopwatch.ElapsedMilliseconds,
                result.Hits.Count));

            Console.WriteLine(
                $"{_indexName} filterCount: {articleNumbers.Count} \"ProcessingTimeMs: {result.ProcessingTimeMs}ms\" \"ElapsedMs: {stopwatch.ElapsedMilliseconds}ms\", item count {result.Hits.Count}");
        }
        else
        {
            _benchmarkSamples.Add(new BenchmarkSample(start, count, 0, stopwatch.ElapsedMilliseconds, 0));
        }
    }
}

public record BenchmarkSample(int Start, int Count, long ProcessingTimeMs, long ElapsedMs, int HitCount);

public static class BenchmarkSummaryPrinter
{
    public static void Print(IEnumerable<TestIndex> testIndexes)
    {
        Console.WriteLine();
        Console.WriteLine("========== BENCHMARK SUMMARY ==========");

        foreach (var testIndex in testIndexes)
        {
            var samples = testIndex.BenchmarkSamples.ToList();
            if (samples.Count == 0)
            {
                Console.WriteLine($"{testIndex.Name}: no samples recorded.");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"Version: {testIndex.Name}");
            Console.WriteLine(
                $"Runs: {samples.Count}, AvgProcessingMs: {samples.Average(x => x.ProcessingTimeMs):F2}, MinProcessingMs: {samples.Min(x => x.ProcessingTimeMs)}, MaxProcessingMs: {samples.Max(x => x.ProcessingTimeMs)}");
            Console.WriteLine(
                $"AvgElapsedMs: {samples.Average(x => x.ElapsedMs):F2}, MinElapsedMs: {samples.Min(x => x.ElapsedMs)}, MaxElapsedMs: {samples.Max(x => x.ElapsedMs)}");

            Console.WriteLine("By filter size:");
            foreach (var group in samples.GroupBy(x => x.Count).OrderBy(x => x.Key))
            {
                Console.WriteLine(
                    $"  count={group.Key,6}: runs={group.Count(),2}, avgProc={group.Average(x => x.ProcessingTimeMs),8:F2}ms, avgElapsed={group.Average(x => x.ElapsedMs),8:F2}ms, maxProc={group.Max(x => x.ProcessingTimeMs),4}ms");
            }
        }

        Console.WriteLine("=======================================");
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