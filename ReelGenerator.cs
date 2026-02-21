using System;
using System.Collections.Generic;
using System.Linq;

namespace ReelsGenerator;

public class Mulberry32
{
    private const uint U32_MASK = 0xFFFFFFFF;
    private const uint GOLDEN_GAMMA = 0x9E3779B9;
    private const uint STEP = 0x6D2B79F5;

    private uint seed;

    public Mulberry32(int seedValue, int attempt = 0)
    {
        uint seedU = U32(seedValue);
        uint attemptU = U32(attempt);
        seed = U32(seedU + ImulU32(attemptU, GOLDEN_GAMMA));
    }

    private static uint U32(int x)
    {
        return ((uint)x) & U32_MASK;
    }

    private static uint U32(uint x)
    {
        return x & U32_MASK;
    }

    private static uint ImulU32(uint a, uint b)
    {
        return ((a & U32_MASK) * (b & U32_MASK)) & U32_MASK;
    }

    public float Rand()
    {
        seed = U32(seed + STEP);
        uint t = seed;
        t = ImulU32(t ^ (t >> 15), t | 1);
        t = U32(t ^ (t + ImulU32(t ^ (t >> 7), t | 61)));
        t = U32(t ^ (t >> 14));
        return t / 4294967296.0f;
    }
}

public class Stack
{
    public int Symbol { get; set; }
    public int Length { get; set; }

    public Stack() { }

    public Stack(int symbol, int length)
    {
        Symbol = symbol;
        Length = length;
    }

    public override bool Equals(object? obj)
    {
        if (obj is Stack other)
            return Symbol == other.Symbol && Length == other.Length;
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Symbol, Length);
    }
}

public class ReelGenerator
{
    private HashSet<int> specialSymbols;
    private HashSet<int> highSymbols;

    private Func<float>? rand;

    private List<Stack> specialStacks = new();
    private Dictionary<int, List<Stack>> highStacks = new();
    private Dictionary<int, List<Stack>> lowStacks = new();

    public ReelGenerator(IEnumerable<int>? configuredSpecialSymbols = null, IEnumerable<int>? configuredHighSymbols = null)
    {
        specialSymbols = configuredSpecialSymbols?.ToHashSet() ?? new HashSet<int> { 0, 1 };
        highSymbols = configuredHighSymbols?.ToHashSet() ?? new HashSet<int>();
    }

