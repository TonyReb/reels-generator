using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ReelsGenerator;

public static class ConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static AppConfigRoot LoadRoot(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Config file not found: {configPath}");
        }

        var yaml = File.ReadAllText(configPath);
        return Deserializer.Deserialize<AppConfigRoot>(yaml)
            ?? throw new InvalidOperationException("Failed to parse YAML config.");
    }

    public static AppConfig ResolveConfig(string configPath, string? profileName)
    {
        var root = LoadRoot(configPath);
        return ResolveConfig(root, profileName);
    }

    public static string? GetDefaultProfileName(AppConfigRoot root)
    {
        if (root.Profiles.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(root.DefaultProfile) && root.Profiles.ContainsKey(root.DefaultProfile))
        {
            return root.DefaultProfile;
        }

        return root.Profiles.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First();
    }

    public static AppConfig ResolveConfig(AppConfigRoot root, string? profileName)
    {
        if (root.Profiles.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                throw new InvalidOperationException("This config file does not define profiles.");
            }

            return root.ToSingleConfig();
        }

        string effectiveProfile = profileName
            ?? GetDefaultProfileName(root)
            ?? throw new InvalidOperationException("No profiles defined in config.");

        if (!root.Profiles.TryGetValue(effectiveProfile, out var profileConfig))
        {
            throw new InvalidOperationException($"Unknown config profile: {effectiveProfile}");
        }

        return profileConfig;
    }

    public static IReadOnlyList<ConfigFileOption> GetAvailableConfigFiles()
    {
        var result = new List<ConfigFileOption>();

        string cwdConfigsRoot = Path.Combine(Directory.GetCurrentDirectory(), "configs");
        string baseConfigsRoot = Path.Combine(AppContext.BaseDirectory, "configs");
        string root = Directory.Exists(cwdConfigsRoot) ? cwdConfigsRoot : baseConfigsRoot;

        if (!Directory.Exists(root))
        {
            return Array.Empty<ConfigFileOption>();
        }

        foreach (var configPath in Directory.GetFiles(root, "config.yml", SearchOption.AllDirectories))
        {
            string? dir = Path.GetDirectoryName(configPath);
            if (dir == null)
            {
                continue;
            }

            string relativeFolder = Path.GetRelativePath(root, dir).Replace('\\', '/');
            string name = relativeFolder == "." ? "configs/root" : $"configs/{relativeFolder}";
            result.Add(new ConfigFileOption(name, Path.GetFullPath(configPath)));
        }

        return result
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class ConfigFileOption
{
    public ConfigFileOption(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name { get; }
    public string Path { get; }

    public override string ToString()
    {
        return Name;
    }
}
