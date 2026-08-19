using System;
using System.Collections.Generic;
using System.Text;

namespace BookRecommendationSystem.Data;

public record BookRecommendation(string Title, double AvgRating, int Votes);

public record RecommendationEvidence(string ReaderName, int Score, List<string> SharedBooks);

public record ExplainableRecommendation(
    string Title,
    double AvgRating,
    int Votes,
    List<RecommendationEvidence> Evidence);

public class DataBaseUnaviableException : Exception
{
    public DataBaseUnaviableException(string message, Exception inner) : base(message, inner) { }
}