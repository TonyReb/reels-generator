using System;
using System.Collections.Generic;
using ReelsGenerator;

// Direct configuration - no YAML parsing needed
var gaConfig = new GAConfig
{
    PopSize = 20,
    Generations = 10,
    CrossoverRate = 0.9f,
    MutationRate = 0.05f,
    Elitism = 2,
    TournamentK = 3,
    Maximize = false,
    Seed = 42
};

// Symbol stacks
var lowStacks = new Dictionary<int, List<int>>
{
    {0, new List<int> {1, 0, 0}},
    {1, new List<int> {1, 1, 1}},
    {2, new List<int> {1, 1, 1}},
    {3, new List<int> {1, 1, 1}},
    {4, new List<int> {1, 1, 1}},
    {5, new List<int> {1, 1, 1}},
    {6, new List<int> {1, 1, 1}},
    {7, new List<int> {1, 1, 1}},
    {8, new List<int> {1, 1, 1}},
    {9, new List<int> {1, 1, 1}},
};

var highStacks = new Dictionary<int, List<int>>
{
    {0, new List<int> {1, 0, 0}},
    {1, new List<int> {5, 5, 5}},
    {2, new List<int> {1, 1, 1}},
    {3, new List<int> {2, 2, 2}},
    {4, new List<int> {3, 3, 3}},
    {5, new List<int> {4, 4, 4}},
    {6, new List<int> {10, 10, 10}},
    {7, new List<int> {10, 10, 10}},
    {8, new List<int> {10, 10, 10}},
    {9, new List<int> {20, 20, 20}},
};

// Simulation parameters
double targetRtp = 0.45;
double targetHitFrequency = 0.15;

// Reel generation parameters
int reelRadius = 3;
int reelSeed = 1;

// Run genetic algorithm
Console.WriteLine("Starting Genetic Algorithm for Slot Reel Generation...");
Console.WriteLine($"Population size: {gaConfig.PopSize}");
Console.WriteLine($"Generations: {gaConfig.Generations}");
Console.WriteLine($"Crossover rate: {gaConfig.CrossoverRate}");
Console.WriteLine($"Mutation rate: {gaConfig.MutationRate}");
Console.WriteLine($"Elitism: {gaConfig.Elitism}");
Console.WriteLine($"Tournament K: {gaConfig.TournamentK}");
Console.WriteLine("-------------------------------------------");

var ga = new GeneticAlgorithm(gaConfig, lowStacks, highStacks, targetRtp, targetHitFrequency, reelRadius, reelSeed);
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
