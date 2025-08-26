using NasdaqTrader.Bot.Core;
using System.Collections.Concurrent;

namespace LaurensBot;

public class TradeAction
{
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public decimal PercentageIncrease { get; init; }
    public decimal StartPrice { get; init; }
    public decimal EndPrice { get; init; }
    public IStockListing Listing { get; init; } = null;
    public int Amount { get; init; } = 0;

    public TradeAction Copy(int amount)
    {
        return new TradeAction()
        {
            StartDate = StartDate,
            EndDate = EndDate,
            PercentageIncrease = PercentageIncrease,
            StartPrice = StartPrice,
            EndPrice = EndPrice,
            Listing = Listing,
            Amount = Amount
        };
    }
}

public class LaurensTrader : ITraderBot
{
    public string CompanyName => "Laurens Inc. Investments";
    private bool _initial = true;
    private ITraderSystemContext _systemContext = null!;
    private Dictionary<DateOnly, List<TradeAction>> _tradeBuyPlanner = [];
    private decimal _initialCash;

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
        try
        {
            //Console.WriteLine($"Todays date is {systemContext.CurrentDate}");
            _tradeSellPlanner.TryGetValue(systemContext.CurrentDate, out var sellTrades);
            foreach (var tradeAction in sellTrades ?? [])
            {
                var currentCash = _systemContext.GetCurrentCash(this);
                var sellFailed = !systemContext.SellStock(this, tradeAction.Listing, tradeAction.Amount);
                //Console.WriteLine($"I sold {tradeAction.Amount} times {tradeAction.Listing.Name} at {tradeAction.EndPrice} on {systemContext.CurrentDate}");
                if (sellFailed)
                {
                    //Console.WriteLine($"But I failed?");
                    var test = 1;
                }
            }

            _tradeBuyPlanner.TryGetValue(systemContext.CurrentDate, out var buyTrades);
            foreach (var tradeAction in buyTrades ?? [])
            {
                var buyFailed = !systemContext.BuyStock(this, tradeAction.Listing, tradeAction.Amount);
                //Console.WriteLine($"I bought {tradeAction.Amount} times {tradeAction.Listing.Name} at {tradeAction.StartPrice} on {systemContext.CurrentDate}");
                if (buyFailed)
                {
                    //Console.WriteLine($"But I failed?");
                    var test = 1;
                }
            }
        }
        catch (Exception)
        {
            throw;
        }
        await Task.CompletedTask;
    }

    private bool _sellEarly = true;
    private int _threshold = 100000;
    private decimal _multiplier = 1.001M; // To discourage buying long, unless very good gain.

    private decimal CalculateProjectedGain(decimal increase, int dayRange)
    {
        if (dayRange > 3)
        {
            return 0;
        }
        if (dayRange == 1)
        {
            return increase;
        }
        var gain = increase / ((dayRange - 1) * _multiplier);
        return gain;
    }

    private Task InitialCalculations()
    {
        // De bruteforce manier om dit te doen zou zijn loopen over alle dagen en hoogste stijgingen berekenen voor elke startdag met 1 dag in de toekomst of 2 etc.

        // Om alles veel sneller te maken zou ik een threshold kunnen instellen waarbij een bepaalde start/einddag geskipped wordt als er voldoende trades zijn en de stijging hoog genoeg is?

        // Het is niet de stijging per aankoop of de absolute winst per aankoop maar de absolute winst in een bepaalde periode waar ik naar zou moeten kijken.
        // Dus een periode van 14 dagen winst met 1 aankoop vergelijken met diezelfde periode met meerdere aankopen!
        // Dus de beste kopen of een reeks, wiens som beter is dan de beste? De totale hoeveelheid reeksen is wss imens groot wel.

        // Of een formule verzinnen die als parameters heeft bechikbare cash, percentage stijging en lengte begin/einddatum.
        // 4% stijging als maatstaf voor nu, dan is kopen/verkopen beter als stijging / ((aantal dagen - 1) * x)?

        // Hoe slecht is kopen/verkopen op 2 dagen?
        // Of beter algo. Koop beste increase en verkoop volgende dag. Loop door alle dagen van het jaar. Doe dit 5 keer.
        // Dan heb ik alles gekocht. Loop dan door deze chains en zoek sequenties die beter zijn, startend vanaf het einde van het jaar.
        // Zou makkelijk veriefiëerbaar moeten zijn of een strategie beter is.
        // Is een beetje zoals backpropagation? Kan gestopt worden vanaf dat de tijd op is!

        var startDate = _systemContext.StartDate;
        var endDate = _systemContext.EndDate;
        var listings = _systemContext.GetListings().ToList();
        var allTradeActions = CalculateAllPossibleActions(listings);
        var allActions = allTradeActions.OrderBy(a => a.StartDate).GroupBy(a => a.StartDate);
        var currentCash = _initialCash = _systemContext.GetCurrentCash(this);
        foreach (var actionsOnDate in allActions)
        {
            if (_tradeSellPlanner.TryGetValue(actionsOnDate.FirstOrDefault()?.StartDate ?? DateOnly.MinValue, out var sellTrades))
            {
                foreach (var trade in sellTrades)
                {
                    currentCash += trade.EndPrice * trade.Amount;
                }
            }
            IEnumerable<TradeAction> sortedActions = actionsOnDate.Where(a => a.EndDate.DayNumber - a.StartDate.DayNumber <= 1).OrderByDescending(a => a.PercentageIncrease);
            foreach (var action in sortedActions)
            {
                if(action.PercentageIncrease <= 0)
                {
                    continue;
                }

                if (!CanDoTradesOnThisDate(action.StartDate))
                {
                    break;
                }
                if (!CanDoTradesOnThisDate(action.EndDate))
                {
                    break;
                }

                int amount = Math.Min(1000, (int)Math.Floor(currentCash / action.StartPrice));
                if (amount <= 0)
                {
                    continue;
                }
                var actionCopy = action.Copy(amount);
                currentCash -= amount * actionCopy.StartPrice;

                if (!_tradeBuyPlanner.TryGetValue(actionCopy.StartDate, out var tradesOnStartDay))
                {
                    tradesOnStartDay = [];
                    _tradeBuyPlanner.Add(actionCopy.StartDate, tradesOnStartDay);
                }
                if (!_tradeSellPlanner.TryGetValue(actionCopy.EndDate, out var tradesOnEndDay))
                {
                    tradesOnEndDay = [];
                    _tradeSellPlanner.Add(actionCopy.EndDate, tradesOnEndDay);
                }
                tradesOnStartDay.Add(actionCopy);
                tradesOnEndDay.Add(actionCopy);
            }
        }
        // _allTradeActions.Where(a => a.StartDate == _systemContext.CurrentDate).OrderBy(a => a.PercentageIncrease);

        // Loop door alle _activeTrades en verkoop diegene met een einddatum van vandaag
        // Loop door alle _allTradeActions voor vandaag en koop diegene 1. als er nog handelingen gedaan kunnen worden vandaag 2. amount groot genoeg is 3. einddatum nog niet vol zit

        OptimizeTrades(currentCash + _tradeBuyPlanner.Values.LastOrDefault()?.Sum(a => a.Amount * a.EndPrice) ?? 0);

        return Task.CompletedTask;
    }

    private void OptimizeTrades(decimal initialCash)
    {
        decimal currentBest = initialCash;

        // Loop door alle trades
        // Filter trades met een hogere efficientie en een latere einddatum (verwijder de slechtste trade die overlapt), houdt alle andere trades hetzelfde
        // Voer aanpassing door als het totale resultaat beter is, loop anders naar de volgende mogelijke actie.
        // Probleem: soms is niets doen ook + een latere trade beter dan altijd iets doen!
        var betterTradeSellPlanner = _tradeSellPlanner.ToDictionary();
        var betterTradeBuyPlanner = _tradeBuyPlanner.ToDictionary();

        // Do something
        // Loop through every single action and try to insert the absolute best ones, after that is done go through again and "fill" the gaps with CalculateIncreaseWithBudget?


        if (CalculateTotalGain(betterTradeSellPlanner, betterTradeBuyPlanner) > currentBest)
        {

        }


        var test = initialCash;
    }

    private decimal CalculateTotalGain(Dictionary<DateOnly, List<TradeAction>> tradeSellPlanner, Dictionary<DateOnly, List<TradeAction>> tradeBuyPlanner)
    {
        List<DateOnly> dates = tradeSellPlanner.Keys.Concat(tradeBuyPlanner.Keys).Distinct().OrderByDescending(d => d.DayNumber).ToList();
        var currentCash = _initialCash;

        foreach (var date in dates)
        {
            _tradeSellPlanner.TryGetValue(date, out var sellTrades);
            foreach (var tradeAction in sellTrades ?? [])
            {
                currentCash += tradeAction.Amount * tradeAction.EndPrice;
            }

            _tradeBuyPlanner.TryGetValue(date, out var buyTrades);
            foreach (var tradeAction in buyTrades ?? [])
            {
                currentCash -= tradeAction.Amount * tradeAction.StartPrice;
            }
        }

        return currentCash;
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
                    if (startPricePoint.Date.IsFederalHoliday())
                    {
                        continue;
                    }
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

    private decimal CalculateIncreaseWithBudget(decimal a, decimal b, decimal budget)
    {
            if (a == 0)
            {
                return -1;
            }
            int amount = (int)Math.Floor(budget / a);
            return (amount * b) - (amount * a);
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

internal static class DateExtension
{
    /// <summary>
    /// Determines if this date is a federal holiday.
    /// </summary>
    /// <param name="date">This date</param>
    /// <returns>True if this date is a federal holiday</returns>
    public static bool IsFederalHoliday(this DateOnly date)
    {
        // to ease typing
        int nthWeekDay = (int)(Math.Ceiling((double)date.Day / 7.0d));
        DayOfWeek dayName = date.DayOfWeek;
        bool isThursday = dayName == DayOfWeek.Thursday;
        bool isFriday = dayName == DayOfWeek.Friday;
        bool isMonday = dayName == DayOfWeek.Monday;
        bool isWeekend = dayName == DayOfWeek.Saturday || dayName == DayOfWeek.Sunday;

        //Junteeth
        if (new DateOnly(date.Year, 6, 19) == date)
            return true;
        //good friday
        if (DateOnly.FromDateTime(EasterSunday(date.Year)).AddDays(-2) == date)
            return true;

        // New Years Day (Jan 1, or preceding Friday/following Monday if weekend)
        if ((date.Month == 12 && date.Day == 31 && isFriday) || (date.Month == 1 && date.Day == 1 && !isWeekend) || (date.Month == 1 && date.Day == 2 && isMonday))
            return true;

        // MLK day (3rd monday in January)
        if (date.Month == 1 && isMonday && nthWeekDay == 3)
            return true;

        // President’s Day (3rd Monday in February)
        if (date.Month == 2 && isMonday && nthWeekDay == 3)
            return true;

        // Memorial Day (Last Monday in May)
        if (date.Month == 5 && isMonday && date.AddDays(7).Month == 6)
            return true;

        // Independence Day (July 4, or preceding Friday/following Monday if weekend)
        if ((date.Month == 7 && date.Day == 3 && isFriday) || (date.Month == 7 && date.Day == 4 && !isWeekend) || (date.Month == 7 && date.Day == 5 && isMonday))
            return true;

        // Labor Day (1st Monday in September)
        if (date.Month == 9 && isMonday && nthWeekDay == 1)
            return true;

        // Columbus Day (2nd Monday in October)
        if (date.Month == 10 && isMonday && nthWeekDay == 2)
            return true;

        // Veteran’s Day (November 11, or preceding Friday/following Monday if weekend))
        if ((date.Month == 11 && date.Day == 10 && isFriday) || (date.Month == 11 && date.Day == 11 && !isWeekend) || (date.Month == 11 && date.Day == 12 && isMonday))
            return true;

        // Thanksgiving Day (4th Thursday in November)
        if (date.Month == 11 && isThursday && nthWeekDay == 4)
            return true;

        // Christmas Day (December 25, or preceding Friday/following Monday if weekend))
        if ((date.Month == 12 && date.Day == 24 && isFriday) || (date.Month == 12 && date.Day == 25 && !isWeekend) || (date.Month == 12 && date.Day == 26 && isMonday))
            return true;

        return false;
    }

    public static DateTime EasterSunday(int year)
    {
        int day = 0;
        int month = 0;

        int g = year % 19;
        int c = year / 100;
        int h = (c - (int)(c / 4) - (int)((8 * c + 13) / 25) + 19 * g + 15) % 30;
        int i = h - (int)(h / 28) * (1 - (int)(h / 28) * (int)(29 / (h + 1)) * (int)((21 - g) / 11));

        day = i - ((year + (int)(year / 4) + i + 2 - c + (int)(c / 4)) % 7) + 28;
        month = 3;

        if (day > 31)
        {
            month++;
            day -= 31;
        }

        return new DateTime(year, month, day);
    }
}
