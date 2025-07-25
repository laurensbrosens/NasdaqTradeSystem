using NasdaqTrader.Bot.Core;
using System.Collections.Concurrent;

namespace LaurensBot;

public class LaurensTrader : ITraderBot
{
    public string CompanyName => "Laurens Inc. Investments";
    private decimal _currentHighest = 0;
    private IStockListing? _currentHighestListing = null;
    private HashSet<IStockListing> _boughtToday = [];
    private int _tradesToBuy = 5;
    private object _lock = new object();

    private void CalculateIncrease(decimal a, decimal b, IStockListing listing, decimal budget)
    {
        lock (_lock)
        {
            if (a == 0)
            {
                return;
            }
            //decimal increase = ((b - a) / a);
            int amount = (int)Math.Floor(budget / a);
            decimal increase = (amount * b) - (amount * a);
            if (increase > _currentHighest)
            {
                _currentHighest = increase;
                _currentHighestListing = listing;
            }
        }
    }

    public async Task DoTurn(ITraderSystemContext systemContext)
    {
        /*
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"A new day");
        Console.WriteLine();
        */
        // Problem: je kan slechts 5 transacties doen, je zou kunnen zeggen dat je 3 koopt 2 verkoopt, maar dan zit je geld vast in random aandelen, Als je 2 koopt en 3 verkoopt doe je maar 4
        // transacties wat niet optimaal is. Dus je kan om de dag switchen: 3 kopen 2 verkopen, 2 kopen 3 verkopen etc. Dit is vrij gemakkelijk: verkoop alles en switch tussen 2 en 3 kopen.
        // Ipv. de 3 meest stijgende aandelen, koop je de 3 meest stijgende waarmee het meeste geld weg is (profit is hoger in dat geval!).
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
        SellAll(systemContext);
        int tradesBought = 0;
        while (_boughtToday.Count < _tradesToBuy)
        {
            var currentCash = systemContext.GetCurrentCash(this);

            Parallel.ForEach(pricesToday, x => CalculateIncrease(x.Value, pricesTomorrow[x.Key], x.Key, currentCash));

            if (_currentHighestListing == null)
            {
                break;
            }

            var currentPrice = _currentHighestListing.PricePoints.Where(p => p.Date == today).FirstOrDefault()?.Price ?? -1;
            if (currentPrice == -1)
            {
                pricesToday.Remove(_currentHighestListing, out _);
                _currentHighestListing = null;
                _currentHighest = 0;
                continue;
            }

            int amountICanBuy = (int)Math.Floor(currentCash / currentPrice);

            int amount = Math.Min(1000, amountICanBuy);

            if (amount != 0)
            {
                var success = systemContext.BuyStock(this, _currentHighestListing, amount);
                if (!success)
                {
                    break;
                }
                _boughtToday.Add(_currentHighestListing);
                pricesToday.Remove(_currentHighestListing, out _);
            }
            else
            {
                break;
            }


            _currentHighestListing = null;
            _currentHighest = 0;
        }
        _tradesToBuy = _tradesToBuy == 3 ? 2 : 3; // Switch between buying 3 and 2 stocks per turn.
        _currentHighestListing = null;
        _currentHighest = 0;
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
