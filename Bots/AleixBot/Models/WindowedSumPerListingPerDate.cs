namespace AleixBot.Models;

public record WindowedSumPerListingPerDate(DateOnly Date, string Name, decimal Sum, decimal PriceOnStartDate);