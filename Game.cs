using System;
using System.Collections.Generic;

namespace ReelsGenerator;

public class Game
    : IGameEngine
{
    private readonly int spinNumber;
    private readonly ISlotEngine slot;
    private readonly Dictionary<(int Symbol, int Length), long> winningCombinationCounts = new();
    private readonly Dictionary<(int Symbol, int Length), long> winningCombinationWinSums = new();

    private double totalWin;
    private int countWin;
    private int bonusGameTriggerCount;

    private double rtp;
    private double hitFrequency;

    public Game(List<List<int>> reels, int configuredSpinNumber, SlotMachineConfig slotConfig, ISlotEngineFactory slotFactory)
    {
        spinNumber = configuredSpinNumber;
        slot = slotFactory.Create(reels, slotConfig);

        totalWin = 0;
        countWin = 0;
        bonusGameTriggerCount = 0;

        rtp = 0;
        hitFrequency = 0;
    }

    public void Run()
    {
        totalWin = 0;
        countWin = 0;
        bonusGameTriggerCount = 0;
        winningCombinationCounts.Clear();
        winningCombinationWinSums.Clear();

        for (int i = 0; i < spinNumber; i++)
        {
            var spinResult = slot.SpinBaseGameWin();
            int baseGameWinValue = spinResult.Win;

            foreach (var combination in spinResult.WinningCombinations)
            {
                var key = (combination.Symbol, combination.Length);
                winningCombinationCounts.TryGetValue(key, out long currentCount);
                winningCombinationCounts[key] = currentCount + 1;
                winningCombinationWinSums.TryGetValue(key, out long currentWinSum);
                winningCombinationWinSums[key] = currentWinSum + combination.Win;
            }

            totalWin += baseGameWinValue;
            if (spinResult.BonusGameTriggered)
            {
                bonusGameTriggerCount++;
            }

            if (baseGameWinValue > 0)
            {
                countWin++;
            }
        }

        rtp = totalWin / spinNumber;
        hitFrequency = (double)countWin / spinNumber;
    }

    public (double, double) GetStats()
    {
        return (rtp, hitFrequency);
    }

    public IReadOnlyDictionary<(int Symbol, int Length), long> GetWinningCombinationCounts()
    {
        return winningCombinationCounts;
    }

    public IReadOnlyDictionary<(int Symbol, int Length), long> GetWinningCombinationWinSums()
    {
        return winningCombinationWinSums;
    }

    public double GetBonusGameFrequency()
    {
        return spinNumber > 0 ? (double)bonusGameTriggerCount / spinNumber : 0;
    }
}
