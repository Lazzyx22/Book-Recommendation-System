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
            AND NOT EXISTS { MATCH(me)-[:RATED]->(rec) }
        WITH rec,
            collect({readerName: similar.name, score: r3.score, sharedBooks: sharedBooks}) AS evidence
        RETURN rec.title AS title,
               rec.avgRating AS avgRating,
               evidence
        ORDER BY size(evidence) DESC, rec.avgRating DESC
        LIMIT 5
        """;
    private const string GenreFallbackQuery = """
        MATCH (me:Reader {id: $readerId})-[:RATED]->(:Book)-[:HAS_GENRE]->(g:Genre)
        WITH me, g, count(*) AS affinity
        ORDER BY affinity DESC LIMIT 3
        MATCH(g)<-[:HAS_GENRE]-(rec:Book)
        WHERE NOT EXISTS { MATCH(me)-[:RATED]->(rec) }
        RETURN DISTINCT rec.title AS title, rec.avgRating AS avgRating
        ORDER BY rec.avgRating DESC
        LIMIT 5
        """;

    private const string ReadingTwinQuery = """
         MATCH (me:Reader {id: $readerId})-[r1:RATED]->(b:Book)<-[r2:RATED]-(other:Reader)
         WHERE other <> me
         WITH other, count(b) AS sharedBooks, avg(abs(r1.score - r2.score)) AS avgScoreGap
         RETURN other.name AS twin, sharedBooks, avgScoreGap
         ORDER BY sharedBooks DESC, avgScoreGap ASC
         LIMIT 1
         """;


    public RecommendationRepository(IDriver driver) => _driver = driver;

    public async Task<List<ExplainableRecommendation>> GetExplainableRecommendationsAsync(string readerId)
    {
        //await using var session = _driver.AsyncSession();
        try
        {
            var result = await _driver.ExecutableQuery(ExplainableQuery)
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
                    Votes: evidence.Count(),
                    Evidence: evidence);
            }).ToList();
        }
        catch(Neo4jException ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new DatabaseUnavailableException("CognoDB is unreachable right now.", ex);
        }
    }

    public async Task<List<BookRecommendation>> GetGenreFallbackRecommendationAsync(string readerId)
    {
        //await using var session = _driver.AsyncSession();
        try
        {
            var result = await _driver.ExecutableQuery(GenreFallbackQuery)
                .WithParameters(new { readerId })
                .ExecuteAsync();

            return result.Result.Select(r => new BookRecommendation(
                Title: r["title"].As<string>(),
                AvgRating: r["avgRating"].As<double>(),
                Votes: 0
                )).ToList();
        }
        catch(Neo4jException ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new DatabaseUnavailableException("CognoDb is unreachable right now.", ex);
        }
    }

    public async Task<(string TwinName, int SharedBooks, double AvgScoreGap)?> GetReadingTwinAsync(string readerId)
    {
        try
        {
            var result = await _driver.ExecutableQuery(ReadingTwinQuery)
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