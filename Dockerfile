FROM mcr.microsoft.com/dotnet/sdk:9.0

RUN mkdir /intrinio

COPY . /intrinio

WORKDIR /intrinio/SampleApp

RUN dotnet build SampleApp.csproj

CMD ["dotnet", "run", "--framework", "net9.0", "--project", "SampleApp.csproj"]

