using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ReelsGenerator;

public class GAConfig
{
    public int PopSize { get; set; } = 5;
    public int Generations { get; set; } = 3;
    public float CrossoverRate { get; set; } = 0.9f;
    public float MutationRate { get; set; } = 0.02f;
    public int Elitism { get; set; } = 2;
    public int TournamentK { get; set; } = 3;
    public bool Maximize { get; set; } = true;
    public int Seed { get; set; } = 123;
    public float CrossoverAlpha { get; set; } = 0.5f;
    public double MutationSigma { get; set; } = 3.0;
    public bool VerboseProgress { get; set; } = false;
}

public class GAResult
{
    public Individual? BestIndividual { get; set; }
    public (double, double, double) BestFitness { get; set; }
    public List<double> HistoryBest { get; set; } = new();
}

public class Individual
{
    public List<Dictionary<int, List<int>>> Items { get; set; } = new();
    public List<List<int>> Reels { get; set; } = new();
}

public class GeneticAlgorithm
{
    private List<Dictionary<int, List<int>>> LOWS;
    private List<Dictionary<int, List<int>>> HIGHS;
    private List<int[]> SYMBOLS_BY_REEL;
    private List<int> REEL_RADII;
    private List<int> REEL_SEEDS;
    private List<ReelGenerator> REEL_GENERATORS;
    private int REEL_COUNT;

    private double MUT_SIGMA;
    private float CROSSOVER_ALPHA;
    private double TARGET_RTP;
    private double TARGET_HIT_FREQUENCY;
    private int SPIN_NUMBER;
    private SlotMachineConfig SLOT_CONFIG;

    private GAConfig config;
    private Random rng;

    public GeneticAlgorithm(
        GAConfig gaConfig,
        List<ReelConfig> reelConfigs,
        double targetRtp,
        double targetHitFrequency,
        int spinNumber,
        SlotMachineConfig slotConfig)
    {
        config = gaConfig;
        rng = new Random(config.Seed);

        LOWS = new List<Dictionary<int, List<int>>>();
        HIGHS = new List<Dictionary<int, List<int>>>();
        SYMBOLS_BY_REEL = new List<int[]>();
        REEL_RADII = new List<int>();
        REEL_SEEDS = new List<int>();
        REEL_GENERATORS = new List<ReelGenerator>();
        var specialSymbols = slotConfig.IconWild.Concat(slotConfig.IconScatter);

        for (int reelIndex = 0; reelIndex < reelConfigs.Count; reelIndex++)
        {
            var reelConfig = reelConfigs[reelIndex];
            var low = reelConfig.SymbolStacks.Low;
            var high = reelConfig.SymbolStacks.High;

            var symbols = low.Keys.Union(high.Keys).OrderBy(x => x).ToArray();
            foreach (var symbol in symbols)
            {
                if (!low.ContainsKey(symbol) || !high.ContainsKey(symbol))
                {
                    throw new InvalidOperationException($"Reel {reelIndex}: symbol {symbol} must exist in both low and high stacks.");
                }

                if (low[symbol].Count != high[symbol].Count)
                {
                    throw new InvalidOperationException($"Reel {reelIndex}: symbol {symbol} low/high stack lengths must match.");
                }
            }

            LOWS.Add(low);
            HIGHS.Add(high);
            SYMBOLS_BY_REEL.Add(symbols);
            REEL_RADII.Add(reelConfig.Radius);
            REEL_SEEDS.Add(reelConfig.Seed);
            REEL_GENERATORS.Add(new ReelGenerator(specialSymbols));
        }

        REEL_COUNT = reelConfigs.Count;
        TARGET_RTP = targetRtp;
        TARGET_HIT_FREQUENCY = targetHitFrequency;
        SPIN_NUMBER = spinNumber;
        SLOT_CONFIG = slotConfig;

        MUT_SIGMA = config.MutationSigma;
        CROSSOVER_ALPHA = config.CrossoverAlpha;
    }

    private (double, double, double) FitnessSphere(Individual ind)
    {
        Game game = new(ind.Reels, SPIN_NUMBER, SLOT_CONFIG);
        game.Run();
        var (rtp, hitFrequency) = game.GetStats();

        double fitness = Math.Abs(TARGET_RTP - rtp) / (Math.Abs(TARGET_RTP) + Math.Abs(rtp)) +
                         Math.Abs(TARGET_HIT_FREQUENCY - hitFrequency) / (Math.Abs(TARGET_HIT_FREQUENCY) + Math.Abs(hitFrequency));

        return (fitness, rtp, hitFrequency);
    }

    private Individual TournamentSelect(List<Individual> population, List<(double, double, double)> fitnesses)
    {
        int bestIdx = -1;

        for (int i = 0; i < config.TournamentK; i++)
        {
            int idx = rng.Next(population.Count);

            if (bestIdx == -1)
            {
                bestIdx = idx;
            }
            else
            {
                bool isBetter = config.Maximize
                    ? fitnesses[idx].Item1 > fitnesses[bestIdx].Item1
                    : fitnesses[idx].Item1 < fitnesses[bestIdx].Item1;

                if (isBetter)
                {
                    bestIdx = idx;
                }
            }
        }

        return population[bestIdx];
    }

