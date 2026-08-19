using System.Text.Json;
using BookRecommendationSystem.Seed;

var json = File.ReadAllText("seed_data.json");
var seed = JsonSerializer.Deserialize<SeedData>(json)!;

Console.WriteLine($"Loaded {seed.Readers.Count} readers, {seed.Books.Count} books, {seed.Ratings.Count} ratings");