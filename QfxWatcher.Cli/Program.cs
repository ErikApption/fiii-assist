using System.CommandLine;
using System.Globalization;
using QfxWatcher.Cli;
using QfxWatcher.FireflyIII;
using QfxWatcher.Services;

var rootCommand = new RootCommand("Import transactions from an Actual Budget SQLite export into Firefly III");

var dbOption = new Option<FileInfo>(
    name: "--db",
    description: "Path to the Actual Budget SQLite database file (db.sqlite)")
{ IsRequired = true };

var serverOption = new Option<string>(
    name: "--server",
    description: "Firefly III server URL (e.g. https://firefly.example.com)")
{ IsRequired = true };

var tokenOption = new Option<string?>(
    name: "--token",
    description: "Firefly III personal access token (or set FIREFLY_III_TOKEN env var)")
{ IsRequired = false };

var accountMapOption = new Option<Dictionary<string, string>?>(
    name: "--map",
    description: "Account mapping in format ActualAccountName=FireflyAccountId (can be specified multiple times)",
    parseArgument: result =>
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in result.Tokens)
        {
            var parts = token.Value.Split('=', 2);
            if (parts.Length == 2)
                dict[parts[0]] = parts[1];
        }
        return dict;
    })
{ AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.ZeroOrMore };

var ignoreSslOption = new Option<bool>(
    name: "--ignore-ssl",
    description: "Ignore SSL certificate validation errors",
    getDefaultValue: () => false);

var dryRunOption = new Option<bool>(
    name: "--dry-run",
    description: "Show what would be imported without actually importing",
    getDefaultValue: () => false);

var errorOnDuplicateOption = new Option<bool>(
    name: "--error-on-duplicate",
    description: "Treat duplicate transaction hashes as errors",
    getDefaultValue: () => false);

rootCommand.AddOption(dbOption);
rootCommand.AddOption(serverOption);
rootCommand.AddOption(tokenOption);
rootCommand.AddOption(accountMapOption);
rootCommand.AddOption(ignoreSslOption);
rootCommand.AddOption(dryRunOption);
rootCommand.AddOption(errorOnDuplicateOption);

