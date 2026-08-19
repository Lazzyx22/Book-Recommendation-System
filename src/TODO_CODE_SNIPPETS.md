# BookRecommendationSystem — Build TODO & Code Snippets

Everything discussed so far, organized by **file path** so you can copy each block straight into the matching file in your scaffolded solution. Check items off as you go.

---

## ✅ 0. Scaffold (run once, from repo root)

```bash
dotnet new sln -n BookRecommendationSystem

dotnet new blazor -n BookRecommendationSystem.Web --interactivity Server --empty -o src/BookRecommendationSystem.Web
dotnet new classlib -n BookRecommendationSystem.Data -o src/BookRecommendationSystem.Data
dotnet new console -n BookRecommendationSystem.Seed -o src/BookRecommendationSystem.Seed

dotnet sln add src/BookRecommendationSystem.Web src/BookRecommendationSystem.Data src/BookRecommendationSystem.Seed

dotnet add src/BookRecommendationSystem.Web reference src/BookRecommendationSystem.Data
dotnet add src/BookRecommendationSystem.Seed reference src/BookRecommendationSystem.Data

dotnet new gitignore
```

**Packages:**
```bash
dotnet add src/BookRecommendationSystem.Data package Neo4j.Driver
dotnet add src/BookRecommendationSystem.Seed package Neo4j.Driver
dotnet add src/BookRecommendationSystem.Web package Microsoft.Extensions.Configuration.EnvironmentVariables
```

- [ ] Scaffold created
- [ ] Packages restored (`dotnet restore`)

---

## ✅ 1. `src/BookRecommendationSystem.Data/CognoDbSettings.cs`

```csharp
namespace BookRecommendationSystem.Data;

public class CognoDbSettings
{
    public string Uri { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
}
```

- [ ] File created

---

## ✅ 2. `src/BookRecommendationSystem.Data/Neo4jConnection.cs`

```csharp
using Neo4j.Driver;

namespace BookRecommendationSystem.Data;

public static class Neo4jConnection
{
    public static IDriver CreateDriver(CognoDbSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Uri) || string.IsNullOrWhiteSpace(settings.Password))
            throw new InvalidOperationException(
                "CognoDb connection settings are missing. Set CognoDb:Uri / CognoDb:User / CognoDb:Password " +
                "via user-secrets (dev) or environment variables (hosted).");

        return GraphDatabase.Driver(settings.Uri, AuthTokens.Basic(settings.User, settings.Password));
    }
}
```

- [ ] File created
- [ ] Shared by both `.Web` and `.Seed` — do not duplicate driver-creation logic elsewhere

---

## ✅ 3. `src/BookRecommendationSystem.Data/Models.cs`

```csharp
namespace BookRecommendationSystem.Data;

public record BookRecommendation(string Title, double AvgRating, int Votes);

public record RecommendationEvidence(string ReaderName, int Score, List<string> SharedBooks);

public record ExplainableRecommendation(
    string Title,
    double AvgRating,
    List<RecommendationEvidence> Evidence);

public class DatabaseUnavailableException : Exception
{
    public DatabaseUnavailableException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] File created

---

## ✅ 4. `src/BookRecommendationSystem.Data/RecommendationRepository.cs`

```csharp
using Neo4j.Driver;

namespace BookRecommendationSystem.Data;

public class RecommendationRepository
{
    private readonly IDriver _driver;

    private const string ExplainableQuery = """
        MATCH (me:Reader {id: $readerId})-[r1:RATED]->(shared:Book)<-[r2:RATED]-(similar:Reader)
        WHERE r1.score >= 4 AND r2.score >= 4 AND similar <> me
        WITH similar, collect(shared.title) AS sharedBooks, count(shared) AS overlap
        ORDER BY overlap DESC
        LIMIT 10
        MATCH (similar)-[r3:RATED]->(rec:Book)
        WHERE r3.score >= 4
          AND NOT EXISTS { MATCH (me)-[:RATED]->(rec) }
        WITH rec,
             collect({readerName: similar.name, score: r3.score, sharedBooks: sharedBooks}) AS evidence
        RETURN rec.title AS title,
               rec.avgRating AS avgRating,
               evidence
        ORDER BY size(evidence) DESC, avgRating DESC
        LIMIT 5
        """;