    private List<(double, double, double)> EvaluatePopulation(List<Individual> population, bool showProgress)
    {
        var fitnesses = new List<(double, double, double)>(population.Count);
        for (int i = 0; i < population.Count; i++)
        {
            if (showProgress)
            {
                Console.Write($"[E{i}]");
                if (i % 10 == 9)
                {
                    Console.WriteLine();
                }
            }

            fitnesses.Add(FitnessSphere(population[i]));
        }

        if (showProgress)
        {
            Console.WriteLine();
        }

        return fitnesses;
    }

    private Dictionary<int, List<int>> CloneItem(Dictionary<int, List<int>> item)
    {
        var result = new Dictionary<int, List<int>>();
        foreach (var kvp in item)
        {
            result[kvp.Key] = new List<int>(kvp.Value);
        }
        return result;
    }

    private Individual CloneIndividual(Individual ind)
    {
        var clone = new Individual();
        foreach (var item in ind.Items)
        {
            clone.Items.Add(CloneItem(item));
        }
        foreach (var reel in ind.Reels)
        {
            clone.Reels.Add(reel != null ? new List<int>(reel) : null!);
        }
        return clone;
    }

    private ReelGenerator GetReelGenerator(int reelIndex) => REEL_GENERATORS[reelIndex];

    private Dictionary<int, List<int>> BlendReelItem(Individual a, Individual b, int reelIndex)
    {
        var low = LOWS[reelIndex];
        var high = HIGHS[reelIndex];
        var symbols = SYMBOLS_BY_REEL[reelIndex];

        var item = new Dictionary<int, List<int>>();

        foreach (int symbol in symbols)
        {
            var blended = new List<int>();
            for (int i = 0; i < a.Items[reelIndex][symbol].Count; i++)
            {
                int x = a.Items[reelIndex][symbol][i];
                int y = b.Items[reelIndex][symbol][i];
                int lo = (int)Math.Round(Math.Min(x, y) - CROSSOVER_ALPHA * Math.Abs(x - y));
                int hi = (int)Math.Round(Math.Max(x, y) + CROSSOVER_ALPHA * Math.Abs(x - y));

                int value = rng.Next(lo, hi + 1);
                value = Math.Min(high[symbol][i], Math.Max(low[symbol][i], value));
                blended.Add(value);
            }
            item[symbol] = blended;
        }

        return item;
    }

    private (Individual, Individual) CrossoverBlend(Individual a, Individual b)
    {
        var c1 = new Individual();
        var c2 = new Individual();

        for (int reelIndex = 0; reelIndex < REEL_COUNT; reelIndex++)
        {
            bool generated = false;
            while (!generated)
            {
                var item1 = BlendReelItem(a, b, reelIndex);
                var item2 = BlendReelItem(a, b, reelIndex);

                var reelGen = GetReelGenerator(reelIndex);
                var reel1 = reelGen.Generate(item1, REEL_RADII[reelIndex], REEL_SEEDS[reelIndex]);
                var reel2 = reelGen.Generate(item2, REEL_RADII[reelIndex], REEL_SEEDS[reelIndex]);

                if (reel1 != null && reel2 != null)
                {
                    c1.Items.Add(item1);
                    c1.Reels.Add(reel1);
                    c2.Items.Add(item2);
                    c2.Reels.Add(reel2);
                    generated = true;
                }
            }
        }

        return (c1, c2);
    }

    private Individual MutateReal(Individual ind)
    {
        var outInd = new Individual();

        for (int reelIndex = 0; reelIndex < REEL_COUNT; reelIndex++)
        {
            bool generated = false;
            while (!generated)
            {
                var low = LOWS[reelIndex];
                var high = HIGHS[reelIndex];
                var symbols = SYMBOLS_BY_REEL[reelIndex];
                var item = new Dictionary<int, List<int>>();

                foreach (int symbol in symbols)
                {
                    item[symbol] = new List<int>();
                    for (int index = 0; index < ind.Items[reelIndex][symbol].Count; index++)
                    {
                        int val = ind.Items[reelIndex][symbol][index];

                        if (rng.NextDouble() < config.MutationRate)
                        {
                            double gaussian = GetGaussian(0.0, MUT_SIGMA);
                            val += (int)gaussian;
                        }

                        val = Math.Min(high[symbol][index], Math.Max(low[symbol][index], val));
                        item[symbol].Add(val);
                    }
                }

                var reelGen = GetReelGenerator(reelIndex);
                var reel = reelGen.Generate(item, REEL_RADII[reelIndex], REEL_SEEDS[reelIndex]);
                if (reel != null)
                {
                    outInd.Items.Add(item);
                    outInd.Reels.Add(reel);
                    generated = true;
                }
            }
        }

        return outInd;
    }

