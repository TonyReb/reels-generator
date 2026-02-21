using System;
using System.Collections.Generic;
using System.Linq;

namespace ReelsGenerator;

public class Slot
{
    private int[] windowSize;
    private List<List<int>> reels;
    private int[] reelsLen;
    private long[] divisors;
    private int[] indexBuffer;
    private int[] windowBuffer;
    private long cycle;
    private int[][] paytable;
    private HashSet<int> iconWild;
    private HashSet<int> iconScatter;
    private bool[] isWildSymbol;
    private bool[] isScatterSymbol;
    private int[][] lines;
    private readonly Random rng;
    private static readonly List<WinningCombinationInfo> EmptyWinningCombinations = new();

    public Slot(List<List<int>> reelsData, SlotMachineConfig config)
    {
        if (config.Window.Count == 0 || config.Paytable.Count == 0 || config.Lines.Count == 0)
        {
            throw new InvalidOperationException("slot_machine section must define window, paytable and lines.");
        }
        if (config.Window.Count != reelsData.Count)
        {
            throw new InvalidOperationException("slot_machine.window size must match reel count.");
        }

        windowSize = config.Window.ToArray();
        reels = reelsData;
        reelsLen = new int[reels.Count];
        for (int i = 0; i < reels.Count; i++)
        {
            reelsLen[i] = reels[i].Count;
        }

        divisors = new long[reelsLen.Length];
        cycle = 1;
        foreach (int len in reelsLen)
        {
            cycle *= len;
        }
        for (int i = 0; i < reelsLen.Length; i++)
        {
            long divisor = 1;
            for (int j = i + 1; j < reelsLen.Length; j++)
            {
                divisor *= reelsLen[j];
            }
            divisors[i] = divisor;
        }

        indexBuffer = new int[reelsLen.Length];
        int windowCells = 0;
        for (int i = 0; i < windowSize.Length; i++)
        {
            windowCells += windowSize[i];
        }
        windowBuffer = new int[windowCells];

        int maxSymbol = config.Paytable.Keys.Max();
        paytable = new int[maxSymbol + 1][];
        for (int i = 0; i < paytable.Length; i++)
        {
            paytable[i] = Array.Empty<int>();
        }
        foreach (var kvp in config.Paytable)
        {
            paytable[kvp.Key] = kvp.Value.ToArray();
        }

        iconWild = config.IconWild.ToHashSet();
        iconScatter = config.IconScatter.ToHashSet();
        isWildSymbol = BuildFlags(iconWild, paytable.Length);
        isScatterSymbol = BuildFlags(iconScatter, paytable.Length);
        lines = BuildFlattenLines(config.Lines);
        rng = Random.Shared;
    }

    private static bool[] BuildFlags(HashSet<int> symbols, int size)
    {
        var flags = new bool[size];
        foreach (var symbol in symbols)
        {
            if (symbol >= 0 && symbol < size)
            {
                flags[symbol] = true;
            }
        }

        return flags;
    }

    private int[][] BuildFlattenLines(List<List<int>> linesBase)
    {
        var flattenLines = new int[linesBase.Count][];
        for (int lineIndex = 0; lineIndex < linesBase.Count; lineIndex++)
        {
            var line = linesBase[lineIndex];
            var flattenLine = new int[line.Count];
            for (int i = 0; i < line.Count; i++)
            {
                flattenLine[i] = i * windowSize[i] + line[i];
            }
            flattenLines[lineIndex] = flattenLine;
        }

        return flattenLines;
    }

    private void FillIndexBuffer(long index)
    {
        long temp = index;
        for (int i = 0; i < reelsLen.Length; i++)
        {
            indexBuffer[i] = (int)(temp / divisors[i]);
            temp %= divisors[i];
        }
    }

