using NasdaqTrader.Bot.Core;
using System.Collections.Concurrent;

namespace LaurensBot;
public class LaurensTrader : ITraderBot
{
    public string CompanyName => "Laurens Inc. Investments";

    public async Task DoTurn(ITraderSystemContext systemContext)
    {
        var listings = systemContext.GetListings();

        Parallel.ForEach(systemContext.GetHoldings(this), x => systemContext.SellStock(this, x.Listing, x.Amount)); // Sell all

        // Loop door alle holdings voor vandaag
        // Loop door alle holdings voor morgen (zou je kunnen cachen want dat worden die van vandaag?)
        // Koop alle aandelen waar de stijging het grootst is?

        ConcurrentDictionary<string, decimal> pricesToday = [];
        ConcurrentDictionary<string, decimal> pricesTomorrow = [];
        var today = systemContext.CurrentDate;
        var tomorrow = systemContext.CurrentDate.AddDays(1);
        foreach (var listing in listings)
        {
            var ticker = listing.Ticker;
            var price = listing.PricePoints.Where(p => p.Date == today).First().Price;
            var priceTomorrow = listing.PricePoints.Where(p => p.Date == tomorrow).First().Price;
            pricesToday.TryAdd(ticker, price);
            pricesTomorrow.TryAdd(ticker, priceTomorrow);
        }

        // Bereken percentage increase tussen prijzen
        // Sorteer percentages van hoog naar laag
        // Koop zo veel als mogelijk
        // ??
        // Profit

        var tradeListing = listings
            .Where(c => c.PricePoints.Any(p => p.Date == systemContext.CurrentDate) && c.PricePoints.Any(p => p.Date == systemContext.CurrentDate.AddDays(1)))
            .MaxBy(c =>
                c.PricePoints.FirstOrDefault(p => p.Date == systemContext.CurrentDate.AddDays(1))?.Price -
                c.PricePoints.FirstOrDefault(p => p.Date == systemContext.CurrentDate)?.Price);

        if (tradeListing == null)
        {
            return;
        }

        var pricePoint = tradeListing.PricePoints.FirstOrDefault(p => p.Date == systemContext.CurrentDate);
        if (pricePoint == null)
        {
            return;
        }

        systemContext.BuyStock(this, tradeListing, Math.Min(1000, (int)(systemContext.GetCurrentCash(this) / pricePoint.Price)));
    }
}