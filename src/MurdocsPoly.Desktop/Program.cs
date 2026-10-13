namespace MurdocsPoly.Desktop;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Contains("--smoke")));
    }
}

/// <summary>Small formatting helpers shared by the windows.</summary>
static class Fmt
{
    static readonly System.Globalization.NumberFormatInfo Spaced = new() { NumberGroupSeparator = " ", NumberDecimalDigits = 0 };

    /// <summary>Money with spaces between thousands: 1 302 212 $.</summary>
    public static string Usd(decimal v) => v.ToString("N0", Spaced) + " $";

    public static string Cents(decimal price, decimal tick) => MurdocsPoly.Core.PriceGrid.Cents(price, tick);
}
