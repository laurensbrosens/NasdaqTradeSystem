using NasdaqTrader.Bot.Core;
using System.Collections.Concurrent;

namespace LaurensBot;

public class LaurensTrader : ITraderBot
{
    public string CompanyName => "Laurens Inc. Investments";
    private decimal _currentHighest = 0;
    private IStockListing? _currentHighestListing = null;
    private HashSet<IStockListing> _boughtToday = [];

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

        // Problem: selling overpowers buying now, it would be beneficial to buy first, sell and if possible buy again?
        var listings = systemContext.GetListings();
        _boughtToday.Clear();

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
        bool sold = false;
        //SellAll(systemContext);
        //sold = true;
        while (true)
        {
            var currentCash = systemContext.GetCurrentCash(this);

            Parallel.ForEach(pricesToday, x => CalculateIncrease(x.Value, pricesTomorrow[x.Key], x.Key));

            if (_currentHighestListing == null)
            {
                SellAll(systemContext);
                sold = true;
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
                pricesToday.Remove(_currentHighestListing, out _);
                _currentHighestListing = null;
                _currentHighest = 0;
                continue;
            }
            amount = Math.Min(amount, (int)Math.Floor(currentCash / currentPrice));

            if (amount == 0 && !sold)
            {
                SellAll(systemContext);
                sold = true;
                amount = Math.Min(amount, (int)Math.Floor(currentCash / currentPrice));
            }

            if (amount != 0)
            {
                var success = systemContext.BuyStock(this, _currentHighestListing, amount);
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

            _boughtToday.Add(_currentHighestListing);
            pricesToday.Remove(_currentHighestListing, out _);
            _currentHighestListing = null;
            _currentHighest = 0;
        }
    }

    private void SellAll(ITraderSystemContext systemContext)
    {
        foreach (var holding in systemContext.GetHoldings(this).Where(h => !_boughtToday.Contains(h.Listing)).ToList())
        {
            if (holding.Amount > 0)
            {
                systemContext.SellStock(this, holding.Listing, holding.Amount);
            }
        } // Sell all if needed.
    }
}
