using NasdaqTrader.Bot.Core;
using System.Collections.Concurrent;

namespace LaurensBot;

public class LaurensTrader : ITraderBot
{
    public string CompanyName => "Laurens Inc. Investments";
    private decimal _currentHighest = 0;
    private IStockListing _currentHighestListing = null;

    private void CalculateIncrease(decimal a, decimal b, IStockListing listing)
    {
        if (a == 0)
        {
            return;
        }

        decimal increase = ((b - a) / a);
        if (increase > _currentHighest)
        {
            _currentHighest = increase;
            _currentHighestListing = listing;
        }
    }

    public async Task DoTurn(ITraderSystemContext systemContext)
    {
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"A new day");
        Console.WriteLine();
        var listings = systemContext.GetListings();

        foreach (var holding in systemContext.GetHoldings(this).ToList())
        {
            var amount = holding.Amount;
            var listing = holding.Listing;
            if (amount > 0)
            {
                systemContext.SellStock(this, listing, amount);
                Console.WriteLine($"I sold {amount} times {listing}");
            }
        } // Sell all
        ConcurrentDictionary<IStockListing, decimal> pricesToday = [];
        ConcurrentDictionary<IStockListing, decimal> pricesTomorrow = [];
        var today = systemContext.CurrentDate;

        var tomorrow = systemContext.CurrentDate.AddDays(1);
        foreach (var listing in listings)
        {
            var price = listing.PricePoints.Where(p => p.Date == today).FirstOrDefault()?.Price ?? 0;
            var priceTomorrow = listing.PricePoints.Where(p => p.Date == tomorrow).FirstOrDefault()?.Price ?? 0;
            pricesToday.TryAdd(listing, price);
            pricesTomorrow.TryAdd(listing, priceTomorrow);
        }

        while (true)
        {
            var currentCash = systemContext.GetCurrentCash(this);
            if (currentCash <= 0)
            {
                return;
            }
            Parallel.ForEach(pricesToday, x => CalculateIncrease(x.Value, pricesTomorrow[x.Key], x.Key));

            if (_currentHighestListing == null)
            {
                break;
            }

            var listing = listings.Where(l => l == _currentHighestListing).FirstOrDefault();
            if (listing is null)
            {
                continue;
            }

            int amount = 1000;

            var currentPrice = listing.PricePoints.Where(p => p.Date == today).FirstOrDefault()?.Price ?? -1;
            if (currentPrice == -1)
            {
                continue;
            }
            amount = Math.Min(amount, (int)Math.Floor(currentCash / currentPrice));

            var listingToBuy = listings.Where(l => l == _currentHighestListing).FirstOrDefault();
            if (listingToBuy is null)
            {
                continue;
            }

            if (amount != 0)
            {
                var success = systemContext.BuyStock(this, listingToBuy, amount);
                if (!success)
                {
                    break;
                }
                Console.WriteLine($"I bought {amount} times {currentPrice}");
            }
            else
            {
                break;
            }

            pricesToday.Remove(_currentHighestListing, out _);
            _currentHighestListing = null;
            _currentHighest = 0;
        }
    }
}
