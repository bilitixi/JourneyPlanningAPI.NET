FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/JourneyPlanningApi/JourneyPlanningApi.csproj src/JourneyPlanningApi/
RUN dotnet restore src/JourneyPlanningApi/JourneyPlanningApi.csproj
COPY src/ src/
RUN dotnet publish src/JourneyPlanningApi/JourneyPlanningApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "JourneyPlanningApi.dll"]