rootCommand.SetHandler(async (context) =>
{
    var db = context.ParseResult.GetValueForOption(dbOption)!;
    var server = context.ParseResult.GetValueForOption(serverOption)!;
    var token = context.ParseResult.GetValueForOption(tokenOption)
               ?? Environment.GetEnvironmentVariable("FIREFLY_III_TOKEN");

    if (string.IsNullOrWhiteSpace(token))
    {
        Console.Error.WriteLine("ERROR: No token provided. Use --token or set the FIREFLY_III_TOKEN environment variable.");
        context.ExitCode = 1;
        return;
    }
    var accountMap = context.ParseResult.GetValueForOption(accountMapOption);
    var ignoreSsl = context.ParseResult.GetValueForOption(ignoreSslOption);
    var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
    var errorOnDuplicate = context.ParseResult.GetValueForOption(errorOnDuplicateOption);

    Console.WriteLine($"Opening Actual Budget database: {db.FullName}");
    Console.WriteLine();

    using var reader = new ActualBudgetReader(db.FullName);
    var accounts = reader.GetAccounts();

    Console.WriteLine($"Found {accounts.Count} account(s) in database:");
    foreach (var acct in accounts)
    {
        var status = acct.Closed ? " [closed]" : "";
        Console.WriteLine($"  • {acct.Name}{status} ({acct.Id})");
    }
    Console.WriteLine();

    // Set up Firefly III service
    using var fireflyService = new FireflyIIIService();
    fireflyService.Configure(server, ignoreSsl);

    // Log validation errors per-transaction instead of aborting
    var errorCount = 0;
    fireflyService.OnTransactionError = (tx, response) =>
    {
        errorCount++;
        var name = string.IsNullOrWhiteSpace(tx.Name) ? "(no payee)" : tx.Name;
        Console.Error.WriteLine($"    REJECTED: {tx.Date:yyyy-MM-dd} {tx.Amount,10:C}  {name}");
        if (!string.IsNullOrWhiteSpace(response))
            Console.Error.WriteLine($"      → {response}");
    };

    var loggedIn = await fireflyService.LoginAsync(token);
    if (!loggedIn)
    {
        Console.Error.WriteLine("ERROR: Failed to authenticate with Firefly III. Check your server URL and token.");
        context.ExitCode = 1;
        return;
    }

    Console.WriteLine("Connected to Firefly III successfully.");

    // Get Firefly III accounts for interactive mapping
    var fireflyAccounts = await fireflyService.GetAccountsAsync();
    Console.WriteLine($"Found {fireflyAccounts.Count} asset account(s) in Firefly III:");
    foreach (var fa in fireflyAccounts)
    {
        Console.WriteLine($"  • [{fa.Data.Id}] {fa.Data.Attributes.Name}");
    }
    Console.WriteLine();

    // Build resolved mapping: Actual account ID -> Firefly account ID
    var resolvedMap = new Dictionary<string, string>();

    if (accountMap is { Count: > 0 })
    {
        // Resolve user-provided mappings (by name or ID)
        foreach (var (actualKey, fireflyId) in accountMap)
        {
            var matchedAccount = accounts.FirstOrDefault(a =>
                a.Name.Equals(actualKey, StringComparison.OrdinalIgnoreCase) ||
                a.Id.Equals(actualKey, StringComparison.OrdinalIgnoreCase));

            if (matchedAccount is null)
            {
                Console.Error.WriteLine($"WARNING: No Actual account matched '{actualKey}' — skipping.");
                continue;
            }

            resolvedMap[matchedAccount.Id] = fireflyId;
            Console.WriteLine($"  Mapped: {matchedAccount.Name} → Firefly account {fireflyId}");
        }
    }
    else
    {
        // Auto-map by matching account names
        Console.WriteLine("No explicit mapping provided. Attempting auto-match by account name...");
        foreach (var actualAcct in accounts.Where(a => !a.Closed))
        {
            var match = fireflyAccounts.FirstOrDefault(fa =>
                fa.Data.Attributes.Name.Equals(actualAcct.Name, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                resolvedMap[actualAcct.Id] = match.Data.Id;
                Console.WriteLine($"  Auto-matched: {actualAcct.Name} → Firefly account {match.Data.Id}");
            }
            else
            {
                Console.WriteLine($"  No match for: {actualAcct.Name} — skipping (use --map to assign manually)");
            }
        }
    }

    Console.WriteLine();

    if (resolvedMap.Count == 0)
    {
        Console.Error.WriteLine("ERROR: No account mappings resolved. Nothing to import.");
        Console.Error.WriteLine("Use --map \"ActualAccountName=FireflyAccountId\" to specify mappings.");
        context.ExitCode = 1;
        return;
    }

    // Import transactions for each mapped account
    var totalImported = 0;

    // Load or create progress file for resume support
    var progressPath = ImportProgress.GetDefaultPath(db.FullName);
    var progress = ImportProgress.Load(progressPath);
    Console.WriteLine($"Progress file: {progressPath}");
    Console.WriteLine();

    // Build mapping for transfer deduplication
    var allActualAccountIds = resolvedMap;

    var skippedAccounts = 0;
    foreach (var (actualAccountId, fireflyAccountId) in resolvedMap)
    {
        var actualAccount = accounts.First(a => a.Id == actualAccountId);

        // Skip already-completed accounts
        if (progress.IsAccountCompleted(actualAccountId))
        {
            skippedAccounts++;
            Console.WriteLine($"Account: {actualAccount.Name} — already imported, skipping.");
            Console.WriteLine();
            continue;
        }

        var transactions = reader.GetTransactions(actualAccountId, allActualAccountIds);

        Console.WriteLine($"Account: {actualAccount.Name}");
        Console.WriteLine($"  Transactions found: {transactions.Count}");

        if (transactions.Count == 0)
        {
            progress.MarkCompleted(actualAccountId, actualAccount.Name, fireflyAccountId, 0, 0, 0);
            Console.WriteLine("  Nothing to import.");
            Console.WriteLine();
            continue;
        }

        if (dryRun)
        {
            Console.WriteLine("  [DRY RUN] Would import:");
            var sample = transactions.Take(5);
            foreach (var tx in sample)
            {
                Console.WriteLine($"    {tx.Date:yyyy-MM-dd}  {tx.Amount,10:C}  {tx.Name}");
            }
            if (transactions.Count > 5)
                Console.WriteLine($"    ... and {transactions.Count - 5} more");
            Console.WriteLine();
            continue;
        }

        var accountErrors = 0;
        var previousErrorCount = errorCount;

        try
        {
            var imported = await fireflyService.ImportTransactionsAsync(
                fireflyAccountId, transactions, errorOnDuplicate);

            accountErrors = errorCount - previousErrorCount;
            Console.WriteLine($"  Imported: {imported} transaction(s)");
            totalImported += imported;

            // Mark completed (even with some errors — those transactions were individually skipped)
            progress.MarkCompleted(actualAccountId, actualAccount.Name, fireflyAccountId,
                transactions.Count, imported, accountErrors);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  ERROR importing: {ex.Message}");
            Console.Error.WriteLine("  This account was NOT marked complete — it will be retried on next run.");
        }

        Console.WriteLine();
    }

    if (skippedAccounts > 0)
        Console.WriteLine($"Skipped {skippedAccounts} already-completed account(s) (delete {Path.GetFileName(progressPath)} to re-import all).");

    Console.WriteLine($"Done. Total transactions imported: {totalImported}, errors: {errorCount}");
});

return await rootCommand.InvokeAsync(args);