    private const string GenreFallbackQuery = """
        MATCH (me:Reader {id: $readerId})-[:RATED]->(:Book)-[:HAS_GENRE]->(g:Genre)
        WITH me, g, count(*) AS affinity
        ORDER BY affinity DESC LIMIT 3
        MATCH (g)<-[:HAS_GENRE]-(rec:Book)
        WHERE NOT EXISTS { MATCH (me)-[:RATED]->(rec) }
        RETURN DISTINCT rec.title AS title, rec.avgRating AS avgRating
        ORDER BY rec.avgRating DESC
        LIMIT 5
        """;

    private const string ReadingTwinQuery = """
        MATCH (me:Reader {id: $readerId})-[r1:RATED]->(b:Book)<-[r2:RATED]-(other:Reader)
        WHERE other <> me
        WITH other, count(b) AS sharedBooks,
             avg(abs(r1.score - r2.score)) AS avgScoreGap
        RETURN other.name AS twin, sharedBooks, avgScoreGap
        ORDER BY sharedBooks DESC, avgScoreGap ASC
        LIMIT 1
        """;

    public RecommendationRepository(IDriver driver) => _driver = driver;

    public async Task<List<ExplainableRecommendation>> GetExplainableRecommendationsAsync(string readerId)
    {
        await using var session = _driver.AsyncSession();
        try
        {
            var result = await session.ExecutableQuery(ExplainableQuery)
                .WithParameters(new { readerId })
                .ExecuteAsync();

            return result.Result.Select(record =>
            {
                var evidence = record["evidence"].As<List<object>>()
                    .Select(e =>
                    {
                        var map = (IDictionary<string, object>)e;
                        return new RecommendationEvidence(
                            ReaderName: map["readerName"].As<string>(),
                            Score: map["score"].As<int>(),
                            SharedBooks: map["sharedBooks"].As<List<object>>()
                                            .Select(b => b.As<string>()).ToList());
                    })
                    .ToList();

                return new ExplainableRecommendation(
                    Title: record["title"].As<string>(),
                    AvgRating: record["avgRating"].As<double>(),
                    Evidence: evidence);
            }).ToList();
        }
        catch (Neo4jException ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new DatabaseUnavailableException("CognoDB is unreachable right now.", ex);
        }
    }

    public async Task<List<BookRecommendation>> GetGenreFallbackRecommendationsAsync(string readerId)
    {
        await using var session = _driver.AsyncSession();
        try
        {
            var result = await session.ExecutableQuery(GenreFallbackQuery)
                .WithParameters(new { readerId })
                .ExecuteAsync();

            return result.Result.Select(r => new BookRecommendation(
                Title: r["title"].As<string>(),
                AvgRating: r["avgRating"].As<double>(),
                Votes: 0
            )).ToList();
        }
        catch (Neo4jException ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new DatabaseUnavailableException("CognoDB is unreachable right now.", ex);
        }
    }

    public async Task<(string TwinName, int SharedBooks, double AvgScoreGap)?> GetReadingTwinAsync(string readerId)
    {
        await using var session = _driver.AsyncSession();
        try
        {
            var result = await session.ExecutableQuery(ReadingTwinQuery)
                .WithParameters(new { readerId })
                .ExecuteAsync();

            var record = result.Result.FirstOrDefault();
            if (record is null) return null;

            return (record["twin"].As<string>(),
                    record["sharedBooks"].As<int>(),
                    record["avgScoreGap"].As<double>());
        }
        catch (Neo4jException ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new DatabaseUnavailableException("CognoDB is unreachable right now.", ex);
        }
    }
}
```

- [ ] File created
- [ ] `GetExplainableRecommendationsAsync` — primary recommendation query
- [ ] `GetGenreFallbackRecommendationsAsync` — cold-start fallback for sparse readers
- [ ] `GetReadingTwinAsync` — stretch feature, only wire up if time allows

---

## ✅ 5. `src/BookRecommendationSystem.Seed/SeedModels.cs`

```csharp
namespace BookRecommendationSystem.Seed;

