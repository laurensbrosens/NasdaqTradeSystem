// See https://aka.ms/new-console-template for more information

using NasdaqTrader.Bot.Core;
using NasdaqTraderSystem.Core;
using NasdaqTraderSystem.Html;
using System.Diagnostics;
using System.Globalization;

var html = new HtmlGenerator();
CultureInfo.CurrentCulture = new CultureInfo("en-US");
var parameters = Environment.GetCommandLineArgs();

string dataFolder = "";
dataFolder = GetParameter("-d", $"Map met data voor koersen (default ../Data))",
    parameters);
if (string.IsNullOrWhiteSpace(dataFolder))
{
    dataFolder = "../Data";
}

int amountOfStock = 100;
string amountOfStocksAsText = "";
amountOfStocksAsText = GetParameter("-n", $"Aantal aandelen op beurs (default 100)",
    parameters);
if (!int.TryParse(amountOfStocksAsText, out amountOfStock))
{
    amountOfStock = 100;
}

bool runSilent = args.Any(b => b.Equals("-silent"));

int timeLimit = 10000;
string timeLimitAsText = "";
timeLimitAsText = GetParameter("-t", $"Tijdlimiet voor totale run (default 2500)",
    parameters);
if (!int.TryParse(timeLimitAsText, out timeLimit))
{
    timeLimit = 2500;
}

int startingCash = 1000;
string startingCashAsText = "";
startingCashAsText = GetParameter("-s", $"Start bedrag(default 1000)",
    parameters);
if (!int.TryParse(startingCashAsText, out startingCash))
{
    startingCash = 1000;
}

BotLoader botLoader = new BotLoader();

var botTypes = new Dictionary<string, Type>();
botLoader.DetermineBots(AppContext.BaseDirectory + "Bots", botTypes);

var stocksLoader = new StockLoader(dataFolder, amountOfStock);
var year = new Random().Next(2021, 2024);
TraderSystemSimulation traderSystemSimulation = new TraderSystemSimulation(
    botTypes.Values.Select(b => (ITraderBot)Activator.CreateInstance(b)).ToList(),
    startingCash, new DateOnly(year, 01, 01),
    new DateOnly(year, 12, 31), 5,
    stocksLoader);

Dictionary<ITraderBot, Task> playerTasks = new();
foreach (var player in traderSystemSimulation.Players)
{
    CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    var token = cancellationTokenSource.Token;
    var task = Task.Run(async () =>
    {
        while (await traderSystemSimulation.DoSimulationStep(player))
        {
            if (token.IsCancellationRequested)
            {
                traderSystemSimulation.DidNotFinished.Add(player);
                break;
            }
        }
        await cancellationTokenSource.CancelAsync();
    }, cancellationToken: token);

    await task;
    await cancellationTokenSource.CancelAsync();
    while (task is { IsCompleted: false, IsCanceled: false, IsFaulted: false })
    {
        await Task.Delay(100);
    }
}

Console.WriteLine("Generating html results");
html.GenerateFiles(Path.Combine(AppContext.BaseDirectory, "Results"), traderSystemSimulation);
Console.WriteLine("Done");

if (!runSilent)
{
    var p = new Process();
    p.StartInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Results", "index.html"))
    {
        UseShellExecute = true
    };
    p.Start();
}

string GetParameter(string parameter, string question, string[] arguments)
{
    int indexOf = Array.IndexOf(arguments, parameter);
    if (indexOf == -1)
    {
        Console.WriteLine(question);
        return Console.ReadLine();
    }

    return arguments[indexOf + 1];

    return "";
}