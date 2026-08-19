using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Neo4j.Driver;
using BookRecommendationSystem.Data;
using BookRecommendationSystem.Seed;

// --- 1. Load Config (user-secrets or environment variables) ---
var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = config.GetSection("CognoDb").Get<CognoDbSettings>()
    ?? throw new InvalidOperationException("Missing CognoDb configuration Section.");

// --- 2. Load seed data from JSON ---
var json = File.ReadAllText("seed_data.json");

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

var seed = JsonSerializer.Deserialize<SeedData>(json, jsonOptions)
    ?? throw new InvalidOperationException("seed_data.json has failed to deserialize.");

Console.WriteLine($"Loaded from file: {seed.Genres.Count} genres, {seed.Authors.Count} authors, " +
    $"{seed.Books.Count} books, {seed.Readers.Count} readers, {seed.Ratings.Count} ratings");

// --- 3. Connect and seed, in the dependency order ---
using var driver = Neo4jConnection.CreateDriver(settings);

try
{
    await driver.VerifyConnectivityAsync();
    Console.WriteLine("Connected to CognoDb");

    await using var session = driver.AsyncSession();

    await SeedGenresAsync(session, seed.Genres);
    await SeedAuthorsAsync(session, seed.Authors);
    await SeedBooksAsync(session, seed.Books);
    await SeedReaderAsync(session, seed.Readers);
    await SeedRatingAsync(session, seed.Ratings);
    Console.WriteLine("Seed Complete");

}
catch (Neo4jException ex)
{
    Console.Error.WriteLine($"Seeding Failed: {ex.Message}");
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
            new {genres = genres.Select(g => new { g.Name }) });
    });
    Console.WriteLine($" Genres: {genres.Count}");
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
    Console.WriteLine($" Authors: {authors.Count}");
}

static async Task SeedBooksAsync(IAsyncSession session, List<BookSeed> books)
{
    await session.ExecuteWriteAsync(async tx =>
    {
        await tx.RunAsync("""
            UNWIND $books AS b
            MERGE (book:Book {id: b.id})
            SET book.title = b.title, book.publishedYear = b.publishedYear, book.avgRating = b.avgRating
            WITH book, b
            MATCH (author:Author {name: b.author})
            MERGE (book)-[:WRITTEN_BY]->(author)
            """,
            new
            {
                books = books.Select(b => new
                {
                    id = b.Id,
                    title = b.Title,
                    publishedYear = b.PublishedYear,
                    avgRating = b.AvgRating,
                    author = b.Author
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
                bookGenres = books.SelectMany(b =>
                    b.Genres.Select(g => new { bookId = b.Id, genreName = g }))
            });
    });

    Console.WriteLine($" Books: {books.Count} (+ WRITTEN_BY, HAS_GENRE relationships)");
}

static async Task SeedReaderAsync(IAsyncSession session, List<ReaderSeed> readers)
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
    Console.WriteLine($" Readers: {readers.Count}");
}

static async Task SeedRatingAsync(IAsyncSession session, List<RatingSeed> ratings)
{
    await session.ExecuteWriteAsync(async tx =>
    {
        await tx.RunAsync("""
            UNWIND $ratings AS rt
            MATCH (reader:Reader {id: rt.readerId})
            MATCH (book:Book {id: rt.bookId})
            MERGE (reader)- [r:RATED]->(book)
            SET r.score = rt.score,
                r.ratedAt = rt.ratedAt
            """,
            new
            {
                ratings = ratings.Select(rt => new
                {
                    rt.ReaderId,
                    rt.BookId,
                    rt.Score,
                    rt.RatedAt
                })
            });
    });
    Console.WriteLine($" Ratings: {ratings.Count}");
}