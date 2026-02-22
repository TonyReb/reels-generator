using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ReelsGenerator;

internal static class WinningCombinationsTableFormatter
{
    public static IReadOnlyList<string> Format(
        IReadOnlyDictionary<(int Symbol, int Length), long> counts,
        IReadOnlyDictionary<(int Symbol, int Length), long> winSums,
        int spinNumber,
        int lineCount)
    {
        if (counts.Count == 0)
        {
            return ["No winning combinations found."];
        }

        var symbols = counts.Keys.Select(x => x.Symbol).Distinct().OrderBy(x => x).ToArray();
        var lengths = counts.Keys.Select(x => x.Length).Distinct().OrderBy(x => x).ToArray();
        var output = new List<string>();

        var frequencyRows = BuildTableRows(symbols, lengths, (symbol, length) =>
        {
            counts.TryGetValue((symbol, length), out long value);
            double denominator = spinNumber > 0 && lineCount > 0
                ? (double)spinNumber * lineCount
                : 0;
            double ratio = denominator > 0 ? value / denominator : 0;
            return ratio.ToString("0.000000", CultureInfo.CurrentCulture);
        });
        output.AddRange(RenderTable(frequencyRows));

        output.Add(string.Empty);
        output.Add("RTP contribution by symbol and length:");

        var rtpRows = BuildRtpTableRows(symbols, lengths, winSums, spinNumber);
        output.AddRange(RenderTable(rtpRows));

        return output;
    }

    private static List<string[]> BuildTableRows(
        int[] symbols,
        int[] lengths,
        Func<int, int, string> valueFormatter)
    {
        var rows = new List<string[]>();
        var header = new string[lengths.Length + 1];
        header[0] = "Symbol";
        for (int i = 0; i < lengths.Length; i++)
        {
            header[i + 1] = $"L{lengths[i]}";
        }
        rows.Add(header);

        foreach (int symbol in symbols)
        {
            var row = new string[lengths.Length + 1];
            row[0] = symbol.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < lengths.Length; i++)
            {
                row[i + 1] = valueFormatter(symbol, lengths[i]);
            }
            rows.Add(row);
        }

        return rows;
    }

    private static List<string[]> BuildRtpTableRows(
        int[] symbols,
        int[] lengths,
        IReadOnlyDictionary<(int Symbol, int Length), long> winSums,
        int spinNumber)
    {
        var rows = new List<string[]>();
        var header = new string[lengths.Length + 2];
        header[0] = "Symbol";
        for (int i = 0; i < lengths.Length; i++)
        {
            header[i + 1] = $"L{lengths[i]}";
        }
        header[^1] = "Total RTP";
        rows.Add(header);

        double denominator = spinNumber > 0 ? spinNumber : 0;
        foreach (int symbol in symbols)
        {
            var row = new string[lengths.Length + 2];
            row[0] = symbol.ToString(CultureInfo.InvariantCulture);

            double totalRtp = 0;
            for (int i = 0; i < lengths.Length; i++)
            {
                winSums.TryGetValue((symbol, lengths[i]), out long winSum);
                double rtpContribution = denominator > 0 ? winSum / denominator : 0;
                totalRtp += rtpContribution;
                row[i + 1] = rtpContribution.ToString("0.000000", CultureInfo.CurrentCulture);
            }

            row[^1] = totalRtp.ToString("0.000000", CultureInfo.CurrentCulture);
            rows.Add(row);
        }

        return rows;
    }

    private static IReadOnlyList<string> RenderTable(IReadOnlyList<string[]> rows)
    {
        var widths = new int[rows[0].Length];
        foreach (var row in rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        var output = new List<string>(rows.Count + 1)
        {
            RenderRow(rows[0], widths),
            RenderSeparator(widths)
        };
        for (int i = 1; i < rows.Count; i++)
        {
            output.Add(RenderRow(rows[i], widths));
        }

        return output;
    }

    private static string RenderRow(string[] row, int[] widths)
    {
        return string.Join(" | ", row.Select((cell, i) => cell.PadRight(widths[i])));
    }

    private static string RenderSeparator(int[] widths)
    {
        return string.Join("-+-", widths.Select(w => new string('-', w)));
    }
}
