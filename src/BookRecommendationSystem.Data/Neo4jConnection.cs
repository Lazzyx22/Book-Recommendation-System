using Neo4j.Driver;
using System;
using System.Collections.Generic;
using System.Text;

namespace BookRecommendationSystem.Data;
public static class Neo4jConnection
{
    public static IDriver CreateDriver(CognoDbSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Uri) || string.IsNullOrWhiteSpace(settings.Password))
            throw new InvalidOperationException(
                 "CognoDb connection settings are missing. Set CognoDb:Uri / CognoDb:User / CognoDb:Password " +
                "via user-secrets (dev) or environment variables (hosted)."
                );
        return GraphDatabase.Driver(settings.Uri, AuthTokens.Basic(settings.User, settings.Password));
    }
}