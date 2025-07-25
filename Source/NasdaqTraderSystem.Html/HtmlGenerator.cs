using HandlebarsDotNet;
using NasdaqTrader.Bot.Core;
using NasdaqTraderSystem.Core;
using ScottPlot;
using ScottPlot.Plottables;
using System.Reflection;

namespace NasdaqTraderSystem.Html;

public class HtmlGenerator
{
    private readonly HandlebarsTemplate<object, object> _stockTemplate;
    private readonly HandlebarsTemplate<object, object> _playerTemplate;

    public HtmlGenerator()
    {
        Handlebars.Configuration.FormatterProviders.Add(new DecimalFormatter());
        Handlebars.Configuration.FormatterProviders.Add(new CustomDateTimeFormatter("dd-MM-yyyy"));
        Handlebars.Configuration.FormatterProviders.Add(new TimeSpanFormatter("s\\.ffff"));

        var indexTemplate = GetTemplate("StockTemplate.html");

        _stockTemplate = Handlebars.Compile(indexTemplate);

        indexTemplate = GetTemplate("PlayerResult.html");
        _playerTemplate = Handlebars.Compile(indexTemplate);
    }

    public string GetGameHtml(SimulationResults results)
    {
        var indexTemplate = GetTemplate("GameResult.html");

        var template = Handlebars.Compile(indexTemplate);
        var result = template(results);
        return result;
    }

    private string GetTemplate(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"NasdaqTraderSystem.Html.Templates.{name}";

        using (Stream stream = assembly.GetManifestResourceStream(resourceName))
        using (StreamReader reader = new StreamReader(stream))
        {
            return reader.ReadToEnd();
        }
    }

    public void GenerateFiles(string baseDirectory, TraderSystemSimulation traderSystemSimulation)
    {
        var gameDate = DateTime.Now;

        SimulationResults results = new SimulationResults();
        results.GameName = gameDate.ToString("dd-MM-yyyy HH:mm");
        results.StartDate = traderSystemSimulation.StartDate;
        results.EndDate = traderSystemSimulation.EndDate;
        results.RunAt = gameDate.ToString("dd-MM-yyyy HH:mm");
        results.Listings = traderSystemSimulation.StockListings.ToArray();
        results.Trades = traderSystemSimulation.Trades.ToDictionary();

        var tasks = GenerateStockPages(baseDirectory, traderSystemSimulation, gameDate, results);
        results.Companies = traderSystemSimulation.Players.Select(c =>
            new CompanyResult()
            {
                Name = c.CompanyName,
                Cash = traderSystemSimulation.BankAccounts[c],
                HoldingsValue = CalculateHoldingsValue(traderSystemSimulation, c, results),
                Total = (CalculateHoldingsValue(traderSystemSimulation, c, results) +
                         traderSystemSimulation.BankAccounts[c]),
                RunDuration = traderSystemSimulation.Durations[c].Elapsed,
            }).OrderByDescending(c => c.Total).ToArray();

        tasks.AddRange(GenerateCompaniesPlot(baseDirectory, traderSystemSimulation, gameDate));
        Directory.CreateDirectory(Path.Combine(baseDirectory, $"{gameDate:dd-MM-yyyy-HH-mm}"));
        File.WriteAllText(Path.Combine(baseDirectory, $"{gameDate:dd-MM-yyyy-HH-mm}", "GameResult.html"), GetGameHtml(results));

        var files = Directory.GetDirectories(baseDirectory).Reverse().Where(b => !b.Contains(".git")).ToArray();
        GenerateIndex(baseDirectory, files);

        foreach (var task in tasks)
        {
            task.Wait();
        }
    }

    private static decimal CalculateHoldingsValue(TraderSystemSimulation traderSystemSimulation, ITraderBot c,
        SimulationResults results)
    {
        return traderSystemSimulation.Holdings[c].Sum(c =>
            c.Amount * c.Listing.PricePoints.FirstOrDefault(c => c.Date == results.EndDate)?.Price ??
            c.Listing.PricePoints.MinBy(c =>
                c.Date.ToDateTime(new TimeOnly(12, 0)) - results.EndDate.ToDateTime(new TimeOnly(12, 0))).Price);
    }

