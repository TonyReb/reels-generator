using System;
using System.IO;
using ReelsGenerator;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var configPath = args.Length > 0 ? args[0] : "config.yml";
if (!File.Exists(configPath))
{
    throw new FileNotFoundException($"Config file not found: {configPath}");
}

var yaml = File.ReadAllText(configPath);
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(UnderscoredNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var appConfig = deserializer.Deserialize<AppConfig>(yaml)
    ?? throw new InvalidOperationException("Failed to parse YAML config.");

if (appConfig.ReelGeneration.SymbolStacks.Low.Count == 0 || appConfig.ReelGeneration.SymbolStacks.High.Count == 0)
{
    throw new InvalidOperationException("YAML config must define non-empty low/high symbol stacks.");
}
if (appConfig.Simulation.SpinNumber <= 0)
{
    throw new InvalidOperationException("simulation.spin_number must be greater than 0.");
}
if (appConfig.SlotMachine.Window.Count == 0 || appConfig.SlotMachine.Lines.Count == 0 || appConfig.SlotMachine.Paytable.Count == 0)
{
    throw new InvalidOperationException("slot_machine.window, slot_machine.lines and slot_machine.paytable must be defined.");
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
    Maximize = appConfig.GeneticAlgorithm.Maximize,
    Seed = appConfig.GeneticAlgorithm.Seed
};

Console.WriteLine("Starting Genetic Algorithm for Slot Reel Generation...");
Console.WriteLine($"Config path: {configPath}");
Console.WriteLine($"Population size: {gaConfig.PopSize}");
Console.WriteLine($"Generations: {gaConfig.Generations}");
Console.WriteLine($"Crossover rate: {gaConfig.CrossoverRate}");
Console.WriteLine($"Mutation rate: {gaConfig.MutationRate}");
Console.WriteLine($"Elitism: {gaConfig.Elitism}");
Console.WriteLine($"Tournament K: {gaConfig.TournamentK}");
Console.WriteLine("-------------------------------------------");

var ga = new GeneticAlgorithm(
    gaConfig,
    appConfig.ReelGeneration.SymbolStacks.Low,
    appConfig.ReelGeneration.SymbolStacks.High,
    appConfig.Simulation.TargetRtp,
    appConfig.Simulation.TargetHitFrequency,
    appConfig.ReelGeneration.Radius,
    appConfig.ReelGeneration.Seed,
    appConfig.Simulation.SpinNumber,
    appConfig.SlotMachine);
var result = ga.Run();

Console.WriteLine("\n===== RESULTS =====");
Console.WriteLine($"Best fitness: {result.BestFitness.Item1}");
Console.WriteLine($"Best RTP: {result.BestFitness.Item2}");
Console.WriteLine($"Best Hit Frequency: {result.BestFitness.Item3}");

if (result.BestIndividual != null)
{
    Console.WriteLine("\nBest Individual Items:");
    foreach (var item in result.BestIndividual.Items)
    {
        foreach (var kvp in item)
        {
            Console.WriteLine($"Symbol {kvp.Key}: {string.Join(", ", kvp.Value)}");
        }
        Console.WriteLine();
    }

    Console.WriteLine("Best Individual Reels:");
    for (int i = 0; i < result.BestIndividual.Reels.Count; i++)
    {
        Console.WriteLine($"Reel {i + 1}: {string.Join(", ", result.BestIndividual.Reels[i])}");
    }
}

Console.WriteLine("\nAlgorithm completed successfully!");
