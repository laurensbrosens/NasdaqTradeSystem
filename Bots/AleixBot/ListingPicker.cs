using AleixBot.Models;
using NasdaqTrader.Bot.Core;
using System.Collections.ObjectModel;

namespace AleixBot;

public class ListingPicker
{
    public ReadOnlyCollection<IStockListing> Listings { get; }
    public int WindowSize { get; }
    public Collection<IntermediateListing> IntermediateListings { get; } = [];
    public List<WindowedSumPerListingPerDate> WindowedListingSumPerDate = [];

    private List<Period> _periods = [];

    public ListingPicker(ReadOnlyCollection<IStockListing> listings, int windowSize)
    {
        Listings = listings;
        WindowSize = windowSize;
        ConstructIntermediateDictionary();
    }

    public void ConstructIntermediateDictionary()
    {

        foreach (var listing in Listings)
        {
            for (int i = 0; i < listing.PricePoints.Length - 2; i++)
            {
                var date = listing.PricePoints[i].Date;
                var priceDifference = listing.PricePoints[i + 1].Price - listing.PricePoints[i].Price;
                IntermediateListings.Add(new IntermediateListing(listing.Name, date, priceDifference, listing.PricePoints[i].Price));
            }
        }

        var allDates = IntermediateListings.Select(il => il.Date);
        if (!allDates.Any()) return;

        DateOnly begindate = allDates.Min();
        DateOnly endDate = allDates.Max();

        var groupedIntermediateListings = IntermediateListings
            .GroupBy(il => il.Name)
            .ToDictionary(g => g.Key, g => g.ToList());

        DateOnly j = begindate;
        while (j.AddDays(WindowSize) < endDate)
        {
            _periods.Add(new Period(j, j.AddDays(WindowSize)));
            j = j.AddDays(WindowSize);
        }

        foreach (var (name, listings) in groupedIntermediateListings)
        {
            foreach (var period in _periods)
            {
                // Filter once per group and period
                var windowSum = listings
                    .Where(il => il.Date >= period.Start && il.Date <= period.End)
                    .Sum(s => s.PriceDifference);

                var priceOnStartDate = listings
                    .Where(il => il.Date == period.Start)
                    .Select(il => il.Price)
                    .FirstOrDefault();

                WindowedListingSumPerDate.Add(new WindowedSumPerListingPerDate(period.Start, name, windowSum, priceOnStartDate));
            }
        }
    }

    public IStockListing? GetXBestListingForDate(int number, DateOnly date, decimal cash)
    {
        var periodForDate = _periods.Where(p => p.Start <= date && p.End >= date).FirstOrDefault();

        if (periodForDate == null)
        {
            return null;
        }

        var bestListingName = WindowedListingSumPerDate
                                        .Where(wl => wl.Date == periodForDate.Start && wl.PriceOnStartDate < cash)
                                        .OrderByDescending(wl => wl.Sum)
                                        .Select(wl => wl.Name)
                                        .FirstOrDefault();

        return Listings.FirstOrDefault(l => l.Name.Equals(bestListingName));
    }
}