    private void GenerateIndex(string baseDirectory, string[] files)
    {
        var context = new IndexContext();
        context.Games = files.Select(b => new IndexGame()
        {
            GameHTML = Path.Combine(Path.GetFileNameWithoutExtension(b), "GameResult.html"),
            Name = Path.GetFileNameWithoutExtension(b),
            ExecutionTime = DateTime.ParseExact(Path.GetFileNameWithoutExtension(b), "dd-MM-yyyy-HH-mm", null)
        }).OrderByDescending(g => g.ExecutionTime).ToArray();
        var indexTemplate = GetTemplate("Index.html");

        var template = Handlebars.Compile(indexTemplate);

        var result = template(context);
        File.WriteAllText(Path.Combine(baseDirectory, "index.html"), result);
    }

    private List<Task> GenerateStockPages(string baseDirectory, TraderSystemSimulation traderSystemSimulation,
        DateTime gameDate, SimulationResults results)
    {
        List<Task> tasks = new List<Task>();
        Directory.CreateDirectory(Path.Combine(baseDirectory, $"{gameDate:dd-MM-yyyy-HH-mm}", "stocks"));
        foreach (var listing in results.Listings)
        {
            tasks.Add(Task.Run(() =>
            {
                Plot listingsPlot = new();
                var scatter = listingsPlot.Add.Scatter(
                    listing.PricePoints.Select(c => c.Date.ToDateTime(new TimeOnly(12, 0))
                    ).ToArray(),
                    listing.PricePoints.Select(c => c.Price).ToArray());
                scatter.LegendText = listing.Name;
                listingsPlot.Axes.DateTimeTicksBottom();
                listingsPlot.SavePng(Path.Combine(baseDirectory, $"{gameDate:dd-MM-yyyy-HH-mm}", "stocks", $"{listing.Ticker}.png"), 1920,
                    1080);

                Directory.CreateDirectory(Path.Combine(baseDirectory, $"{gameDate:dd-MM-yyyy-HH-mm}"));
                File.WriteAllText(Path.Combine(baseDirectory, $"{gameDate:dd-MM-yyyy-HH-mm}", "stocks", $"{listing.Ticker}.html"),
                    GetStockHtml(listing, results));
            }));
        }

        return tasks;
    }

    private string? GetStockHtml(IStockListing listing, SimulationResults results)
    {
        TickerTemplateContext context = new();
        context.Listing = listing;
        context.TradesForPlayers = results.Trades.Where(b => b.Value
            .Any(c => c.Listing.Ticker == listing.Ticker)).Select(b =>
            new TradesForPlayer
            {
                CompanyName = b.Key.CompanyName,
                Trades = b.Value.Where(c => c.Listing.Ticker == listing.Ticker).ToArray()
            }
        ).ToArray();
        context.Trades = results.Trades.SelectMany(c => c.Value.Select(d => (c.Key.CompanyName, d)))
            .Where(c => c.d.Listing.Ticker == listing.Ticker).Select(c => new TradeForPlayer()
            {
                Trade = c.d,
                Player = c.CompanyName
            }).OrderBy(c => c.Trade.ExecutedOn).ToArray();

        var result = _stockTemplate(context);
        return result;
    }

