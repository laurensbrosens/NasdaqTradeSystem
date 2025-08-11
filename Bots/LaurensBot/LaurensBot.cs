using NasdaqTrader.Bot.Core;
using System.Collections.Concurrent;

namespace LaurensBot;

public class TradeAction
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal PercentageIncrease { get; set; }
    public decimal StartPrice { get; set; }
    public decimal EndPrice { get; set; }
    public IStockListing Listing { get; set; } = null;
    public int Amount { get; set; } = 0;
}

public class LaurensTrader : ITraderBot
{
    public string CompanyName => "Laurens Inc. Investments";
    private bool _initial = true;
    private ITraderSystemContext _systemContext = null!;
    private Dictionary<DateOnly, List<TradeAction>> _tradeBuyPlanner = [];

    //private Lookup<DateOnly, TradeAction> Test = new Dictionary<DateOnly, List<TradeAction>>().ToLookup();//.ToLookup<DateOnly, TradeAction>();
    private Dictionary<DateOnly, List<TradeAction>> _tradeSellPlanner = [];

    public async Task DoTurn(ITraderSystemContext systemContext)
    {
        _systemContext = systemContext;
        if (_initial)
        {
            _initial = false;
            try
            {
                await InitialCalculations();
            }
            catch (Exception e)
            {
                var test = e;
                throw;
            }
        }
        foreach (var tradeAction in _tradeSellPlanner[systemContext.CurrentDate] ?? [])
        {
            systemContext.SellStock(this, tradeAction.Listing, tradeAction.Amount);
        }
        foreach (var tradeAction in _tradeBuyPlanner[systemContext.CurrentDate] ?? [])
        {
            systemContext.BuyStock(this, tradeAction.Listing, tradeAction.Amount);
        }

        await Task.CompletedTask;
    }

    private Task InitialCalculations()
    {
        // De bruteforce manier om dit te doen zou zijn loopen over alle dagen en hoogste stijgingen berekenen voor elke startdag met 1 dag in de toekomst of 2 etc.

        // Om alles veel sneller te maken zou ik een threshold kunnen instellen waarbij een bepaalde start/einddag geskipped wordt als er voldoende trades zijn en de stijging hoog genoeg is?

        var startDate = _systemContext.StartDate;
        var endDate = _systemContext.EndDate;
        var listings = _systemContext.GetListings().ToList();
        var allTradeActions = CalculateAllPossibleActions(listings);
        var allActions = allTradeActions.OrderBy(a => a.StartDate).GroupBy(a => a.StartDate);
        var currentCash = _systemContext.GetCurrentCash(this);
        foreach (var actionsOnDate in allActions)
        {
            if (_tradeSellPlanner.TryGetValue(actionsOnDate.FirstOrDefault()?.StartDate ?? DateOnly.MinValue, out var sellTrades))
            {
                foreach (var trade in sellTrades)
                {
                    currentCash += trade.EndPrice * trade.Amount;
                }
            }

            foreach (var action in actionsOnDate.OrderBy(a => a.PercentageIncrease).Reverse())
            {
                if (!CanDoTradesOnThisDate(action.StartDate))
                {
                    break;
                }
                if (!CanDoTradesOnThisDate(action.EndDate))
                {
                    break;
                }

                int amount = Math.Min(1000, (int)Math.Floor(currentCash / action.StartPrice));
                if(amount <= 0)
                {
                    continue;
                }
                action.Amount = amount;
                currentCash -= amount;

                if (!_tradeBuyPlanner.TryGetValue(action.StartDate, out var tradesOnStartDay))
                {
                    tradesOnStartDay = [];
                    _tradeBuyPlanner.Add(action.StartDate, tradesOnStartDay);
                }
                if (!_tradeSellPlanner.TryGetValue(action.EndDate, out var tradesOnEndDay))
                {
                    tradesOnEndDay = [];
                    _tradeSellPlanner.Add(action.EndDate, tradesOnEndDay);
                }
                tradesOnStartDay.Add(action);
                tradesOnEndDay.Add(action);
            }
        }
        // _allTradeActions.Where(a => a.StartDate == _systemContext.CurrentDate).OrderBy(a => a.PercentageIncrease);

        // Loop door alle _activeTrades en verkoop diegene met een einddatum van vandaag
        // Loop door alle _allTradeActions voor vandaag en koop diegene 1. als er nog handelingen gedaan kunnen worden vandaag 2. amount groot genoeg is 3. einddatum nog niet vol zit

        return Task.CompletedTask;
    }

    private bool CanDoTradesOnThisDate(DateOnly date)
    {
        int sellCount = 0;
        int buyCount = 0;
        if (_tradeSellPlanner.TryGetValue(date, out var sellTrades))
        {
            sellCount = sellTrades.Count;
        }
        if (_tradeBuyPlanner.TryGetValue(date, out var buyTrades))
        {
            buyCount = buyTrades.Count;
        }
        return sellCount + buyCount <= _systemContext.AmountOfTradesPerDay - 1;
    }

