FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Amsterfam.sln .
COPY Amsterfam.Api/Amsterfam.Api.csproj Amsterfam.Api/
COPY Amsterfam.Tests/Amsterfam.Tests.csproj Amsterfam.Tests/
RUN dotnet restore -p:HUSKY=0

COPY . .
RUN dotnet publish Amsterfam.Api/Amsterfam.Api.csproj -c Release -o /app/publish --no-restore -p:HUSKY=0

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Amsterfam.Api.dll"]
