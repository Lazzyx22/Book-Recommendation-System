# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first so restore is cached across builds
COPY BookRecommendationSystem.sln ./
COPY src/BookRecommendationSystem.Data/*.csproj src/BookRecommendationSystem.Data/
COPY src/BookRecommendationSystem.Web/*.csproj src/BookRecommendationSystem.Web/
COPY src/BookRecommendationSystem.Seed/*.csproj src/BookRecommendationSystem.Seed/

RUN dotnet restore src/BookRecommendationSystem.Web/BookRecommendationSystem.Web.csproj

# Copy everything else and publish
COPY . .
RUN dotnet publish src/BookRecommendationSystem.Web/BookRecommendationSystem.Web.csproj -c Release -o /app/publish

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Render assigns a random port via the $PORT env var at runtime — bind to it
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

CMD ASPNETCORE_URLS=http://+:$PORT dotnet BookRecommendationSystem.Web.dll