    private ConcurrentBag<TradeAction> CalculateAllPossibleActions(List<IStockListing> listings)
    {
        ConcurrentBag<TradeAction> allTradeActions = [];
        foreach (var listing in listings)
        {
            var pricePoints = listing.PricePoints;

            for (int i = 0; i < listing.PricePoints.Length; i++)
            {
                for (int j = i + 1; j < listing.PricePoints.Length; j++)
                {
                    var startPricePoint = listing.PricePoints[i];
                    var endPricePoint = listing.PricePoints[j];
                    var increase = CalculateIncrease(startPricePoint.Price, endPricePoint.Price);
                    allTradeActions.Add(
                        new TradeAction()
                        {
                            StartDate = startPricePoint.Date,
                            EndDate = endPricePoint.Date,
                            StartPrice = startPricePoint.Price,
                            EndPrice = endPricePoint.Price,
                            PercentageIncrease = increase,
                            Listing = listing
                        }
                    );
                }
            }
        }
        return allTradeActions;
    }

    private decimal CalculateIncrease(decimal a, decimal b)
    {
        if (a == 0)
        {
            return -1;
        }
        return ((b - a) / a);
    }
    /* Doesn't work because pricepoints aren't 365 days
    private DateOnly[] AllDates(DateOnly startDate, DateOnly endDate)
    {
        List<DateOnly> dates = [];
        DateOnly currentDate = startDate;
        while (currentDate <= endDate)
        {
            dates.Add(currentDate);
            currentDate = currentDate.AddDays(1);
        }
        return dates.ToArray();
    }
    */
    // Idee: bereken alles upfront en ga daarna pas 1 voor 1 door alle trades obv. wat berekend is geraakt
    // Simpelweg de best mogelijk trades berekenen met 1 startdatum en 1 einddatum
    // Als de trades voor de startdatums volzitten skip deze dan, als de trades voor de einddatum volzitten skip de trade
    // Sorteer op prijsincrease (ongeacht datum), eigenlijk zou er een multiplier moeten zijn om grote increases op lange termijn een voordeel te geven?




    /* Legacy code
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
        //Console.WriteLine();
        //Console.WriteLine();
        //Console.WriteLine($"A new day");
        //Console.WriteLine();
        
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
                var price = listing.PricePoints.Reverse().Skip(1).Take(1).First()?.Price; // Take secondlast since last is currently often an invalid value
                if(price != null)
                {
                    _pricesLastDay.TryAdd(listing, price ?? 0);
                }
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
        _boughtYesterday.Clear();
        var currentCash = systemContext.GetCurrentCash(this);

        while (_boughtToday.Count < 2)
        {
            // Dit is momenteel een 1 dag lookahead. Maar ik zou eigenlijk de trades moeten pakken die over een periode van minstens 2 dagen goed zijn?
            // Dus loopen over alle increases voor 1 en 2 dagen en dan de beste hiervoor pakken tot ik geen trades meer mag doen en dan over een langere termijn.
            // Dus blijven doorgaan tot mijn trades voor 1 dag opzijn dan?
            // Ik veronderstel dat er dagen zijn waarop er veel tezamen stijgt. In dat geval kan best vantevoren al veel aangekocht worden. De enige manier waarop je dit zou kunnen doen is door
            // van achter naar voor te loopen

            // Todo: negeer null prijzen i.p.v. ze om te zetten naar 0
            // Todo: backward propagation achtig iets gebruiken?
            // Loop door alle trades, koop
            // Of: altijd de 4 gebruiken om de beste trades te kopen/verkopen en de laatste enkel gebruiken om long te gaan? (Altijd enkel verkopen wat er met de 4 gekocht was om reset van de longs te voorkomen)
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
                currentCash = systemContext.GetCurrentCash(this);
                if (!success)
                {
                    break;
                }
                _boughtToday.Add(_currentHighestListing);
                _boughtYesterday.Add((_currentHighestListing, amount));
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
        if (_currentHighestListing != null && currentCash > 100000)
        {
            currentCash = currentCash / 2;
            var currentPrice = _currentHighestListing.PricePoints.Where(p => p.Date == today).FirstOrDefault()?.Price ?? -1;
            int amountICanBuy = (int)Math.Floor(currentCash / currentPrice);
            int amount = Math.Min(1000, amountICanBuy);
            if (amount != 0)
            {
                systemContext.BuyStock(this, _currentHighestListing, amount); // Buy long with all remaining cash.
                _longBuys.Add(_currentHighestListing);
            }
        }

        _tradesToBuy = _tradesToBuy == 2 ? 1 : 2; // Switch between buying 3 and 2 stocks per turn.
        _currentHighestListing = null;
        _currentHighest = 0;
    }

    private static List<(IStockListing, int)> _boughtYesterday = [];
    private static bool _start = false;

    private void SellAll(ITraderSystemContext systemContext)
    {
        
        //List<(IStockListing listing, int amount)> stocksToSell = [];
        //foreach (var holding in systemContext.GetHoldings(this))
        //{
        //    if (holding.Amount > 0)
        //    {
        //        stocksToSell.Add((holding.Listing, holding.Amount));
        //    }
        //} // Sell all if needed.

        //if (!_start)
        //{
        //    var currentCash = systemContext.GetCurrentCash(this);
        //    _start = currentCash > 100000;
        //}
        //else
        //{
        //    stocksToSell = stocksToSell.Where(h => !_longBuys.Contains(h.listing)).ToList(); // Start ignoring longs now
        //}

        foreach (var (listing, amount) in _boughtYesterday)
        {
            systemContext.SellStock(this, listing, amount);
        }
    }*/
}
