using System;
using System.Collections.Generic;
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
        // Instance variables for configuration
        private Dictionary<int, List<int>> LOW;
        private Dictionary<int, List<int>> HIGH;
        private int[] SYMBOLS;
        private double MUT_SIGMA;
        private float CROSSOVER_ALPHA;
        private double TARGET_RTP;
        private double TARGET_HIT_FREQUENCY;
        private int REEL_RADIUS;
        private int REEL_SEED;
        private int SPIN_NUMBER;
        private SlotMachineConfig SLOT_CONFIG;

        private GAConfig config;
        private Random rng;

        public GeneticAlgorithm(
            GAConfig gaConfig,
            Dictionary<int, List<int>> low,
            Dictionary<int, List<int>> high,
            double targetRtp,
            double targetHitFrequency,
            int reelRadius,
            int reelSeed,
            int spinNumber,
            SlotMachineConfig slotConfig)
        {
            config = gaConfig;
            rng = new Random(config.Seed);
            LOW = low;
            HIGH = high;
            SYMBOLS = LOW.Keys.Union(HIGH.Keys).OrderBy(x => x).ToArray();
            foreach (var symbol in SYMBOLS)
            {
                if (!LOW.ContainsKey(symbol) || !HIGH.ContainsKey(symbol))
                {
                    throw new InvalidOperationException($"Symbol {symbol} must exist in both low and high stacks.");
                }

                if (LOW[symbol].Count != HIGH[symbol].Count)
                {
                    throw new InvalidOperationException($"Symbol {symbol} low/high stack lengths must match.");
                }
            }
            TARGET_RTP = targetRtp;
            TARGET_HIT_FREQUENCY = targetHitFrequency;
            REEL_RADIUS = reelRadius;
            REEL_SEED = reelSeed;
            SPIN_NUMBER = spinNumber;
            SLOT_CONFIG = slotConfig;
            
            MUT_SIGMA = config.MutationSigma;
            CROSSOVER_ALPHA = config.CrossoverAlpha;

        }

        private (double, double, double) FitnessSphere(Individual ind)
    {
        // Extract reels from individual
        var reelsData = new List<List<int>>();
        foreach (var reel in ind.Reels)
        {
            if (reel != null)
                reelsData.Add(reel);
        }

        Game game = new(reelsData, SPIN_NUMBER, SLOT_CONFIG);
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

    private ReelGenerator CreateReelGenerator()
    {
        var specialSymbols = SLOT_CONFIG.IconWild.Concat(SLOT_CONFIG.IconScatter);
        return new ReelGenerator(specialSymbols);
    }

    private (Individual, Individual) CrossoverBlend(Individual a, Individual b)
    {
        var rng1 = new Random(this.rng.Next());
        var rng2 = new Random(this.rng.Next());

        var c1 = new Individual();
        var c2 = new Individual();

        int reelNumber = 0;

        while (reelNumber < 3)
        {
            var item1 = new Dictionary<int, List<int>>();
            var item2 = new Dictionary<int, List<int>>();

            foreach (int symbol in SYMBOLS)
            {
                var t1 = new List<int>();
                var t2 = new List<int>();

                for (int i = 0; i < a.Items[reelNumber][symbol].Count; i++)
                {
                    int x = a.Items[reelNumber][symbol][i];
                    int y = b.Items[reelNumber][symbol][i];

                    int lo = (int)Math.Round(Math.Min(x, y) - CROSSOVER_ALPHA * Math.Abs(x - y));
                    int hi = (int)Math.Round(Math.Max(x, y) + CROSSOVER_ALPHA * Math.Abs(x - y));

                    t1.Add(rng1.Next(lo, hi + 1));
                    t2.Add(rng2.Next(lo, hi + 1));
                }

                item1[symbol] = new List<int>();
                item2[symbol] = new List<int>();

                for (int i = 0; i < t1.Count; i++)
                {
                    item1[symbol].Add(Math.Min(HIGH[symbol][i], Math.Max(LOW[symbol][i], t1[i])));
                    item2[symbol].Add(Math.Min(HIGH[symbol][i], Math.Max(LOW[symbol][i], t2[i])));
                }
            }

            var reelGen = CreateReelGenerator();
            var reel1 = reelGen.Generate(item1, REEL_RADIUS, REEL_SEED);
            var reel2 = reelGen.Generate(item2, REEL_RADIUS, REEL_SEED);

            if (reel1 != null && reel2 != null)
            {
                c1.Items.Add(item1);
                c1.Reels.Add(reel1);

                c2.Items.Add(item2);
                c2.Reels.Add(reel2);

                reelNumber++;
            }
        }

        return (c1, c2);
    }

    private Individual MutateReal(Individual ind)
    {
        var outInd = new Individual();
        var random = new Random(rng.Next());

        int reelNumber = 0;

        while (reelNumber < 3)
        {
            var item = new Dictionary<int, List<int>>();

            foreach (int symbol in SYMBOLS)
            {
                item[symbol] = new List<int>();

                for (int index = 0; index < ind.Items[reelNumber][symbol].Count; index++)
                {
                    int val = ind.Items[reelNumber][symbol][index];

                    if (random.NextDouble() < config.MutationRate)
                    {
                        // Gaussian mutation
                        double gaussian = GetGaussian(0.0, MUT_SIGMA, random);
                        val = val + (int)gaussian;
                    }

                    val = Math.Min(HIGH[symbol][index], Math.Max(LOW[symbol][index], val));
                    item[symbol].Add(val);
                }
            }

            var reelGen = CreateReelGenerator();
            var reel = reelGen.Generate(item, REEL_RADIUS, REEL_SEED);

            if (reel != null)
            {
                outInd.Items.Add(item);
                outInd.Reels.Add(reel);
                reelNumber++;
            }
        }

        return outInd;
    }

    private Individual CreateReal()
    {
        var ind = new Individual();
        int reelNumber = 0;

        while (reelNumber < 3)
        {
            var item = new Dictionary<int, List<int>>();

            foreach (int symbol in SYMBOLS)
            {
                item[symbol] = new List<int>();
                for (int index = 0; index < LOW[symbol].Count; index++)
                {
                    item[symbol].Add(rng.Next(LOW[symbol][index], HIGH[symbol][index] + 1));
                }
            }

            var reelGen = CreateReelGenerator();
            var reel = reelGen.Generate(item, REEL_RADIUS, REEL_SEED);

            if (reel != null)
            {
                ind.Items.Add(item);
                ind.Reels.Add(reel);
                reelNumber++;
            }
            else
            {
                // Reel generation failed, retry
                Console.Write(".");
            }
        }

        return ind;
    }

    public GAResult Run()
    {
        Console.WriteLine(">>> Initializing population...");
        // Initialize population
        var population = new List<Individual>();
        for (int i = 0; i < config.PopSize; i++)
        {
            Console.Write($"[{i}]");
            if (i % 10 == 9) Console.WriteLine();
            population.Add(CreateReal());
        }
        Console.WriteLine();
        Console.WriteLine($">>> Population created with {population.Count} individuals");

        // Evaluate population
        Console.WriteLine(">>> Evaluating initial population...");
        var fitnesses = population.Select((ind, idx) => 
        {
            Console.Write($"[E{idx}]");
            if (idx % 10 == 9) Console.WriteLine();
            return FitnessSphere(ind);
        }).ToList();
        Console.WriteLine();
        Console.WriteLine($">>> Initial evaluation complete, {fitnesses.Count} fitnesses calculated");

        // Find best individual
        int bestIdx = 0;
        for (int i = 1; i < fitnesses.Count; i++)
        {
            bool isBetter = config.Maximize
                ? fitnesses[i].Item1 > fitnesses[bestIdx].Item1
                : fitnesses[i].Item1 < fitnesses[bestIdx].Item1;

            if (isBetter)
                bestIdx = i;
        }

        var bestInd = CloneIndividual(population[bestIdx]);
        var bestFit = fitnesses[bestIdx];
        var history = new List<double> { bestFit.Item1 };

        // Main GA loop
        for (int gen = 0; gen < config.Generations; gen++)
        {
            // Sort by fitness
            var indexed = Enumerable.Range(0, config.PopSize).ToList();
            indexed.Sort((a, b) =>
            {
                int cmp = fitnesses[a].Item1.CompareTo(fitnesses[b].Item1);
                return config.Maximize ? -cmp : cmp;
            });

            // Elitism
            var newPopulation = new List<Individual>();
            for (int i = 0; i < config.Elitism && i < indexed.Count; i++)
            {
                newPopulation.Add(CloneIndividual(population[indexed[i]]));
            }

            // Generate new population
            while (newPopulation.Count < config.PopSize)
            {
                var p1 = TournamentSelect(population, fitnesses);
                var p2 = TournamentSelect(population, fitnesses);

                Individual c1, c2;
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
            fitnesses = population.Select(ind => FitnessSphere(ind)).ToList();

            // Find best in current generation
            int curBestIdx = 0;
            for (int i = 1; i < fitnesses.Count; i++)
            {
                bool isBetter = config.Maximize
                    ? fitnesses[i].Item1 > fitnesses[curBestIdx].Item1
                    : fitnesses[i].Item1 < fitnesses[curBestIdx].Item1;

                if (isBetter)
                    curBestIdx = i;
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

            Console.WriteLine($"---- Generation {gen} ----");
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

    private double GetGaussian(double mean, double stdDev, Random random)
    {
        double u1 = random.NextDouble();
        double u2 = random.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + stdDev * z;
    }
}
