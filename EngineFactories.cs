using System;
using System.Collections.Generic;

namespace ReelsGenerator;

public interface ISlotEngine
{
    SpinResult SpinBaseGameWin(long? index = null);
}

public interface ISlotEngineFactory
{
    string Id { get; }
    ISlotEngine Create(List<List<int>> reels, SlotMachineConfig config);
}

public interface IGameEngine
{
    void Run();
    (double, double) GetStats();
    IReadOnlyDictionary<(int Symbol, int Length), long> GetWinningCombinationCounts();
    IReadOnlyDictionary<(int Symbol, int Length), long> GetWinningCombinationWinSums();
    double GetBonusGameFrequency();
}

public interface IGameEngineFactory
{
    string Id { get; }
    IGameEngine Create(List<List<int>> reels, int spinNumber, SlotMachineConfig slotConfig, ISlotEngineFactory slotFactory);
}

public static class EngineFactoryRegistry
{
    private static readonly Dictionary<string, ISlotEngineFactory> SlotFactories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["classic"] = new ClassicSlotEngineFactory()
    };

    private static readonly Dictionary<string, IGameEngineFactory> GameFactories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["classic"] = new ClassicGameEngineFactory()
    };

    public static ISlotEngineFactory GetSlotFactory(string? id)
    {
        string key = string.IsNullOrWhiteSpace(id) ? "classic" : id;
        if (!SlotFactories.TryGetValue(key, out var factory))
        {
            throw new InvalidOperationException($"Unknown slot factory: {id}");
        }

        return factory;
    }

    public static IGameEngineFactory GetGameFactory(string? id)
    {
        string key = string.IsNullOrWhiteSpace(id) ? "classic" : id;
        if (!GameFactories.TryGetValue(key, out var factory))
        {
            throw new InvalidOperationException($"Unknown game factory: {id}");
        }

        return factory;
    }
}

public sealed class ClassicSlotEngineFactory : ISlotEngineFactory
{
    public string Id => "classic";

    public ISlotEngine Create(List<List<int>> reels, SlotMachineConfig config)
    {
        return new ClassicSlotEngine(new Slot(reels, config));
    }
}

public sealed class ClassicGameEngineFactory : IGameEngineFactory
{
    public string Id => "classic";

    public IGameEngine Create(List<List<int>> reels, int spinNumber, SlotMachineConfig slotConfig, ISlotEngineFactory slotFactory)
    {
        return new Game(reels, spinNumber, slotConfig, slotFactory);
    }
}

public sealed class ClassicSlotEngine : ISlotEngine
{
    private readonly Slot slot;

    public ClassicSlotEngine(Slot slot)
    {
        this.slot = slot;
    }

    public SpinResult SpinBaseGameWin(long? index = null)
    {
        return slot.SpinBaseGameWin(index);
    }
}
