using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;

namespace ReelsGenerator;

public sealed class SpinResult
{
    public int Win { get; set; }
    public bool BonusGameTriggered { get; set; }
    public List<WinningCombination> WinningCombinations { get; set; } = new();
}

public sealed class WinningCombination
{
    public int Symbol { get; set; }
    public int Length { get; set; }
    public int Win { get; set; }
}

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
            if (reels[i].Count <= 0)
            {
                throw new InvalidOperationException($"Reel {i + 1} must contain at least one symbol.");
            }
            reelsLen[i] = reels[i].Count;
        }

        divisors = new long[reelsLen.Length];
        cycle = 1;
        try
        {
            checked
            {
                foreach (int len in reelsLen)
                {
                    cycle *= len;
                }
            }
        }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException("Total reel cycle is too large to evaluate.", ex);
        }
        if (cycle <= 0)
        {
            throw new InvalidOperationException("Total reel cycle must be greater than zero.");
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
        var reelOffsets = new int[windowSize.Length];
        int offset = 0;
        for (int i = 0; i < windowSize.Length; i++)
        {
            reelOffsets[i] = offset;
            offset += windowSize[i];
        }

        for (int lineIndex = 0; lineIndex < linesBase.Count; lineIndex++)
        {
            var line = linesBase[lineIndex];
            if (line.Count != windowSize.Length)
            {
                throw new InvalidOperationException($"Line {lineIndex} must define {windowSize.Length} rows.");
            }

            var flattenLine = new int[line.Count];
            for (int i = 0; i < line.Count; i++)
            {
                if (line[i] < 0 || line[i] >= windowSize[i])
                {
                    throw new InvalidOperationException($"Line {lineIndex}, reel {i + 1} row index {line[i]} is out of range 0..{windowSize[i] - 1}.");
                }

                flattenLine[i] = reelOffsets[i] + line[i];
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
        int maxLen = 1;
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

    private SpinResult GetWin(int[] window)
    {
        var result = new SpinResult();
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var (maxLen, gameIcon) = CheckCombination(line, window);

            int lineWin = 0;
            bool isValidCombination =
                maxLen > 0 &&
                gameIcon >= 0 &&
                gameIcon < paytable.Length &&
                maxLen - 1 < paytable[gameIcon].Length;

            if (isValidCombination)
            {
                lineWin = paytable[gameIcon][maxLen - 1];
            }

            result.Win += lineWin;
            if (isValidCombination)
            {
                result.WinningCombinations.Add(new WinningCombination
                {
                    Symbol = gameIcon,
                    Length = maxLen,
                    Win = lineWin
                });
            }
        }

        return result;
    }

    private bool IsBonusGameTriggered(int[] window)
    {
        int offset = 0;
        for (int reelIndex = 0; reelIndex < windowSize.Length; reelIndex++)
        {
            bool hasScatterInReel = false;
            for (int row = 0; row < windowSize[reelIndex]; row++)
            {
                if (iconScatter.Contains(window[offset + row]))
                {
                    hasScatterInReel = true;
                    break;
                }
            }

            if (!hasScatterInReel)
            {
                return false;
            }

            offset += windowSize[reelIndex];
        }

        return true;
    }

    public SpinResult SpinBaseGameWin(long? index = null)
    {
        index ??= rng.NextInt64(cycle);
        FillIndexBuffer(index.Value);
        FillWindowBuffer();
        var result = GetWin(windowBuffer);
        result.BonusGameTriggered = IsBonusGameTriggered(windowBuffer);

        return result;
    }
}
