** The following are instructions to Intrinio developers for building and publishing this code to NuGet.

# Building

Open the Package Manager Console window in Visual Studio, then run:

```
nuget pack IntrinioRealTimeClient.nuspec -IncludeReferencedProjects
```

This will create a `IntrinioRealTimeClient.{version}.nupkg` file. To publish the file to NuGet, run:

# Publishing

For first-time setup, specify your NuGet API key:

```
nuget setApiKey <your_API_key>
```

Then publish the generated NuGet package:

```
nuget push IntrinioRealTimeClient.{version}.nupkg -Source https://api.nuget.org/v3/index.json
```
