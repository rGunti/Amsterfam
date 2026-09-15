FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The build context here doesn't include .git (it's a subdirectory of the
# repo), so the csproj's own `git rev-parse` fallback can't work — the SHA
# is passed in from outside instead.
ARG SOURCE_REVISION_ID=unknown

COPY Amsterfam.slnx .
COPY Amsterfam.Api/Amsterfam.Api.csproj Amsterfam.Api/
COPY Amsterfam.Core/Amsterfam.Core.csproj Amsterfam.Core/
COPY Amsterfam.Infrastructure/Amsterfam.Infrastructure.csproj Amsterfam.Infrastructure/
COPY Amsterfam.Tests/Amsterfam.Tests.csproj Amsterfam.Tests/
RUN dotnet restore -p:HUSKY=0

COPY . .
RUN dotnet publish Amsterfam.Api/Amsterfam.Api.csproj -c Release -o /app/publish --no-restore -p:HUSKY=0 -p:SourceRevisionId=$SOURCE_REVISION_ID

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Amsterfam.Api.dll"]
