using NasdaqTrader.Bot.Core;
using System.Collections.Concurrent;

namespace LaurensBot;

public class LaurensTrader : ITraderBot
{
    public string CompanyName => "Laurens Inc. Investments";
    private decimal _currentHighest = 0;
    private IStockListing? _currentHighestListing = null;
    private HashSet<IStockListing> _boughtToday = [];
    private HashSet<IStockListing> _longBuys = [];
    private ConcurrentDictionary<IStockListing, decimal> _pricesLastDay = [];
    private int _tradesToBuy = 4;
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

        // Wat als ik 1 trade niet verkoop?
        var listings = systemContext.GetListings();
        _boughtToday.Clear();

        ConcurrentDictionary<IStockListing, decimal> pricesToday = [];
        ConcurrentDictionary<IStockListing, decimal> pricesTomorrow = [];

        if (_pricesLastDay.Count == 0)
        {
            foreach (var listing in listings)
            {
                var price = listing.PricePoints.Last()?.Price ?? 0;
                _pricesLastDay.TryAdd(listing, price);
            }
        }

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
        var currentCash = systemContext.GetCurrentCash(this);

        while (_boughtToday.Count < _tradesToBuy)
        {
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

        Parallel.ForEach(pricesToday, x => CalculateIncrease(x.Value, _pricesLastDay[x.Key], x.Key, currentCash));
        if (_currentHighestListing != null)
        {
            var currentPrice = _currentHighestListing.PricePoints.Where(p => p.Date == today).FirstOrDefault()?.Price ?? -1;
            int amountICanBuy = (int)Math.Floor(currentCash / currentPrice);
            int amount = Math.Min(1000, amountICanBuy);
            if (amount != 0)
            {
                systemContext.BuyStock(this, _currentHighestListing, amount);
                _longBuys.Add(_currentHighestListing);
            }
        }

        _tradesToBuy = _tradesToBuy == 2 ? 1 : 2; // Switch between buying 3 and 2 stocks per turn.
        _currentHighestListing = null;
        _currentHighest = 0;
    }

    private static bool _start = false;

    private void SellAll(ITraderSystemContext systemContext)
    {
        List<(IStockListing listing, int amount)> stocksToSell = [];
        foreach (var holding in systemContext.GetHoldings(this))
        {
            if (holding.Amount > 0)
            {
                stocksToSell.Add((holding.Listing, holding.Amount));
            }
        } // Sell all if needed.

        if (!_start)
        {
            var currentCash = systemContext.GetCurrentCash(this);
            _start = currentCash > 100000;
        }
        else
        {
            stocksToSell = stocksToSell.Where(h => !_longBuys.Contains(h.listing)).ToList(); // Start ignoring longs now
        }

        foreach (var holding in stocksToSell)
        {
            systemContext.SellStock(this, holding.listing, holding.amount);
        }
    }
}