    private List<Task> GenerateCompaniesPlot(string baseDirectory, TraderSystemSimulation traderSystemSimulation,
        DateTime gameDate)
    {
        List<Task> tasks = new List<Task>();
        Plot companiesPlot = new();
        companiesPlot.Title("Companies");
        foreach (var player in traderSystemSimulation.Players)
        {
            var recordsOfPlayer = traderSystemSimulation.Records.Where(b => b.Name == player.CompanyName).ToList();
            var playerScatter = GetTotalWorthScatter(companiesPlot, recordsOfPlayer);
            playerScatter.LegendText = player.CompanyName + " - Total worth";

            var playerCashScatter = CashScatter(companiesPlot, recordsOfPlayer);
            playerCashScatter.LegendText = player.CompanyName + " - Cash";
            var playerHoldingScatter = HoldingScatter(companiesPlot, recordsOfPlayer);
            playerHoldingScatter.LegendText = player.CompanyName + " - Holdings";

            tasks.Add(Task.Run(() =>
            {
                Plot playerPlot = new();

                var playerPlotScatter = GetTotalWorthScatter(playerPlot, recordsOfPlayer);
                playerPlotScatter.LegendText = "Total worth";

                var playerPlotCashScatter = CashScatter(playerPlot, recordsOfPlayer);
                playerPlotCashScatter.LegendText = "Cash";

                var playerPlotHoldingScatter = HoldingScatter(playerPlot, recordsOfPlayer);
                playerPlotHoldingScatter.LegendText = "Holdings";

                playerPlot.Axes.DateTimeTicksBottom();
                playerPlot.SavePng(Path.Combine(baseDirectory, $"{gameDate:dd-MM-yyyy-HH-mm}", $"{player.CompanyName}.png"), 1920,
                    1080);

                string html = GenerateHtmlForPlayer(player, baseDirectory, traderSystemSimulation,
                    gameDate);
                File.WriteAllText(Path.Combine(baseDirectory, $"{gameDate:dd-MM-yyyy-HH-mm}", $"{player.CompanyName}.html"), html);
            }));
        }

        companiesPlot.Axes.DateTimeTicksBottom();
        companiesPlot.SavePng(Path.Combine(baseDirectory, $"{gameDate:dd-MM-yyyy-HH-mm}", "results.png"), 1920,
            1080);
        return tasks;
    }

    private static Scatter HoldingScatter(Plot plot, List<HistoricCompanyRecord> recordsOfPlayer)
    {
        var playerHoldingScatter = plot.Add.Scatter(
            recordsOfPlayer.Select(b => b.OnDate.ToDateTime(new TimeOnly(12, 0))).ToArray(),
            recordsOfPlayer.Select(b => b.TotalHolding).ToArray()
        );
        return playerHoldingScatter;
    }

    private static Scatter CashScatter(Plot plot, List<HistoricCompanyRecord> recordsOfPlayer)
    {
        var playerCashScatter = plot.Add.Scatter(
            recordsOfPlayer.Select(b => b.OnDate.ToDateTime(new TimeOnly(12, 0))).ToArray(),
            recordsOfPlayer.Select(b => b.Cash).ToArray()
        );
        return playerCashScatter;
    }

    private static Scatter GetTotalWorthScatter(Plot plot, List<HistoricCompanyRecord> recordsOfPlayer)
    {
        var playerScatter = plot.Add.Scatter(
            recordsOfPlayer.Select(b => b.OnDate.ToDateTime(new TimeOnly(12, 0))).ToArray(),
            recordsOfPlayer.Select(b => b.TotalWorth).ToArray()
        );
        return playerScatter;
    }

    private string GenerateHtmlForPlayer(ITraderBot player, string baseDirectory,
        TraderSystemSimulation traderSystemSimulation, DateTime gameDate)
    {
        PlayerTemplateContext context = new();
        context.CompanyName = player.CompanyName;
        context.TradesForPlayer = traderSystemSimulation.Trades[player].ToArray();
        var result = _playerTemplate(context);
        return result;
    }
}

internal class IndexGame
{
    public string GameHTML { get; set; }
    public string Name { get; set; }
    public DateTime ExecutionTime { get; set; }
}

internal class IndexContext
{
    public IndexGame[] Games { get; set; }
}

internal class PlayerTemplateContext
{
    public string CompanyName { get; set; }
    public ITrade[] TradesForPlayer { get; set; }
}

internal class TradesForPlayer
{
    public string CompanyName { get; set; }
    public ITrade[] Trades { get; set; }
}

internal class TickerTemplateContext
{
    public IStockListing Listing { get; set; }
    public TradesForPlayer[] TradesForPlayers { get; set; }
    public TradeForPlayer[] Trades { get; set; }
}

public class TradeForPlayer
{
    public string Player { get; set; }
    public ITrade Trade { get; set; }
}