using Neo4j.Driver;

namespace BookRecommendationSystem.Data;

public class RecommendationRepository
{
    private readonly IDriver _driver;
    private const string ExplainableQuery = """
    MATCH (me:Reader {id: $readerId})-[r1:RATED]->(shared:Book)<-[r2:RATED]-(similar:Reader)
    WHERE r1.score >= 4 AND r2.score >= 4 AND similar <> me
    WITH me, similar, collect(shared.title) AS sharedBooks, count(shared) AS overlap
    ORDER BY overlap DESC
    LIMIT 10
    MATCH (similar)-[r3:RATED]->(rec:Book)
    WHERE r3.score >= 4
    OPTIONAL MATCH (me)-[already:RATED]->(rec)
    WITH rec, similar, r3, sharedBooks, already
    WHERE already IS NULL
    OPTIONAL MATCH (me)-[fw:FOLLOWS]->(similar)
    WITH rec, similar, r3, sharedBooks, fw IS NOT NULL AS isFollowed
    WITH rec,
         collect({readerId: similar.id, readerName: similar.name, score: r3.score,
                   sharedBooks: sharedBooks, isFollowed: isFollowed}) AS evidence
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
        MATCH(g)<-[:HAS_GENRE]-(rec:Book)
        WHERE NOT EXISTS { MATCH(me)-[:RATED]->(rec) }
        RETURN DISTINCT rec.title AS title, rec.avgRating AS avgRating
        ORDER BY rec.avgRating DESC
        LIMIT 5
        """;

    // Third tier: for a reader with zero ratings (so GenreFallbackQuery also comes up
    // empty, since it needs at least one RATED->HAS_GENRE chain to find a genre at all).
    // No personalization possible yet, so just surface the highest-rated books overall.
    private const string PopularFallbackQuery = """
        MATCH (rec:Book)
        WHERE rec.avgRating IS NOT NULL
        RETURN rec.title AS title, rec.avgRating AS avgRating
        ORDER BY rec.avgRating DESC
        LIMIT 5
        """;

    private const string ReadingTwinQuery = """
        MATCH (me:Reader {id: $readerId})-[r1:RATED]->(b:Book)<-[r2:RATED]-(other:Reader)
        WHERE other <> me
        WITH other, count(b) AS sharedBooks, avg(abs(r1.score - r2.score)) AS avgScoreGap
        RETURN other.id AS twinId, other.name AS twin, sharedBooks, avgScoreGap
        ORDER BY sharedBooks DESC, avgScoreGap ASC
        LIMIT 1
        """;

    public RecommendationRepository(IDriver driver) => _driver = driver;

    public async Task<List<BookRecommendation>> GetGenreFallbackRecommendationsAsync(string readerId)
    {
        try
        {
            var result = await _driver.ExecutableQuery(GenreFallbackQuery)
                .WithParameters(new { readerId })
                .ExecuteAsync();

            var books = result.Result.Select(r => new BookRecommendation(
                Title: r["title"].As<string>(),
                AvgRating: r["avgRating"].As<double>(),
                Votes: 0
            )).ToList();

            if (books.Count > 0) return books;

            // Reader has no genre affinity signal at all (zero ratings) — fall back
            // to overall popularity so the page never dead-ends on a real reader.
            var popular = await _driver.ExecutableQuery(PopularFallbackQuery)
                .ExecuteAsync();

            return popular.Result.Select(r => new BookRecommendation(
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

    public async Task<List<ExplainableRecommendation>> GetExplainableRecommendationsAsync(string readerId)
    {
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
                            ReaderId: map["readerId"].As<string>(),
                            ReaderName: map["readerName"].As<string>(),
                            Score: map["score"].As<int>(),
                            SharedBooks: map["sharedBooks"].As<List<object>>()
                                            .Select(b => b.As<string>()).ToList(),
                            IsFollowed: map["isFollowed"].As<bool>());
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

    public async Task<(string TwinId, string TwinName, int SharedBooks, double AvgScoreGap)?> GetReadingTwinAsync(string readerId)
    {
        try
        {
            var result = await _driver.ExecutableQuery(ReadingTwinQuery)
                .WithParameters(new { readerId })
                .ExecuteAsync();

            var record = result.Result.FirstOrDefault();
            if (record is null) return null;

            return (record["twinId"].As<string>(),
                    record["twin"].As<string>(),
                    record["sharedBooks"].As<int>(),
                    record["avgScoreGap"].As<double>());
        }
        catch (Neo4jException ex) when (ex is ServiceUnavailableException or AuthenticationException)
        {
            throw new DatabaseUnavailableException("CognoDB is unreachable right now.", ex);
        }
    }

}