using NasdaqTrader.Bot.Core;

namespace AleixBot.Models;

public record IntermediateListing(string Name, DateOnly Date, decimal PriceDifference, decimal Price);