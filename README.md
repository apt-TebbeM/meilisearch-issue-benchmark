# meilisearch-issue-benchmark

Code to benchmark the performance degradation in meilisearch from 1.41.0 to 1.42.1
This codebase includes a compose file to run multiple versions of meilisearch, one for each version, and a simple single file dotnet app to run a crude benchmark  that illustrates the performance degradation in meilisearch from 1.41.0 to 1.42.1


## What the code does

The code create two classes that connect to the two meilisearch instances. It then proceeds to create N = 2 SearchIndexes in each instance and populates them with 100_000 documents each.

The documents stored are simple they have a "ArticleNumber" that is increased for every document and a "Title" that is a random string. The ArticleNumber is used to create the filterstring for the benchmark.

The benchmark itselfe creates a multisearch query that is send to both indexes and the filterstring is build to contain encreasing numbers of "articleNumber IN [ '1','2', .... 'N' ]" to illustrate the performance degradation as the filterstring grows.


## installation

install dotnet sdk 10.0 from https://dotnet.microsoft.com/en-us/download/dotnet/10.0
install docker to run the compose file

## run the benchmark

run the compose file to start the meilisearch instances

```bash
docker compose up -d
```

### donet run

run the dotnet app to create the documents and run the benchmark, this will run the benchmark against both meilisearch instances and print the results to the console

```bash
# use the createDocuments argument to create the documents in the meilisearch instances, this only needs to be done once
dotnet run --file .\App.cs -- createDocuments  

# runs the benchmark
dotnet run --file .\App.cs
```

### docker 

build the container and run it if you do not want to install dotnet sdk, this will run the benchmark without creating the documents, so make sure to run the dotnet command with the createDocuments argument at least once before running the container

```bash

docker build . -t meilisearch-benchmark

docker run --rm --net=host meilisearch-benchmark

```
