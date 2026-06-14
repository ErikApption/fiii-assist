using System.Text.Json;
using Microsoft.Data.Sqlite;
using QfxWatcher.FireflyIII;

namespace QfxWatcher.Cli;

/// <summary>
/// Reads rules from an Actual Budget SQLite database and converts them to Firefly III rules.
/// Focuses on payee-based string matching rules (starts/ends/contains/is) that set a category.
/// </summary>
public sealed class ActualRuleImporter
{
    private readonly SqliteConnection _connection;
    private readonly Client _fireflyClient;

    public ActualRuleImporter(SqliteConnection connection, Client fireflyClient)
    {
        _connection = connection;
        _fireflyClient = fireflyClient;
    }

    /// <summary>
    /// Represents a parsed Actual Budget rule.
    /// </summary>
    private record ActualRule(
        string Id,
        string? Conditions,
        string? Actions,
        int? Stage);

    /// <summary>
    /// Reads rules from the Actual Budget database, converts applicable ones to Firefly III rules,
    /// and creates them via the API.
    /// </summary>
    /// <returns>Number of rules successfully created.</returns>
    public async Task<(int created, int skipped)> ImportRulesAsync(string ruleGroupTitle = "Imported from Actual Budget")
    {
        var rules = ReadActualRules();
        Console.WriteLine($"  Found {rules.Count} rule(s) in Actual Budget database.");

        var created = 0;
        var skipped = 0;

        // Find or create the rule group
        string ruleGroupId;
        try
        {
            // Try to find existing group first
            var groups = await _fireflyClient.ListRuleGroupAsync(null, 100, 1);
            var existing = groups.Data.FirstOrDefault(g =>
                string.Equals(g.Attributes.Title, ruleGroupTitle, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                ruleGroupId = existing.Id;

                // Ensure the group is active — Firefly III won't store rules into inactive groups
                if (!existing.Attributes.Active)
                {
                    await _fireflyClient.UpdateRuleGroupAsync(null, ruleGroupId, new RuleGroupUpdate
                    {
                        Title = ruleGroupTitle,
                        Active = true,
                    });
                    Console.WriteLine($"  Activated existing rule group: {ruleGroupTitle} (ID: {ruleGroupId})");
                }
                else
                {
                    Console.WriteLine($"  Using existing rule group: {ruleGroupTitle} (ID: {ruleGroupId})");
                }
            }
            else
            {
                var ruleGroup = await _fireflyClient.StoreRuleGroupAsync(null, new RuleGroupStore
                {
                    Title = ruleGroupTitle,
                    Active = true,
                });
                ruleGroupId = ruleGroup.Data.Id;
                Console.WriteLine($"  Created rule group: {ruleGroupTitle} (ID: {ruleGroupId})");
            }
        }
        catch (ApiException ex)
        {
            Console.Error.WriteLine($"  ERROR: Could not create or find rule group: {ex.Message}");
            return (0, rules.Count);
        }

        // Resolve category and payee names for ID-based references
        var categoryNames = LoadCategoryNames();
        var payeeNames = LoadPayeeNames();

        foreach (var rule in rules)
        {
            var fireflyRule = ConvertRule(rule, categoryNames, payeeNames);
            if (fireflyRule is null)
            {
                skipped++;
                continue;
            }

            // Use both ID and title — Firefly III should match on either
            fireflyRule.Rule_group_id = ruleGroupId;
            fireflyRule.Rule_group_title = ruleGroupTitle;

            try
            {
                await _fireflyClient.StoreRuleAsync(null, fireflyRule);
                Console.WriteLine($"    ✔ Created: {fireflyRule.Title}");
                created++;
            }
            catch (ApiException ex)
            {
                if (skipped < 3)
                {
                    // Print debug info for first few failures
                    Console.Error.WriteLine($"    ✘ Rejected: {fireflyRule.Title}");
                    Console.Error.WriteLine($"      Status {ex.StatusCode}, Rule group ID used: \"{ruleGroupId}\"");
                    Console.Error.WriteLine($"      Trigger: {fireflyRule.Triggers.FirstOrDefault()?.Type} = \"{fireflyRule.Triggers.FirstOrDefault()?.Value}\"");
                    Console.Error.WriteLine($"      Action: {fireflyRule.Actions.FirstOrDefault()?.Type} = \"{fireflyRule.Actions.FirstOrDefault()?.Value}\"");
                    if (!string.IsNullOrWhiteSpace(ex.Response))
                        Console.Error.WriteLine($"      Response: {ex.Response[..Math.Min(ex.Response.Length, 300)]}");
                }
                else
                {
                    Console.Error.WriteLine($"    ✘ Rejected: {fireflyRule.Title} (status {ex.StatusCode})");
                }
                skipped++;
            }
        }

        return (created, skipped);
    }

    private List<ActualRule> ReadActualRules()
    {
        var rules = new List<ActualRule>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, conditions, actions, stage FROM rules WHERE tombstone = 0";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rules.Add(new ActualRule(
                Id: reader.GetString(0),
                Conditions: reader.IsDBNull(1) ? null : reader.GetString(1),
                Actions: reader.IsDBNull(2) ? null : reader.GetString(2),
                Stage: reader.IsDBNull(3) ? null : reader.GetInt32(3)
            ));
        }

        return rules;
    }

    private Dictionary<string, string> LoadCategoryNames()
    {
        var names = new Dictionary<string, string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM categories WHERE tombstone = 0";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names[reader.GetString(0)] = reader.GetString(1);
        }
        return names;
    }

    private Dictionary<string, string> LoadPayeeNames()
    {
        var names = new Dictionary<string, string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM payees WHERE tombstone = 0";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names[reader.GetString(0)] = reader.GetString(1);
        }
        return names;
    }

    private RuleStore? ConvertRule(
        ActualRule rule,
        Dictionary<string, string> categoryNames,
        Dictionary<string, string> payeeNames)
    {
        if (string.IsNullOrWhiteSpace(rule.Conditions) || string.IsNullOrWhiteSpace(rule.Actions))
            return null;

        // Parse conditions and actions JSON arrays
        List<ActualCondition>? conditions;
        List<ActualAction>? actions;

        try
        {
            conditions = JsonSerializer.Deserialize<List<ActualCondition>>(rule.Conditions, JsonOptions);
            actions = JsonSerializer.Deserialize<List<ActualAction>>(rule.Actions, JsonOptions);
        }
        catch
        {
            return null;
        }

        if (conditions is null || conditions.Count == 0 || actions is null || actions.Count == 0)
            return null;

        // Convert conditions to Firefly III triggers
        var triggers = new List<RuleTriggerStore>();

        foreach (var cond in conditions)
        {
            var trigger = ConvertCondition(cond, payeeNames);
            if (trigger is not null)
                triggers.Add(trigger);
        }

        if (triggers.Count == 0)
            return null;

        // Convert actions to Firefly III actions
        var fireflyActions = new List<RuleActionStore>();

        foreach (var action in actions)
        {
            var fireflyAction = ConvertAction(action, categoryNames, payeeNames);
            if (fireflyAction is not null)
                fireflyActions.Add(fireflyAction);
        }

        if (fireflyActions.Count == 0)
            return null;

        // Build a descriptive title
        var title = BuildTitle(conditions, actions, categoryNames, payeeNames);

        return new RuleStore
        {
            Title = title,
            Description = $"Imported from Actual Budget (rule {rule.Id})",
            Trigger = RuleTriggerType.StoreJournal,
            Active = true,
            Strict = conditions.Count > 1, // AND if multiple conditions
            Triggers = triggers,
            Actions = fireflyActions,
        };
    }

    private RuleTriggerStore? ConvertCondition(ActualCondition condition, Dictionary<string, string> payeeNames)
    {
        var field = condition.Field?.ToLowerInvariant();
        var op = condition.Op?.ToLowerInvariant();
        var value = condition.Value ?? string.Empty;

        // In Actual Budget, the "description" field on transactions is actually the payee FK.
        // Rules with field="description" and a UUID value are payee-based rules.
        // Also "payee" and "imported_payee" can reference UUIDs.
        if ((field == "payee" || field == "imported_payee" || field == "description")
            && payeeNames.TryGetValue(value, out var resolvedPayee))
        {
            value = resolvedPayee;
        }

        // If value is still a UUID pattern after resolution attempt, skip this condition
        // (it references a deleted payee we can't resolve)
        if (IsUuid(value))
            return null;

        // Map Actual's field+op to Firefly III trigger keywords
        // In Actual Budget, payee/description-based rules map to Firefly III's "description" triggers
        // because we import the payee name as the transaction description.
        RuleTriggerKeyword? triggerType = (field, op) switch
        {
            ("payee", "is") => RuleTriggerKeyword.Description_is,
            ("payee", "contains") => RuleTriggerKeyword.Description_contains,
            ("payee", "startswith") => RuleTriggerKeyword.Description_starts,
            ("payee", "endswith") => RuleTriggerKeyword.Description_ends,
            ("payee", "oneof") => RuleTriggerKeyword.Description_is,

            ("imported_payee", "is") => RuleTriggerKeyword.Description_is,
            ("imported_payee", "contains") => RuleTriggerKeyword.Description_contains,
            ("imported_payee", "startswith") => RuleTriggerKeyword.Description_starts,
            ("imported_payee", "endswith") => RuleTriggerKeyword.Description_ends,

            ("description", "is") => RuleTriggerKeyword.Description_is,
            ("description", "contains") => RuleTriggerKeyword.Description_contains,
            ("description", "startswith") => RuleTriggerKeyword.Description_starts,
            ("description", "endswith") => RuleTriggerKeyword.Description_ends,

            ("imported_description", "is") => RuleTriggerKeyword.Description_is,
            ("imported_description", "contains") => RuleTriggerKeyword.Description_contains,
            ("imported_description", "startswith") => RuleTriggerKeyword.Description_starts,
            ("imported_description", "endswith") => RuleTriggerKeyword.Description_ends,

            ("notes", "is") => RuleTriggerKeyword.Description_is,
            ("notes", "contains") => RuleTriggerKeyword.Description_contains,
            ("notes", "startswith") => RuleTriggerKeyword.Description_starts,
            ("notes", "endswith") => RuleTriggerKeyword.Description_ends,

            _ => null,
        };

        if (triggerType is null || string.IsNullOrWhiteSpace(value))
            return null;

        return new RuleTriggerStore
        {
            Type = triggerType.Value,
            Value = value,
            Active = true,
        };
    }

    private static bool IsUuid(string value) =>
        value.Length >= 32 && Guid.TryParse(value, out _);

    private RuleActionStore? ConvertAction(
        ActualAction action,
        Dictionary<string, string> categoryNames,
        Dictionary<string, string> payeeNames)
    {
        var field = action.Field?.ToLowerInvariant();
        var value = action.Value ?? string.Empty;

        return field switch
        {
            "category" => new RuleActionStore
            {
                Type = RuleActionKeyword.Set_category,
                Value = categoryNames.TryGetValue(value, out var catName) ? catName : value,
                Active = true,
            },
            "payee" => new RuleActionStore
            {
                Type = RuleActionKeyword.Set_description,
                Value = payeeNames.TryGetValue(value, out var pName) ? pName : value,
                Active = true,
            },
            _ => null,
        };
    }

    private string BuildTitle(
        List<ActualCondition> conditions,
        List<ActualAction> actions,
        Dictionary<string, string> categoryNames,
        Dictionary<string, string> payeeNames)
    {
        var parts = new List<string>();

        // Describe the trigger
        var firstCond = conditions.FirstOrDefault();
        if (firstCond is not null)
        {
            var val = firstCond.Value ?? string.Empty;
            var field = firstCond.Field?.ToLowerInvariant() ?? "";

            // Resolve IDs to names
            if ((field == "payee" || field == "imported_payee" || field == "description")
                && payeeNames.TryGetValue(val, out var pn))
                val = pn;
            else if (field == "category" && categoryNames.TryGetValue(val, out var cn))
                val = cn;

            parts.Add($"When {firstCond.Field} {firstCond.Op} \"{Truncate(val, 30)}\"");
        }

        // Describe the action
        var firstAction = actions.FirstOrDefault();
        if (firstAction is not null)
        {
            var val = firstAction.Value ?? string.Empty;
            var field = firstAction.Field?.ToLowerInvariant() ?? "";

            if (field == "category" && categoryNames.TryGetValue(val, out var cn))
                val = cn;
            else if (field == "payee" && payeeNames.TryGetValue(val, out var pn2))
                val = pn2;

            parts.Add($"→ set {firstAction.Field} to \"{Truncate(val, 30)}\"");
        }

        var title = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(title) ? "Imported rule" : title;
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length > maxLen ? s[..maxLen] + "…" : s;

    // ── JSON models for Actual Budget rule format ──────────────────────────────

    private class ActualCondition
    {
        public string? Field { get; set; }
        public string? Op { get; set; }
        public string? Value { get; set; }
        public string? Type { get; set; }
    }

    private class ActualAction
    {
        public string? Field { get; set; }
        public string? Op { get; set; }
        public string? Value { get; set; }
        public string? Type { get; set; }
        public JsonElement? Options { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