    private void FillWindowBuffer()
    {
        int outputIndex = 0;
        for (int i = 0; i < indexBuffer.Length; i++)
        {
            for (int offset = 0; offset < windowSize[i]; offset++)
            {
                int reelIndex = (indexBuffer[i] + offset) % reelsLen[i];
                windowBuffer[outputIndex++] = reels[i][reelIndex];
            }
        }
    }

    private (int, int) CheckCombination(int[] line, int[] window)
    {
        int maxLen = 0;
        int gameIcon = window[line[0]];

        for (int i = 1; i < line.Length; i++)
        {
            int icon = window[line[i]];

            bool gameIconIsScatter = gameIcon >= 0 && gameIcon < isScatterSymbol.Length && isScatterSymbol[gameIcon];
            bool gameIconIsWild = gameIcon >= 0 && gameIcon < isWildSymbol.Length && isWildSymbol[gameIcon];
            bool iconIsScatter = icon >= 0 && icon < isScatterSymbol.Length && isScatterSymbol[icon];
            bool iconIsWild = icon >= 0 && icon < isWildSymbol.Length && isWildSymbol[icon];

            if (gameIconIsScatter)
            {
                if (!iconIsScatter)
                {
                    break;
                }
            }

            if (gameIconIsWild && !iconIsWild && !iconIsScatter)
            {
                gameIcon = icon;
            }

            if (icon == gameIcon)
            {
                maxLen++;
            }
            else if (iconIsWild)
            {
                maxLen++;
            }
            else
            {
                break;
            }
        }

        return (maxLen, gameIcon);
    }

    public class WinningCombinationInfo
    {
        public int Line { get; set; }
        public int Length { get; set; }
        public int Icon { get; set; }
        public List<int> Combination { get; set; } = new();
    }

    public (int, List<WinningCombinationInfo>) GetWin(int[] window, bool includeWinningCombinations)
    {
        if (!includeWinningCombinations)
        {
            int fastWin = 0;
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var (maxLen, gameIcon) = CheckCombination(line, window);
                if (maxLen > 0 && gameIcon < paytable.Length && maxLen - 1 < paytable[gameIcon].Length)
                {
                    fastWin += paytable[gameIcon][maxLen - 1];
                }
            }

            return (fastWin, EmptyWinningCombinations);
        }

        int win = 0;
        var winningCombinations = new List<WinningCombinationInfo>(lines.Length);

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var (maxLen, gameIcon) = CheckCombination(line, window);

            int lineWin = 0;
            if (maxLen > 0 && gameIcon < paytable.Length && maxLen - 1 < paytable[gameIcon].Length)
            {
                lineWin = paytable[gameIcon][maxLen - 1];
            }

            var combination = new List<int>(line.Length);
            for (int i = 0; i < line.Length; i++)
            {
                combination.Add(window[line[i]]);
            }

            winningCombinations.Add(new WinningCombinationInfo
            {
                Line = lineIndex,
                Length = maxLen + 1,
                Icon = gameIcon,
                Combination = combination
            });

            win += lineWin;
        }

        return (win, winningCombinations);
    }

    public int SpinBaseGameWin(long? index = null)
    {
        if (index == null)
        {
            index = rng.NextInt64(cycle);
        }

        FillIndexBuffer(index.Value);
        FillWindowBuffer();
        var (win, _) = GetWin(windowBuffer, includeWinningCombinations: false);
        return win;
    }

    public class SpinResult
    {
        public long Index { get; set; }
        public Dictionary<string, int> Result { get; set; } = new();
        public List<WinningCombinationInfo> WinningCombinations { get; set; } = new();
    }

    public SpinResult Spin(long? index = null, bool includeWinningCombinations = true)
    {
        if (index == null)
        {
            index = rng.NextInt64(cycle);
        }

        FillIndexBuffer(index.Value);
        FillWindowBuffer();
        var (win, winningCombinations) = GetWin(windowBuffer, includeWinningCombinations);

        var result = new SpinResult
        {
            Index = index.Value,
            WinningCombinations = winningCombinations
        };

        result.Result["base_game_win"] = win;

        return result;
    }
}
