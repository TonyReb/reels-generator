using System;
using System.Collections.Generic;
using System.Linq;

namespace ReelsGenerator;

public class Game
{
    private int spinNumber;
    private Slot slot;

    private double baseGameWin;
    private double freeGameWin;
    private double totalWin;
    private int countWin;
    private List<int> wins;

    private double rtp;
    private double hitFrequency;

    public Game(List<List<int>> reels, int configuredSpinNumber, SlotMachineConfig slotConfig)
    {
        spinNumber = configuredSpinNumber;
        slot = new Slot(reels, slotConfig);

        baseGameWin = 0;
        freeGameWin = 0;
        totalWin = 0;
        countWin = 0;
        wins = new List<int>();

        rtp = 0;
        hitFrequency = 0;
    }

    public void Run(bool collectWins = false)
    {
        if (collectWins)
        {
            wins.Clear();
        }
        baseGameWin = 0;
        freeGameWin = 0;
        totalWin = 0;
        countWin = 0;

        for (int i = 0; i < spinNumber; i++)
        {
            int baseGameWinValue = slot.SpinBaseGameWin();
            int freeGameWinValue = 0;

            int totalWinValue = baseGameWinValue + freeGameWinValue;

            baseGameWin += baseGameWinValue;
            freeGameWin += freeGameWinValue;
            totalWin += totalWinValue;
            if (collectWins)
            {
                wins.Add(totalWinValue);
            }

            if (totalWinValue > 0)
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

    public double GetRtp() => rtp;
    public double GetHitFrequency() => hitFrequency;
    public double GetTotalWin() => totalWin;
    public double GetBaseGameWin() => baseGameWin;
    public double GetFreeGameWin() => freeGameWin;
    public List<int> GetWins() => wins;
}
