using System;
using System.IO;
using System.Linq;

namespace ReelsGenerator;

public sealed class AppRunOptions
{
    public string? ConfigPath { get; set; }
    public string? ProfileName { get; set; }
    public string? SlotFactoryId { get; set; }
    public string? GameFactoryId { get; set; }
}

public static class AppRunner
{
    public static string GetDefaultConfigPath()
    {
        string cwdConfigsRoot = Path.Combine(Directory.GetCurrentDirectory(), "configs");
        string baseConfigsRoot = Path.Combine(AppContext.BaseDirectory, "configs");
        string root = Directory.Exists(cwdConfigsRoot) ? cwdConfigsRoot : baseConfigsRoot;

        if (!Directory.Exists(root))
        {
            return Path.Combine(AppContext.BaseDirectory, "config.yml");
        }

        var configPath = Directory
            .GetFiles(root, "config.yml", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return configPath ?? Path.Combine(AppContext.BaseDirectory, "config.yml");
    }

    public static void Run(AppRunOptions? options)
    {
        options ??= new AppRunOptions();
        var configPath = string.IsNullOrWhiteSpace(options.ConfigPath)
            ? GetDefaultConfigPath()
            : options.ConfigPath!;

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Config file not found: {configPath}");
        }

        var root = ConfigLoader.LoadRoot(configPath);
        var effectiveProfile = root.Profiles.Count == 0
            ? null
            : options.ProfileName ?? ConfigLoader.GetDefaultProfileName(root);
        var appConfig = ConfigLoader.ResolveConfig(root, effectiveProfile);
        var slotFactory = EngineFactoryRegistry.GetSlotFactory(options.SlotFactoryId);
        var gameFactory = EngineFactoryRegistry.GetGameFactory(options.GameFactoryId);

        if (appConfig.Simulation.SpinNumber <= 0)
        {
            throw new InvalidOperationException("simulation.spin_number must be greater than 0.");
        }
        if (appConfig.GeneticAlgorithm.PopSize <= 0)
        {
            throw new InvalidOperationException("genetic_algorithm.pop_size must be greater than 0.");
        }
        if (appConfig.GeneticAlgorithm.Generations < 0)
        {
            throw new InvalidOperationException("genetic_algorithm.generations must be 0 or greater.");
        }
        if (appConfig.GeneticAlgorithm.Elitism < 0 || appConfig.GeneticAlgorithm.Elitism > appConfig.GeneticAlgorithm.PopSize)
        {
            throw new InvalidOperationException("genetic_algorithm.elitism must be between 0 and pop_size.");
        }
        if (appConfig.GeneticAlgorithm.TournamentK <= 0)
        {
            throw new InvalidOperationException("genetic_algorithm.tournament_k must be greater than 0.");
        }
        if (appConfig.GeneticAlgorithm.CrossoverRate is < 0 or > 1)
        {
            throw new InvalidOperationException("genetic_algorithm.crossover_rate must be in [0, 1].");
        }
        if (appConfig.GeneticAlgorithm.MutationRate is < 0 or > 1)
        {
            throw new InvalidOperationException("genetic_algorithm.mutation_rate must be in [0, 1].");
        }
        if (appConfig.GeneticAlgorithm.MutationSigma < 0)
        {
            throw new InvalidOperationException("genetic_algorithm.mutation_sigma must be >= 0.");
        }
        if (appConfig.Simulation.TargetHitFrequency is < 0 or > 1)
        {
            throw new InvalidOperationException("simulation.target_hit_frequency must be in [0, 1].");
        }
        if (appConfig.Simulation.TargetBonusGameFrequency is < 0 or > 1)
        {
            throw new InvalidOperationException("simulation.target_bonus_game_frequency must be in [0, 1].");
        }
        if (appConfig.Simulation.SymbolRtpUnevennessWeight < 0)
        {
            throw new InvalidOperationException("simulation.symbol_rtp_unevenness_weight must be >= 0.");
        }
        foreach (var kvp in appConfig.Simulation.SymbolRtpTargets)
        {
            if (kvp.Key < 0)
            {
                throw new InvalidOperationException($"simulation.symbol_rtp_targets contains invalid symbol id: {kvp.Key}.");
            }
            if (kvp.Value < 0)
            {
                throw new InvalidOperationException($"simulation.symbol_rtp_targets[{kvp.Key}] must be >= 0.");
            }
        }
        if (appConfig.SlotMachine.Window.Count == 0 || appConfig.SlotMachine.Lines.Count == 0 || appConfig.SlotMachine.Paytable.Count == 0)
        {
            throw new InvalidOperationException("slot_machine.window, slot_machine.lines and slot_machine.paytable must be defined.");
        }
        for (int reelIndex = 0; reelIndex < appConfig.SlotMachine.Window.Count; reelIndex++)
        {
            if (appConfig.SlotMachine.Window[reelIndex] <= 0)
            {
                throw new InvalidOperationException($"slot_machine.window[{reelIndex}] must be greater than 0.");
            }
        }
        for (int lineIndex = 0; lineIndex < appConfig.SlotMachine.Lines.Count; lineIndex++)
        {
            var line = appConfig.SlotMachine.Lines[lineIndex];
            if (line.Count != appConfig.SlotMachine.Window.Count)
            {
                throw new InvalidOperationException($"slot_machine.lines[{lineIndex}] must contain {appConfig.SlotMachine.Window.Count} positions.");
            }
            for (int reelIndex = 0; reelIndex < line.Count; reelIndex++)
            {
                int row = line[reelIndex];
                int windowSize = appConfig.SlotMachine.Window[reelIndex];
                if (row < 0 || row >= windowSize)
                {
                    throw new InvalidOperationException($"slot_machine.lines[{lineIndex}][{reelIndex}] = {row} is out of range 0..{windowSize - 1}.");
                }
            }
        }
        foreach (var kvp in appConfig.SlotMachine.Paytable)
        {
            if (kvp.Key < 0)
            {
                throw new InvalidOperationException($"slot_machine.paytable contains invalid symbol id: {kvp.Key}.");
            }
            if (kvp.Value.Count == 0)
            {
                throw new InvalidOperationException($"slot_machine.paytable[{kvp.Key}] must contain at least one payout.");
            }
        }
        foreach (var kvp in appConfig.Simulation.SymbolRtpTargets)
        {
            int symbol = kvp.Key;
            if (!appConfig.SlotMachine.Paytable.ContainsKey(symbol))
            {
                throw new InvalidOperationException($"simulation.symbol_rtp_targets[{symbol}] must exist in slot_machine.paytable.");
            }
        }
        if (appConfig.ReelGeneration.Reels.Count == 0)
        {
            throw new InvalidOperationException("reel_generation.reels must be defined.");
        }
        if (appConfig.ReelGeneration.Reels.Count != appConfig.SlotMachine.Window.Count)
        {
            throw new InvalidOperationException("reel_generation.reels count must match slot_machine.window length.");
        }
        for (int i = 0; i < appConfig.ReelGeneration.Reels.Count; i++)
        {
            var reel = appConfig.ReelGeneration.Reels[i];
            if (reel.Radius <= 0)
            {
                throw new InvalidOperationException($"reel_generation.reels[{i}].radius must be greater than 0.");
            }

            if (reel.SymbolStacks.Low.Count == 0 || reel.SymbolStacks.High.Count == 0)
            {
                throw new InvalidOperationException($"reel_generation.reels[{i}] must define non-empty symbol_stacks.low/high.");
            }
        }

        var gaConfig = new GAConfig
        {
            PopSize = appConfig.GeneticAlgorithm.PopSize,
            Generations = appConfig.GeneticAlgorithm.Generations,
            CrossoverRate = appConfig.GeneticAlgorithm.CrossoverRate,
            MutationRate = appConfig.GeneticAlgorithm.MutationRate,
            CrossoverAlpha = appConfig.GeneticAlgorithm.CrossoverAlpha,
            MutationSigma = appConfig.GeneticAlgorithm.MutationSigma,
            Elitism = appConfig.GeneticAlgorithm.Elitism,
            TournamentK = appConfig.GeneticAlgorithm.TournamentK,
            Seed = appConfig.GeneticAlgorithm.Seed,
            VerboseProgress = appConfig.GeneticAlgorithm.VerboseProgress,
            SymbolRtpUnevennessWeight = appConfig.Simulation.SymbolRtpUnevennessWeight
        };

        Console.WriteLine("Starting Genetic Algorithm for Slot Reel Generation...");
        Console.WriteLine($"Config path: {configPath}");
        Console.WriteLine($"Profile: {effectiveProfile ?? "(single)"}");
        Console.WriteLine($"Slot factory: {slotFactory.Id}");
        Console.WriteLine($"Game factory: {gameFactory.Id}");
        Console.WriteLine($"Population size: {gaConfig.PopSize}");
        Console.WriteLine($"Generations: {gaConfig.Generations}");
        Console.WriteLine($"Crossover rate: {gaConfig.CrossoverRate}");
        Console.WriteLine($"Mutation rate: {gaConfig.MutationRate}");
        Console.WriteLine($"Elitism: {gaConfig.Elitism}");
        Console.WriteLine($"Tournament K: {gaConfig.TournamentK}");
        Console.WriteLine("-------------------------------------------");

        var ga = new GeneticAlgorithm(
            gaConfig,
            appConfig.ReelGeneration.Reels,
            appConfig.Simulation.TargetRtp,
            appConfig.Simulation.TargetHitFrequency,
            appConfig.Simulation.TargetBonusGameFrequency,
            appConfig.Simulation.SymbolRtpTargets,
            appConfig.Simulation.SpinNumber,
            appConfig.SlotMachine,
            gameFactory,
            slotFactory);
        _ = ga.Run();

        Console.WriteLine("\nAlgorithm completed successfully!");
    }
}
