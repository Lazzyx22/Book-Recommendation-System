using System;
using System.Collections.Generic;
using System.Text;

namespace BookRecommendationSystem.Data;

public record BookRecommendation(string Title, double AvgRating, int Votes);

public record RecommendationEvidence(string ReaderId,string ReaderName, bool IsFollowed,int Score, List<string> SharedBooks);

public record FollowedReader(string Id, string Name);


public record ExplainableRecommendation(
    string Title,
    double AvgRating,
    //int Votes,
    List<RecommendationEvidence> Evidence)
{
    public int FollowedByCount = Evidence.Count(e => e.IsFollowed);
};

public class DatabaseUnavailableException : Exception
{
    public DatabaseUnavailableException(string message, Exception inner) : base(message, inner) { }

}