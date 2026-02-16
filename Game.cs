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

    public Game(List<List<int>> reels)
    {
        spinNumber = 1000000;
        slot = new Slot(reels);

        baseGameWin = 0;
        freeGameWin = 0;
        totalWin = 0;
        countWin = 0;
        wins = new List<int>();

        rtp = 0;
        hitFrequency = 0;
    }

    public void Run()
    {
        wins.Clear();
        baseGameWin = 0;
        freeGameWin = 0;
        totalWin = 0;
        countWin = 0;

        for (int i = 0; i < spinNumber; i++)
        {
            var spinResult = slot.Spin(null);

            int baseGameWinValue = 0;
            if (spinResult.Result.ContainsKey("base_game_win"))
            {
                baseGameWinValue = spinResult.Result["base_game_win"];
            }

            int freeGameWinValue = 0;
            if (spinResult.Result.ContainsKey("free_game_win"))
            {
                freeGameWinValue = spinResult.Result["free_game_win"];
            }

            int totalWinValue = baseGameWinValue + freeGameWinValue;

            baseGameWin += baseGameWinValue;
            freeGameWin += freeGameWinValue;
            totalWin += totalWinValue;
            wins.Add(totalWinValue);

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
