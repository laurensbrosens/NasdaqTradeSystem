using NasdaqTrader.Bot.Core;
using System.Diagnostics;
using System.Diagnostics.Metrics;


namespace AleixBot;

public class AleixBot : ITraderBot
{
    public string CompanyName => "Beginner Investments";
    public static ListingPicker _listingPicker = null;
    private static int _counter = 0;
    private const int WINDOWSIZE = 3;

    public async Task DoTurn(ITraderSystemContext systemContext)
    {
        var listings = systemContext.GetListings();
        var cash = systemContext.GetCurrentCash(this);
        var currentDate = systemContext.CurrentDate;
        var tradesLeft = systemContext.GetTradesLeftForToday(this);

        if (_listingPicker == null)
        {
            InitializeListingPicker(listings, WINDOWSIZE);
        }

        if (_counter % WINDOWSIZE == 0 || _counter == 0)
        {
            //TODO implement while loop for trading
            if (tradesLeft <= 0)
            {
                _counter++;
                return;
            }

            if (systemContext.GetHoldings(this).Any() && tradesLeft > 0)
            {
                tradesLeft = Sell(systemContext, tradesLeft);
            }                

            cash = systemContext.GetCurrentCash(this);

            if (tradesLeft > 0 && cash > 0)
            {
                tradesLeft = Buy(systemContext, cash, currentDate, tradesLeft);
            }
        }

        _counter++;
    }

    private int Buy(ITraderSystemContext systemContext, decimal cash, DateOnly currentDate, int tradesLeft)
    {
        var listing = _listingPicker.GetXBestListingForDate(1, currentDate, cash);
        var pricePoint = listing?.PricePoints.FirstOrDefault(l => l.Date == currentDate);

        if (pricePoint is not null && cash >= pricePoint.Price)
        {
            systemContext.BuyStock(this, listing, (int)(cash / pricePoint.Price));
            tradesLeft--;
        }

        return tradesLeft;
    }

    private int Sell(ITraderSystemContext systemContext, int tradesLeft)
    {
        var holding = systemContext.GetHoldings(this).OrderByDescending(h => h.Amount).FirstOrDefault();
        if (holding != null)
        {
            systemContext.SellStock(this, holding.Listing, holding.Amount);
            tradesLeft--;
        }

        return tradesLeft;
    }

    private static void InitializeListingPicker(System.Collections.ObjectModel.ReadOnlyCollection<IStockListing> listings, int windowSize)
    {
        var sw = new Stopwatch();
        sw.Start();
        _listingPicker = new ListingPicker(listings, windowSize);
        sw.Stop();
        Debug.WriteLine($"Building ListingPicker took {sw.ElapsedMilliseconds} milliseconds");
    }
}