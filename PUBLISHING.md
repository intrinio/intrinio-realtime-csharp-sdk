** The following are instructions to Intrinio developers for building and publishing this code to NuGet.

# Building

Open a powershell window. Navigate to the ...\intrinio-realtime-csharp-sdk\IntrinioRealTimeSDK folder. Run:

```
dotnet pack IntrinioRealTimeSDK.csproj -p:NuspecFile=IntrinioRealTimeClient.nuspec
```

This will create a `IntrinioRealTimeClient.{version}.nupkg` file. The path to this file will be output by the 'pack' command but is likely in:
'...\intrinio-realtime-csharp-sdk\IntrinioRealTimeSDK\bin\Debug\'
To publish the file to NuGet, run:

# Publishing

## Nuget Website
Log in to www.nuget.org. Navigate to https://www.nuget.org/packages/manage/upload. Browse to the package. Upload.
*Note:* your nuget account must be authorized to perform this operation.

## CLI
For first-time setup, generate (or obtain) your NuGet API key (https://docs.microsoft.com/en-us/nuget/nuget-org/publish-a-package):

Then publish the generated NuGet package:

```
dotnet nuget push IntrinioRealTimeClient.{version}.nupkg --api-key {api-key} --source https://api.nuget.org/v3/index.json
```
