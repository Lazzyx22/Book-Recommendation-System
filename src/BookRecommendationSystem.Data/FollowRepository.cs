using Neo4j.Driver;

namespace BookRecommendationSystem.Data;

public class FollowRepository
{
    private readonly IDriver _driver;

    private const string GetFollowingQuery = """
        MATCH (r:Reader {id: $readerId})-[:FOLLOWS]->(f:Reader)
        RETURN f.id AS id, f.name AS name
        ORDER BY f.name
        """;

    private const string FollowQuery = """
        MATCH (a:Reader {id: $readerId}), (b:Reader {id: $targetId})
        MERGE (a)-[:FOLLOWS]->(b)
        """;

    public FollowRepository(IDriver driver) => _driver = driver;

    public async Task<List<FollowedReader>> GetFollowingAsync(string readerId)
    {
        try
        {
            var result = await _driver.ExecutableQuery(GetFollowingQuery)
                .WithParameters(new { readerId })
                .ExecuteAsync();

            return result.Result.Select(r => new FollowedReader(
                Id: r["id"].As<string>(),
                Name: r["name"].As<string>()
            )).ToList();
        }
        catch (Neo4jException ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new DatabaseUnavailableException("CognoDB is unreachable right now.", ex);
        }
    }

    public async Task FollowAsync(string readerId, string targetId)
    {
        try
        {
            await _driver.ExecutableQuery(FollowQuery)
                .WithParameters(new { readerId, targetId })
                .ExecuteAsync();
        }
        catch (Neo4jException ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new DatabaseUnavailableException("CognoDB is unreachable right now.", ex);
        }
    }
}