public record SeedData(
    List<GenreSeed> Genres,
    List<AuthorSeed> Authors,
    List<BookSeed> Books,
    List<ReaderSeed> Readers,
    List<RatingSeed> Ratings);

public record GenreSeed(string Name);

public record AuthorSeed(string Name);

public record BookSeed(
    string Id,
    string Title,
    string Author,
    int PublishedYear,
    List<string> Genres,
    double AvgRating);

public record ReaderSeed(
    string Id,
    string Name,
    string JoinedAt);

public record RatingSeed(
    string Id,
    string ReaderId,
    string BookId,
    int Score,
    string RatedAt);
```

- [ ] File created

---

## ✅ 6. `src/BookRecommendationSystem.Seed/Program.cs`

```csharp
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Neo4j.Driver;
using BookRecommendationSystem.Data;
using BookRecommendationSystem.Seed;

// --- 1. Load config (user-secrets / env vars) ---
var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = config.GetSection("CognoDb").Get<CognoDbSettings>()
    ?? throw new InvalidOperationException("Missing CognoDb configuration section.");

// --- 2. Load seed data from JSON ---
var json = File.ReadAllText("seed_data.json");
var seed = JsonSerializer.Deserialize<SeedData>(json)
    ?? throw new InvalidOperationException("seed_data.json failed to deserialize.");

Console.WriteLine($"Loaded from file: {seed.Genres.Count} genres, {seed.Authors.Count} authors, " +
                   $"{seed.Books.Count} books, {seed.Readers.Count} readers, {seed.Ratings.Count} ratings");

// --- 3. Connect and seed, in dependency order ---
using var driver = Neo4jConnection.CreateDriver(settings);

try
{
    await driver.VerifyConnectivityAsync();
    Console.WriteLine("Connected to CognoDB.");

    await using var session = driver.AsyncSession();

    await SeedGenresAsync(session, seed.Genres);
    await SeedAuthorsAsync(session, seed.Authors);
    await SeedBooksAsync(session, seed.Books);
    await SeedReadersAsync(session, seed.Readers);
    await SeedRatingsAsync(session, seed.Ratings);

    Console.WriteLine("Seed complete.");
}
catch (Neo4jException ex)
{
    Console.Error.WriteLine($"Seeding failed: {ex.Message}");
    Environment.ExitCode = 1;
}

static async Task SeedGenresAsync(IAsyncSession session, List<GenreSeed> genres)
{
    await session.ExecuteWriteAsync(async tx =>
    {
        await tx.RunAsync("""
            UNWIND $genres AS g
            MERGE (:Genre {name: g.name})
            """,
            new { genres = genres.Select(g => new { g.Name }) });
    });
    Console.WriteLine($"  Genres: {genres.Count}");
}

static async Task SeedAuthorsAsync(IAsyncSession session, List<AuthorSeed> authors)
{
    await session.ExecuteWriteAsync(async tx =>
    {
        await tx.RunAsync("""
            UNWIND $authors AS a
            MERGE (:Author {name: a.name})
            """,
            new { authors = authors.Select(a => new { a.Name }) });
    });
    Console.WriteLine($"  Authors: {authors.Count}");
}

static async Task SeedBooksAsync(IAsyncSession session, List<BookSeed> books)
{
    await session.ExecuteWriteAsync(async tx =>
    {
        await tx.RunAsync("""
            UNWIND $books AS b
            MERGE (book:Book {id: b.id})
            SET book.title = b.title,
                book.publishedYear = b.publishedYear,
                book.avgRating = b.avgRating
            WITH book, b
            MATCH (author:Author {name: b.author})
            MERGE (book)-[:WRITTEN_BY]->(author)
            """,
            new
            {
                books = books.Select(b => new
                {
                    b.Id, b.Title, b.PublishedYear, b.AvgRating, b.Author
                })
            });
    });

    await session.ExecuteWriteAsync(async tx =>
    {
        await tx.RunAsync("""
            UNWIND $bookGenres AS bg
            MATCH (book:Book {id: bg.bookId})
            MATCH (genre:Genre {name: bg.genreName})
            MERGE (book)-[:HAS_GENRE]->(genre)
            """,
            new
            {
                bookGenres = books.SelectMany(b => b.Genres.Select(g => new { bookId = b.Id, genreName = g }))
            });
    });

    Console.WriteLine($"  Books: {books.Count} (+ WRITTEN_BY, HAS_GENRE relationships)");
}

