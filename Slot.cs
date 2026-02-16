using System;
using System.Collections.Generic;
using System.Linq;

namespace ReelsGenerator;

public class Slot
{
    private int[] windowSize;
    private List<List<int>> reels;
    private int[] reelsLen;
    private long cycle;
    private int[][] paytable;
    private HashSet<int> iconWild;
    private HashSet<int> iconScatter;
    private List<List<int>> linesBase;
    private List<List<int>> lines;

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

        cycle = 1;
        foreach (int len in reelsLen)
        {
            cycle *= len;
        }

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
        linesBase = config.Lines;
        lines = new List<List<int>>();
        GetFlattenLines();
    }

    private void GetFlattenLines()
    {
        lines.Clear();
        foreach (var line in linesBase)
        {
            var flattenLine = new List<int>();
            for (int i = 0; i < line.Count; i++)
            {
                flattenLine.Add(i * windowSize[i] + line[i]);
            }
            lines.Add(flattenLine);
        }
    }

    private List<int> GetIndex(long index)
    {
        List<int> result = new();

        long temp = index;
        for (int i = 0; i < reelsLen.Length; i++)
        {
            long divisor = 1;
            for (int j = i + 1; j < reelsLen.Length; j++)
            {
                divisor *= reelsLen[j];
            }
            result.Add((int)(temp / divisor));
            temp %= divisor;
        }

        return result;
    }

    private List<int> GetWindow(List<int> index)
    {
        List<int> window = new();
        for (int i = 0; i < index.Count; i++)
        {
            for (int offset = 0; offset < windowSize[i]; offset++)
            {
                int reelIndex = (index[i] + offset) % reelsLen[i];
                window.Add(reels[i][reelIndex]);
            }
        }
        return window;
    }

    private (int, int) CheckCombination(List<int> combination)
    {
        int maxLen = 0;
        int gameIcon = combination[0];

        for (int i = 1; i < combination.Count; i++)
        {
            int icon = combination[i];

            if (iconScatter.Contains(gameIcon))
            {
                if (!iconScatter.Contains(icon))
                {
                    break;
                }
            }

            if (iconWild.Contains(gameIcon) && !iconWild.Contains(icon) && !iconScatter.Contains(icon))
            {
                gameIcon = icon;
            }

            if (icon == gameIcon)
            {
                maxLen++;
            }
            else if (iconWild.Contains(icon))
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

    private (int, int, List<int>) GetWinningCombination(List<int> line, List<int> window)
    {
        List<int> combination = new();
        foreach (int index in line)
        {
            combination.Add(window[index]);
        }

        var (maxLen, gameIcon) = CheckCombination(combination);
        return (maxLen, gameIcon, combination);
    }

    public class WinningCombinationInfo
    {
        public int Line { get; set; }
        public int Length { get; set; }
        public int Icon { get; set; }
        public List<int> Combination { get; set; } = new();
    }

    public (int, List<WinningCombinationInfo>) GetWin(List<int> window)
    {
        int win = 0;
        List<WinningCombinationInfo> winningCombinations = new();

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var (maxLen, gameIcon, combination) = GetWinningCombination(line, window);

            int lineWin = 0;
            if (maxLen > 0 && gameIcon < paytable.Length && maxLen - 1 < paytable[gameIcon].Length)
            {
                lineWin = paytable[gameIcon][maxLen - 1];
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

    public class SpinResult
    {
        public long Index { get; set; }
        public Dictionary<string, int> Result { get; set; } = new();
        public List<WinningCombinationInfo> WinningCombinations { get; set; } = new();
    }

    public SpinResult Spin(long? index = null)
    {
        if (index == null)
        {
            index = Random.Shared.NextInt64(cycle);
        }

        var indexList = GetIndex(index.Value);
        var window = GetWindow(indexList);

        var (win, winningCombinations) = GetWin(window);

        var result = new SpinResult
        {
            Index = index.Value,
            WinningCombinations = winningCombinations
        };

        result.Result["base_game_win"] = win;

        return result;
    }
}
