FROM mcr.microsoft.com/dotnet/sdk:6.0

RUN mkdir /intrinio

COPY . /intrinio

WORKDIR /intrinio/IntrinioRealTimeSDK

RUN dotnet build IntrinioRealTimeSDK.csproj
 
CMD dotnet run IntrinioRealTimeSDK.csproj

