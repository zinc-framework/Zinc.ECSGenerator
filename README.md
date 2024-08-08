# Zinc.ECSGenerator

iterate on the generator by running
```
dotnet build-server shutdown && dotnet clean && dotnet restore && dotnet build && dotnet run --project .\Zinc.ECSGenerator.Sample\Zinc.ECSGenerator.Sample.csproj
```

this "cleans" generator state from parent dir and reboots build server