static async Task SeedReadersAsync(IAsyncSession session, List<ReaderSeed> readers)
{
    await session.ExecuteWriteAsync(async tx =>
    {
        await tx.RunAsync("""
            UNWIND $readers AS r
            MERGE (reader:Reader {id: r.id})
            SET reader.name = r.name,
                reader.joinedAt = r.joinedAt
            """,
            new { readers = readers.Select(r => new { r.Id, r.Name, r.JoinedAt }) });
    });
    Console.WriteLine($"  Readers: {readers.Count}");
}

static async Task SeedRatingsAsync(IAsyncSession session, List<RatingSeed> ratings)
{
    await session.ExecuteWriteAsync(async tx =>
    {
        await tx.RunAsync("""
            UNWIND $ratings AS rt
            MATCH (reader:Reader {id: rt.readerId})
            MATCH (book:Book {id: rt.bookId})
            MERGE (reader)-[r:RATED]->(book)
            SET r.score = rt.score,
                r.ratedAt = rt.ratedAt
            """,
            new
            {
                ratings = ratings.Select(rt => new
                {
                    rt.ReaderId, rt.BookId, rt.Score, rt.RatedAt
                })
            });
    });
    Console.WriteLine($"  Ratings: {ratings.Count}");
}
```

- [ ] File created
- [ ] Order preserved: Genres → Authors → Books → Readers → Ratings

---

## ✅ 7. `src/BookRecommendationSystem.Seed/BookRecommendationSystem.Seed.csproj`

Add this `<ItemGroup>` so `seed_data.json` ships next to the built exe:

```xml
<ItemGroup>
  <None Include="seed_data.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] `seed_data.json` copied into `src/BookRecommendationSystem.Seed/`
- [ ] `CopyToOutputDirectory` entry added to the `.csproj`
- [ ] `dotnet build` then confirm: `ls bin/Debug/net10.0/seed_data.json`

---

## ✅ 8. `src/BookRecommendationSystem.Web/Program.cs`

```csharp
using Microsoft.Extensions.Options;
using Neo4j.Driver;
using BookRecommendationSystem.Data;
using BookRecommendationSystem.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CognoDbSettings>(builder.Configuration.GetSection("CognoDb"));

builder.Services.AddSingleton<IDriver>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<CognoDbSettings>>().Value;
    return Neo4jConnection.CreateDriver(settings);
});

builder.Services.AddScoped<RecommendationRepository>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

- [ ] File updated (merge with whatever `dotnet new blazor` scaffolded — keep its `App` component wiring, add the CognoDB parts)

---

## ✅ 9. `src/BookRecommendationSystem.Web/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "CognoDb": {
    "Uri": "",
    "User": "",
    "Password": ""
  }
}
```

- [ ] `CognoDb` section added — **values stay empty here**, real values go in user-secrets/env vars only

---

## ✅ 10. `src/BookRecommendationSystem.Web/Components/Pages/Recommendations.razor`

```razor
@page "/recommendations/{ReaderId}"
@inject RecommendationRepository Repo
@using BookRecommendationSystem.Data

@if (_error is not null)
{
    <div class="db-unavailable-banner">
        <p>@_error</p>
        <button @onclick="LoadAsync">Retry</button>
    </div>
}
else if (_recs is null)
{
    <p>Loading recommendations…</p>
}
else if (_recs.Count == 0)
{
    <p>Not enough ratings yet to build recommendations for this reader.</p>
}
else
{
    <ul class="rec-list">
        @foreach (var rec in _recs)
        {
            <li class="rec-card">
                <div class="rec-header" @onclick="() => Toggle(rec.Title)">
                    <strong>@rec.Title</strong>
                    <span>@rec.AvgRating.ToString("0.0")★</span>
                    <span class="badge">@rec.Evidence.Count reader@(rec.Evidence.Count == 1 ? "" : "s") agree</span>
                </div>

                @if (_expanded.Contains(rec.Title))
                {
                    <ul class="evidence-list">
                        @foreach (var ev in rec.Evidence)
                        {
                            <li>
                                <strong>@ev.ReaderName</strong> rated it @ev.Score★ —
                                you both loved <em>@string.Join(", ", ev.SharedBooks.Take(2))</em>
                            </li>
                        }
                    </ul>
                }
            </li>
        }
    </ul>
}