    private Individual CreateReal()
    {
        var ind = new Individual();

        for (int reelIndex = 0; reelIndex < REEL_COUNT; reelIndex++)
        {
            bool generated = false;
            while (!generated)
            {
                var low = LOWS[reelIndex];
                var high = HIGHS[reelIndex];
                var symbols = SYMBOLS_BY_REEL[reelIndex];
                var item = new Dictionary<int, List<int>>();

                foreach (int symbol in symbols)
                {
                    item[symbol] = new List<int>();
                    for (int index = 0; index < low[symbol].Count; index++)
                    {
                        item[symbol].Add(rng.Next(low[symbol][index], high[symbol][index] + 1));
                    }
                }

                var reelGen = GetReelGenerator(reelIndex);
                var reel = reelGen.Generate(item, REEL_RADII[reelIndex], REEL_SEEDS[reelIndex]);
                if (reel != null)
                {
                    ind.Items.Add(item);
                    ind.Reels.Add(reel);
                    generated = true;
                }
                else
                {
                    Console.Write(".");
                }
            }
        }

        return ind;
    }

    public GAResult Run()
    {
        Console.WriteLine(">>> Initializing population...");
        var population = new List<Individual>();
        for (int i = 0; i < config.PopSize; i++)
        {
            if (config.VerboseProgress)
            {
                Console.Write($"[{i}]");
                if (i % 10 == 9)
                {
                    Console.WriteLine();
                }
            }
            population.Add(CreateReal());
        }
        if (config.VerboseProgress)
        {
            Console.WriteLine();
        }
        Console.WriteLine($">>> Population created with {population.Count} individuals");

        Console.WriteLine(">>> Evaluating initial population...");
        var fitnesses = EvaluatePopulation(population, config.VerboseProgress);
        Console.WriteLine($">>> Initial evaluation complete, {fitnesses.Count} fitnesses calculated");

        int bestIdx = 0;
        for (int i = 1; i < fitnesses.Count; i++)
        {
            bool isBetter = config.Maximize
                ? fitnesses[i].Item1 > fitnesses[bestIdx].Item1
                : fitnesses[i].Item1 < fitnesses[bestIdx].Item1;

            if (isBetter)
            {
                bestIdx = i;
            }
        }

        var bestInd = CloneIndividual(population[bestIdx]);
        var bestFit = fitnesses[bestIdx];
        var history = new List<double> { bestFit.Item1 };

        for (int gen = 0; gen < config.Generations; gen++)
        {
            var generationStopwatch = Stopwatch.StartNew();

            var indexed = Enumerable.Range(0, config.PopSize).ToList();
            indexed.Sort((a, b) =>
            {
                int cmp = fitnesses[a].Item1.CompareTo(fitnesses[b].Item1);
                return config.Maximize ? -cmp : cmp;
            });

            var newPopulation = new List<Individual>();
            for (int i = 0; i < config.Elitism && i < indexed.Count; i++)
            {
                newPopulation.Add(CloneIndividual(population[indexed[i]]));
            }

            while (newPopulation.Count < config.PopSize)
            {
                var p1 = TournamentSelect(population, fitnesses);
                var p2 = TournamentSelect(population, fitnesses);

                Individual c1;
                Individual c2;
                if (rng.NextDouble() < config.CrossoverRate)
                {
                    (c1, c2) = CrossoverBlend(p1, p2);
                }
                else
                {
                    c1 = CloneIndividual(p1);
                    c2 = CloneIndividual(p2);
                }

                c1 = MutateReal(c1);
                if (newPopulation.Count < config.PopSize)
                {
                    newPopulation.Add(c1);
                }

                if (newPopulation.Count < config.PopSize)
                {
                    c2 = MutateReal(c2);
                    newPopulation.Add(c2);
                }
            }

            population = newPopulation;
            fitnesses = EvaluatePopulation(population, showProgress: false);

            int curBestIdx = 0;
            for (int i = 1; i < fitnesses.Count; i++)
            {
                bool isBetter = config.Maximize
                    ? fitnesses[i].Item1 > fitnesses[curBestIdx].Item1
                    : fitnesses[i].Item1 < fitnesses[curBestIdx].Item1;

                if (isBetter)
                {
                    curBestIdx = i;
                }
            }

            var curBestFit = fitnesses[curBestIdx];
            bool isImproved = config.Maximize
                ? curBestFit.Item1 > bestFit.Item1
                : curBestFit.Item1 < bestFit.Item1;

            if (isImproved)
            {
                bestInd = CloneIndividual(population[curBestIdx]);
                bestFit = curBestFit;
            }

            generationStopwatch.Stop();
            Console.WriteLine($"---- Generation {gen} ----");
            Console.WriteLine($"Generation time: {generationStopwatch.Elapsed.TotalSeconds:F2} s");
            Console.WriteLine($"Best fitness: {bestFit.Item1}");
            Console.WriteLine($"RTP: {bestFit.Item2}, Hit Frequency: {bestFit.Item3}");

            history.Add(bestFit.Item1);
        }

        return new GAResult
        {
            BestIndividual = bestInd,
            BestFitness = bestFit,
            HistoryBest = history
        };
    }

    private double GetGaussian(double mean, double stdDev)
    {
        double u1 = rng.NextDouble();
        double u2 = rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + stdDev * z;
    }
}
