using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>Tests of SQL views run through the managed query engine.</summary>
public class ViewTests
{
    [Fact]
    public void ParameterMarkerIsBoundFromRecord()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));
        using var view = db.OpenView("SELECT `Value` FROM `Property` WHERE `Property` = ?");

        view.Execute(new Record("ProductName"));
        using Record? row = view.Fetch();

        Assert.NotNull(row);
        Assert.Equal("Contoso Widget", row!.GetString(1));
        Assert.Null(view.Fetch());
    }

    [Fact]
    public void ParameterMarkerWithoutParametersIsRejected()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));
        using var view = db.OpenView("SELECT `Value` FROM `Property` WHERE `Property` = ?");

        Assert.ThrowsAny<ArgumentException>(() => view.Execute());
    }

    [Fact]
    public void OrderByOrdersStrings()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));
        using var view = db.OpenView("SELECT `File` FROM `File` ORDER BY `File`");

        view.Execute();
        var names = new List<string>();
        Record? row;
        while ((row = view.Fetch()) != null)
        {
            using (row) names.Add(row.GetString(1));
        }

        Assert.True(names.Count >= 2);
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
    }

    [Fact]
    public void IsNotNullKeepsRowsWithData()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));
        using var view = db.OpenView("SELECT `Name` FROM `Binary` WHERE `Data` IS NOT NULL");

        view.Execute();
        int count = 0;
        Record? row;
        while ((row = view.Fetch()) != null)
        {
            using (row) count++;
        }

        Assert.Equal(db.CountRows("Binary"), count);
    }

    [Fact]
    public void JoinWithoutConditionIsCrossProduct()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));
        using var view = db.OpenView("SELECT `Binary`.`Name`, `Property`.`Value` FROM `Binary`, `Property`");

        view.Execute();
        int count = 0;
        Record? row;
        while ((row = view.Fetch()) != null)
        {
            using (row) count++;
        }

        Assert.Equal(db.CountRows("Binary") * db.CountRows("Property"), count);
    }

    [Fact]
    public void DistinctRemovesDuplicateRows()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));
        // Every combination of Binary and Property has the same Binary name, so DISTINCT keeps one.
        using var view = db.OpenView("SELECT DISTINCT `Binary`.`Name` FROM `Binary`, `Property`");

        view.Execute();
        int count = 0;
        Record? row;
        while ((row = view.Fetch()) != null)
        {
            using (row) count++;
        }

        Assert.Equal(1, count);
    }

    [Fact]
    public void TableColumnsAreReadBeforeAnyExecute()
    {
        // TableInfo reads column names from an unexecuted view, so this exercises that path.
        using var db = new Database(TestFixtures.Path("basic.msi"));

        Assert.Equal(new[] { "Property", "Value" }, db.Tables["Property"].Columns.Select(c => c.Name));
    }

    [Fact]
    public void SyntaxErrorIsReported()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));

        Assert.ThrowsAny<BadQuerySyntaxException>(() => db.OpenView("SELEC * FROM `Property`"));
    }

    [Fact]
    public void UnknownTableIsReported()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));

        Assert.ThrowsAny<BadQuerySyntaxException>(() => db.OpenView("SELECT * FROM `NoSuchTable`"));
    }

    [Fact]
    public void UnknownColumnIsReported()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));

        Assert.ThrowsAny<BadQuerySyntaxException>(() => db.OpenView("SELECT `NoSuchColumn` FROM `Property`"));
    }

    [Fact]
    public void StringOrderingIsRejected()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));

        Assert.ThrowsAny<BadQuerySyntaxException>(() =>
        {
            using var view = db.OpenView("SELECT * FROM `Property` WHERE `Property` < 'a'");
            view.Execute();
        });
    }

    [Fact]
    public void InsertOpensButCannotExecuteOnReadOnlyDatabase()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));
        using View view = db.OpenView("INSERT INTO `Property` (`Property`, `Value`) VALUES ('a', 'b')");

        Assert.ThrowsAny<InstallerException>(() => view.Execute());
    }
}
