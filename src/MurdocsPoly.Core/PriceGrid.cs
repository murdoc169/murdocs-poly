using System.Globalization;

namespace MurdocsPoly.Core;

/// <summary>
/// The price rows of a ladder. Unlike Betfair, the step is one number for the whole market (0.1, 0.01, 0.001 or 0.0001), and a price is
/// a probability from one tick to one minus a tick. Prices are shown in cents (0.535 -> "53.5") because that is how people read these markets.
/// </summary>
public static class PriceGrid
{
    public static decimal Snap(decimal price, decimal tick) => Math.Clamp(Math.Round(price / tick, MidpointRounding.AwayFromZero) * tick, tick, 1 - tick);

    public static bool OnTick(decimal price, decimal tick) => price >= tick && price <= 1 - tick && price % tick == 0;

    public static decimal Up(decimal price, decimal tick, int ticks = 1) => Snap(price + ticks * tick, tick);
    public static decimal Down(decimal price, decimal tick, int ticks = 1) => Snap(price - ticks * tick, tick);

    /// <summary>Rows from the top (highest price) down, centred on a price: <paramref name="above"/> rows above it and <paramref name="below"/> below.</summary>
    public static List<decimal> Rows(decimal centre, decimal tick, int above, int below)
    {
        var c = Snap(centre, tick);
        var rows = new List<decimal>();
        for (int i = above; i >= -below; i--)
        {
            var p = c + i * tick;
            if (p >= tick && p <= 1 - tick) rows.Add(p);
        }
        return rows;
    }

    public static string Cents(decimal price, decimal tick)
    {
        int decimals = tick >= 0.01m ? 0 : tick >= 0.001m ? 1 : 2;
        return (price * 100).ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture);
    }

    /// <summary>Decimal odds, for people used to an exchange: a 0.40 share is 2.5.</summary>
    public static decimal Odds(decimal price) => price <= 0 ? 0 : Math.Round(1 / price, 2);

    /// <summary>Buying shares at a price: they cost price x shares and pay one each if the outcome wins.</summary>
    public static (decimal Cost, decimal Profit) BuyOutcome(decimal price, decimal shares) => (Math.Round(price * shares, 4), Math.Round((1 - price) * shares, 4));
}
