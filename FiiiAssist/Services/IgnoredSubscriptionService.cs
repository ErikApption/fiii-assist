using FiiiAssist.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FiiiAssist.Services;

/// <summary>
/// Persists and loads ignored subscription patterns to a JSON file.
/// Patterns that have been ignored won't appear in future subscription detection scans.
/// </summary>
public class IgnoredSubscriptionService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public IgnoredSubscriptionService()
    {
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FiiiAssist");

        Directory.CreateDirectory(appDataFolder);
        _filePath = Path.Combine(appDataFolder, "ignored-subscriptions.json");
    }

    public List<IgnoredSubscriptionPattern> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<IgnoredSubscriptionPattern>>(json, _jsonOptions)
                       ?? [];
            }
        }
        catch
        {
            // If the file is corrupt or unreadable, return empty list
        }

        return [];
    }

    public void Save(IReadOnlyList<IgnoredSubscriptionPattern> patterns)
    {
        try
        {
            var json = JsonSerializer.Serialize(patterns, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Swallow write errors to avoid crashing the app
        }
    }

    /// <summary>
    /// Adds a new ignored pattern and persists the updated list.
    /// </summary>
    public void AddIgnored(string payeeName, decimal approximateAmount)
    {
        var patterns = Load();
        patterns.Add(new IgnoredSubscriptionPattern
        {
            PayeeName = payeeName,
            ApproximateAmount = approximateAmount,
            IgnoredAt = DateTime.UtcNow,
        });
        Save(patterns);
    }

    /// <summary>
    /// Adds a payee-level ignore entry so all patterns for this payee are excluded.
    /// </summary>
    public void AddIgnoredPayee(string payeeName)
    {
        var patterns = Load();
        patterns.Add(new IgnoredSubscriptionPattern
        {
            PayeeName = payeeName,
            IgnoreAllAmounts = true,
            IgnoredAt = DateTime.UtcNow,
        });
        Save(patterns);
    }

    /// <summary>
    /// Checks whether a given payee + amount combination is in the ignore list.
    /// Uses case-insensitive payee match and ±10% amount tolerance,
    /// or matches all amounts if the payee is fully ignored.
    /// </summary>
    public bool IsIgnored(string payeeName, decimal averageAmount)
    {
        var patterns = Load();
        foreach (var pattern in patterns)
        {
            if (!string.Equals(pattern.PayeeName, payeeName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Payee-level ignore: all amounts for this payee are excluded
            if (pattern.IgnoreAllAmounts)
                return true;

            // Check if amounts are within ±10%
            if (pattern.ApproximateAmount == 0 && averageAmount == 0)
                return true;

            if (pattern.ApproximateAmount != 0)
            {
                var diff = Math.Abs(averageAmount - pattern.ApproximateAmount) / Math.Abs(pattern.ApproximateAmount);
                if (diff <= 0.10m)
                    return true;
            }
        }

        return false;
    }
}
