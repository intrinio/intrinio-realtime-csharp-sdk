FROM mcr.microsoft.com/dotnet/sdk:6.0

RUN mkdir /intrinio

COPY . /intrinio

WORKDIR /intrinio/SampleApp

RUN dotnet build SampleApp.csproj
 
CMD dotnet run SampleApp.csproj

