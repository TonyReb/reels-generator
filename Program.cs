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

if (appConfig.Simulation.SpinNumber <= 0)
{
    throw new InvalidOperationException("simulation.spin_number must be greater than 0.");
}
if (appConfig.SlotMachine.Window.Count == 0 || appConfig.SlotMachine.Lines.Count == 0 || appConfig.SlotMachine.Paytable.Count == 0)
{
    throw new InvalidOperationException("slot_machine.window, slot_machine.lines and slot_machine.paytable must be defined.");
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
    Maximize = appConfig.GeneticAlgorithm.Maximize,
    Seed = appConfig.GeneticAlgorithm.Seed,
    VerboseProgress = appConfig.GeneticAlgorithm.VerboseProgress
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
    appConfig.ReelGeneration.Reels,
    appConfig.Simulation.TargetRtp,
    appConfig.Simulation.TargetHitFrequency,
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
