using Neo4j.Driver;

namespace BookRecommendationSystem.Data;

public record ReaderSummary(string Id, string Name, int RatingCount);
public record RatedBook(string Title, int Score, double AvgRating, List<string> Genres);

public class ReaderRepository
{
    private readonly IDriver _driver;

    private const string AllReadersQuery = """
        MATCH (r:Reader)
        OPTIONAL MATCH (r)-[rt:RATED]->(:Book)
        RETURN r.id AS id, r.name AS name, count(rt) AS ratingCount
        ORDER BY r.name
        """;

    private const string ReaderBooksQuery = """
        MATCH (r:Reader {id: $readerId})-[rt:RATED]->(b:Book)
        OPTIONAL MATCH (b)-[:HAS_GENRE]->(g:Genre)
        WITH b, rt, collect(g.name) AS genres
        RETURN b.title AS title, rt.score AS score, b.avgRating AS avgRating, genres
        ORDER BY rt.score DESC
        """;

    public ReaderRepository(IDriver driver) => _driver = driver;

    public async Task<List<ReaderSummary>> GetAllReadersAsync()
    {
        try
        {
            var result = await _driver.ExecutableQuery(AllReadersQuery).ExecuteAsync();
            return result.Result.Select(r => new ReaderSummary(
                Id: r["id"].As<string>(),
                Name: r["name"].As<string>(),
                RatingCount: r["ratingCount"].As<int>()
            )).ToList();
        }
        catch (Neo4jException ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new DatabaseUnavailableException("CognoDB is unreachable right now.", ex);
        }
    }

    public async Task<List<RatedBook>> GetReaderBooksAsync(string readerId)
    {
        try
        {
            var result = await _driver.ExecutableQuery(ReaderBooksQuery)
                .WithParameters(new { readerId })
                .ExecuteAsync();

            return result.Result.Select(r => new RatedBook(
                Title: r["title"].As<string>(),
                Score: r["score"].As<int>(),
                AvgRating: r["avgRating"].As<double>(),
                Genres: r["genres"].As<List<object>>().Select(g => g.As<string>()).ToList()
            )).ToList();
        }
        catch (Neo4jException ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new DatabaseUnavailableException("CognoDB is unreachable right now.", ex);
        }
    }
}
