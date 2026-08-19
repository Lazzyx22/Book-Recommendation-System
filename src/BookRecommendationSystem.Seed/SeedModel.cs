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