@code {
    [Parameter] public string ReaderId { get; set; } = "";
    private List<ExplainableRecommendation>? _recs;
    private readonly HashSet<string> _expanded = new();
    private string? _error;

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _error = null; _recs = null;
        StateHasChanged();
        try { _recs = await Repo.GetExplainableRecommendationsAsync(ReaderId); }
        catch (DatabaseUnavailableException ex) { _error = ex.Message; }
    }

    private void Toggle(string title)
    {
        if (!_expanded.Remove(title)) _expanded.Add(title);
    }
}
```

- [ ] File created (this is the loading/empty/error/data pattern to reuse on every other page)

---

## ✅ 11. Secrets setup (run once per project that needs CognoDB)

```bash
# for the Web project
cd src/BookRecommendationSystem.Web
dotnet user-secrets init
dotnet user-secrets set "CognoDb:Uri" "bolt+s://your-instance-id.databases.cognodb.cloud"
dotnet user-secrets set "CognoDb:User" "cognodb"
dotnet user-secrets set "CognoDb:Password" "your-generated-password"

# for the Seed project — separate UserSecretsId, so set again here
cd ../BookRecommendationSystem.Seed
dotnet user-secrets init
dotnet user-secrets set "CognoDb:Uri" "bolt+s://your-instance-id.databases.cognodb.cloud"
dotnet user-secrets set "CognoDb:User" "cognodb"
dotnet user-secrets set "CognoDb:Password" "your-generated-password"
```

- [ ] Secrets set for `.Web`
- [ ] Secrets set for `.Seed` (separate `UserSecretsId`, so this is a second, independent `set` — not shared automatically)
- [ ] `dotnet user-secrets list` confirms both

---

## ✅ 12. Repo hygiene files

`.env.example` (repo root):
```
COGNODB_URI=bolt+s://your-instance-id.databases.cognodb.cloud
COGNODB_USER=cognodb
COGNODB_PASSWORD=your-generated-password
```

- [ ] `.env.example` added and committed (no real values)
- [ ] `.gitignore` present (`dotnet new gitignore`) — confirm `bin/`, `obj/` are excluded
- [ ] `README.md` in place at repo root
- [ ] `seed_data.json` in place under `src/BookRecommendationSystem.Seed/`

---

## ✅ 13. Verification checklist (before moving to UI polish)

```bash
# 1. Build everything
dotnet build

# 2. Run the seed
cd src/BookRecommendationSystem.Seed && dotnet run
# expect: Genres: 15 / Authors: 25 / Books: 120 / Readers: 40 / Ratings: 652 / Seed complete.

# 3. Run the web app
cd ../BookRecommendationSystem.Web && dotnet run
# navigate to /recommendations/r001 and confirm it renders (not the error banner)
```

- [ ] Seed runs clean, counts match
- [ ] `/recommendations/{readerId}` renders real data for at least one reader
- [ ] `/recommendations/{readerId}` renders the **empty state** for one of the 5 sparse readers (r015, r016, r019, r040, r005) — or wire the genre-fallback query into that path so it shows something instead

---

## Still to write (not yet covered — ask when ready)

- [ ] `ReaderDashboard.razor` (reader picker + their rated books list)
- [ ] `ReadingTwins.razor` (optional stretch, uses `GetReadingTwinAsync`)
- [ ] `DbUnavailableBanner.razor` / `LoadingSkeleton.razor` as reusable components (currently inlined in `Recommendations.razor`)
- [ ] CSS pass for layout/typography
- [ ] CognoDB Browser spot-check queries (count nodes/relationships after seeding)
- [ ] Deployment steps for hosting the demo
