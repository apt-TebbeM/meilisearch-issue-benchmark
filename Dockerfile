FROM mcr.microsoft.com/dotnet/sdk:10.0 AS base

WORKDIR /app
COPY App.cs .

ENTRYPOINT ["dotnet", "run", "--file", "App.cs" ,"--"]