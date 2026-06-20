using FiiiAssist.Services.Csv.Conversion;

namespace FiiiAssist.Core.Tests.Converters;

public class CsvImportPipelineTests
{
    [Fact]
    public void ProcessCsv_SimpleWithdrawal_ProducesCorrectTransaction()
    {
        var config = new CsvImportConfiguration
        {
            HasHeaders = true,
            Delimiter = ',',
            DateFormat = "Y-m-d",
            DefaultAccountId = "1",
            Roles = new()
            {
                [0] = "date_transaction",
                [1] = "description",
                [2] = "amount",
            },
        };

        var csv = "date,description,amount\n2024-03-15,Coffee Shop,-4.50";

        var service = new CsvImportService(config);
        var result = service.ProcessCsv(csv);

        var tx = Assert.Single(result);
        Assert.Equal("withdrawal", tx.Type);
        Assert.Equal(4.50m, tx.Amount);
        Assert.Equal("Coffee Shop", tx.Description);
        Assert.Contains("2024-03-15", tx.Date);
    }

    [Fact]
    public void ProcessCsv_Deposit_ProducesCorrectTransaction()
    {
        var config = new CsvImportConfiguration
        {
            HasHeaders = true,
            Delimiter = ',',
            DateFormat = "Y-m-d",
            DefaultAccountId = "1",
            Roles = new()
            {
                [0] = "date_transaction",
                [1] = "description",
                [2] = "amount",
            },
        };

        var csv = "date,description,amount\n2024-03-15,Salary,2500.00";

        var service = new CsvImportService(config);
        var result = service.ProcessCsv(csv);

        var tx = Assert.Single(result);
        Assert.Equal("deposit", tx.Type);
        Assert.Equal(2500.00m, tx.Amount);
        Assert.Equal("Salary", tx.Description);
    }

    [Fact]
    public void ProcessCsv_DebitCreditColumns_ResolvesAmount()
    {
        var config = new CsvImportConfiguration
        {
            HasHeaders = true,
            Delimiter = ';',
            DateFormat = "Y-m-d",
            DefaultAccountId = "1",
            Roles = new()
            {
                [0] = "date_transaction",
                [1] = "description",
                [2] = "amount_debit",
                [3] = "amount_credit",
            },
        };

        var csv = "date;description;debit;credit\n2024-03-15;Groceries;45.00;";

        var service = new CsvImportService(config);
        var result = service.ProcessCsv(csv);

        var tx = Assert.Single(result);
        Assert.Equal("withdrawal", tx.Type);
        Assert.Equal(45.00m, tx.Amount);
    }

    [Fact]
    public void ProcessCsv_WithDebitCreditIndicator_AppliesModifier()
    {
        var config = new CsvImportConfiguration
        {
            HasHeaders = true,
            Delimiter = ',',
            DateFormat = "Y-m-d",
            DefaultAccountId = "1",
            Roles = new()
            {
                [0] = "date_transaction",
                [1] = "description",
                [2] = "amount",
                [3] = "generic-debit-credit",
            },
        };

        var csv = "date,description,amount,dc\n2024-03-15,Payment,100.00,D";

        var service = new CsvImportService(config);
        var result = service.ProcessCsv(csv);

        var tx = Assert.Single(result);
        Assert.Equal("withdrawal", tx.Type);
        Assert.Equal(100.00m, tx.Amount); // modifier made it negative, then positive enforced
    }

    [Fact]
    public void ProcessCsv_WithTags_MergesTagsCorrectly()
    {
        var config = new CsvImportConfiguration
        {
            HasHeaders = true,
            Delimiter = ',',
            DateFormat = "Y-m-d",
            DefaultAccountId = "1",
            Roles = new()
            {
                [0] = "date_transaction",
                [1] = "description",
                [2] = "amount",
                [3] = "tags-comma",
                [4] = "tags-space",
            },
        };

        var csv = "date,description,amount,tags_c,tags_s\n2024-03-15,Test,-10,food,groceries snack";

        var service = new CsvImportService(config);
        var result = service.ProcessCsv(csv);

        var tx = Assert.Single(result);
        Assert.Contains("food", tx.Tags);
        Assert.Contains("groceries", tx.Tags);
        Assert.Contains("snack", tx.Tags);
    }

    [Fact]
    public void ProcessCsv_DuplicateLines_Removed()
    {
        var config = new CsvImportConfiguration
        {
            HasHeaders = true,
            Delimiter = ',',
            DateFormat = "Y-m-d",
            DefaultAccountId = "1",
            IgnoreDuplicateLines = true,
            Roles = new()
            {
                [0] = "date_transaction",
                [1] = "description",
                [2] = "amount",
            },
        };

        var csv = "date,description,amount\n2024-03-15,Coffee,-4.50\n2024-03-15,Coffee,-4.50";

        var service = new CsvImportService(config);
        var result = service.ProcessCsv(csv);

        Assert.Single(result);
    }

    [Fact]
    public void ProcessCsv_OpposingAccount_SetsDestination()
    {
        var config = new CsvImportConfiguration
        {
            HasHeaders = true,
            Delimiter = ',',
            DateFormat = "Y-m-d",
            DefaultAccountId = "1",
            Roles = new()
            {
                [0] = "date_transaction",
                [1] = "description",
                [2] = "amount",
                [3] = "opposing-name",
            },
        };

        var csv = "date,description,amount,payee\n2024-03-15,Rent,-1200,Landlord Inc";

        var service = new CsvImportService(config);
        var result = service.ProcessCsv(csv);

        var tx = Assert.Single(result);
        Assert.Equal("Landlord Inc", tx.DestinationName);
    }

    [Fact]
    public void ToFireflyTransactions_FiltersZeroAmounts()
    {
        var transactions = new List<PseudoTransaction>
        {
            new() { Amount = 10m, Description = "Valid", Date = "2024-01-01" },
            new() { Amount = 0m, Description = "Zero", Date = "2024-01-01" },
        };

        var result = CsvImportService.ToFireflyTransactions(transactions);

        Assert.Single(result);
        Assert.Equal("Valid", result[0].Description);
    }

    [Fact]
    public void ProcessCsv_EmptyDescription_FallsBackToDefault()
    {
        var config = new CsvImportConfiguration
        {
            HasHeaders = true,
            Delimiter = ',',
            DateFormat = "Y-m-d",
            DefaultAccountId = "1",
            Roles = new()
            {
                [0] = "date_transaction",
                [1] = "amount",
            },
        };

        var csv = "date,amount\n2024-03-15,-10.00";

        var service = new CsvImportService(config);
        var result = service.ProcessCsv(csv);

        var tx = Assert.Single(result);
        Assert.Equal("(no description)", tx.Description);
    }

    [Fact]
    public void ProcessCsv_EuropeanFormat_ParsesCorrectly()
    {
        var config = new CsvImportConfiguration
        {
            HasHeaders = true,
            Delimiter = ';',
            DateFormat = "d/m/Y",
            DefaultAccountId = "1",
            Roles = new()
            {
                [0] = "date_transaction",
                [1] = "description",
                [2] = "amount",
            },
        };

        var csv = "date;description;amount\n15/03/2024;Kaffee;-1.234,56";

        var service = new CsvImportService(config);
        var result = service.ProcessCsv(csv);

        var tx = Assert.Single(result);
        Assert.Equal(1234.56m, tx.Amount);
        Assert.Contains("2024-03-15", tx.Date);
    }
}
