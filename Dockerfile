# Build with the .NET 10 SDK, run on the slim ASP.NET runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FdeTracker.Api/FdeTracker.Api.csproj FdeTracker.Api/
RUN dotnet restore FdeTracker.Api
COPY FdeTracker.Api FdeTracker.Api
COPY migrations migrations
# The practice lab builds the Day 1 and Day 2 case files into the API.
COPY evals/day1-prompts.json evals/day2-cases.json evals/
RUN dotnet publish FdeTracker.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production
USER $APP_UID
ENTRYPOINT ["dotnet", "FdeTracker.Api.dll"]