    public void BuildStacks(Dictionary<int, List<int>> data)
    {
        specialStacks.Clear();
        highStacks.Clear();
        lowStacks.Clear();

        foreach (var kvp in data)
        {
            int symbol = kvp.Key;
            List<int> counts = kvp.Value;

            List<Stack> bucket = specialSymbols.Contains(symbol)
                ? specialStacks
                : highSymbols.Contains(symbol)
                    ? (highStacks.ContainsKey(0) ? highStacks[0] : [])
                    : (lowStacks.ContainsKey(0) ? lowStacks[0] : []);

            if (specialSymbols.Contains(symbol))
            {
                for (int itemLen = 0; itemLen < counts.Count; itemLen++)
                {
                    int count = counts[itemLen];
                    if (count > 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            bucket.Add(new Stack(symbol, itemLen + 1));
                        }
                    }
                }
            }
            else
            {
                for (int itemLen = 0; itemLen < counts.Count; itemLen++)
                {
                    int count = counts[itemLen];
                    if (count > 0)
                    {
                        int len = itemLen + 1;
                        Dictionary<int, List<Stack>> targetBucket = highSymbols.Contains(symbol) ? highStacks : lowStacks;

                        if (!targetBucket.ContainsKey(len))
                        {
                            targetBucket[len] = new List<Stack>();
                        }

                        for (int i = 0; i < count; i++)
                        {
                            targetBucket[len].Add(new Stack(symbol, len));
                        }
                    }
                }
            }
        }
    }

    private static Stack PopSwap(List<Stack> arr, int idx)
    {
        int last = arr.Count - 1;
        Stack v = arr[idx];
        arr[idx] = arr[last];
        arr.RemoveAt(last);
        return v;
    }

    private Stack PopRandom(List<Stack> arr)
    {
        int idx = (int)(rand!() * arr.Count);
        return PopSwap(arr, idx);
    }

    private List<Stack> Dfs(List<Stack> sequence, int gap, bool prevWasHigh, bool isFirst)
    {
        List<(bool, int)> moves = new();

        for (int ln = 1; ln <= gap; ln++)
        {
            if (lowStacks.ContainsKey(ln) && lowStacks[ln].Count > 0)
            {
                moves.Add((false, ln));
            }

            if (!isFirst && !prevWasHigh && ln < gap && highStacks.ContainsKey(ln) && highStacks[ln].Count > 0)
            {
                moves.Add((true, ln));
            }
        }

        if (moves.Count > 0)
        {
            int moveIdx = (int)(rand!() * moves.Count);
            var (isHigh, itemLen) = moves[moveIdx];
            moves.RemoveAt(moveIdx);

            List<Stack> candidates = isHigh ? highStacks[itemLen] : lowStacks[itemLen];
            Stack item = PopRandom(candidates);
            sequence.Add(item);

            if (gap - itemLen > 0)
            {
                sequence = Dfs(sequence, gap - itemLen, isHigh, false);
            }
        }
        else
        {
            for (int i = 0; i < gap; i++)
            {
                sequence.Add(new Stack(-1, 1));
            }
        }

        return sequence;
    }

    private List<Stack> BuildGapSequence(int gap)
    {
        List<Stack> sequence = new();
        sequence = Dfs(sequence, gap, false, true);
        return sequence;
    }

    private List<Stack> BuildSuffix(bool mustStartLow)
    {
        List<Stack> sequence = new();
        List<Stack> highStacksList = new();

        foreach (var ln in highStacks.Keys.OrderBy(x => x))
        {
            foreach (var item in highStacks[ln])
            {
                highStacksList.Add(item);
            }
        }

        List<Stack> lowStacksList = new();
        foreach (var ln in lowStacks.Keys.OrderBy(x => x))
        {
            foreach (var item in lowStacks[ln])
            {
                lowStacksList.Add(item);
            }
        }

        if (mustStartLow && lowStacksList.Count > 0)
        {
            sequence.Add(PopRandom(lowStacksList));
        }

        bool lastWasHigh = sequence.Count > 0 && highSymbols.Contains(sequence[^1].Symbol);

        while (highStacksList.Count > 0 || lowStacksList.Count > 0)
        {
            if (lastWasHigh)
            {
                if (lowStacksList.Count > 0)
                {
                    sequence.Add(PopRandom(lowStacksList));
                }
                lastWasHigh = false;
                continue;
            }

            bool canHigh = highStacksList.Count > 0;
            bool canLow = lowStacksList.Count > 0;

            if (!canHigh)
            {
                if (canLow)
                    sequence.Add(PopRandom(lowStacksList));
                lastWasHigh = false;
            }
            else if (!canLow)
            {
                sequence.Add(PopRandom(highStacksList));
                lastWasHigh = true;
            }
            else
            {
                if (highStacksList.Count > lowStacksList.Count || rand!() < 0.5)
                {
                    sequence.Add(PopRandom(highStacksList));
                    lastWasHigh = true;
                }
                else
                {
                    sequence.Add(PopRandom(lowStacksList));
                    lastWasHigh = false;
                }
            }
        }

        return sequence;
    }

    public List<int>? Generate(Dictionary<int, List<int>> data, int radius, int seed, int maxAttempts = 50)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            rand = new Mulberry32(seed, attempt).Rand;
            BuildStacks(data);

            List<Stack> stackSequence = new();

            if (specialStacks.Count > 0)
            {
                while (specialStacks.Count > 0)
                {
                    stackSequence.Add(specialStacks[0]);
                    specialStacks.RemoveAt(0);

                    var gapSequence = BuildGapSequence(radius - 1);
                    stackSequence.AddRange(gapSequence);
                }

                var suffixSequence = BuildSuffix(true);
                stackSequence.AddRange(suffixSequence);
            }
            else
            {
                var suffixSequence = BuildSuffix(false);
                stackSequence.AddRange(suffixSequence);
            }

            List<int> result = new();
            foreach (var st in stackSequence)
            {
                for (int i = 0; i < st.Length; i++)
                {
                    result.Add(st.Symbol);
                }
            }

            if (!result.Contains(-1))
            {
                return result;
            }
        }

        return null;
    }
}
