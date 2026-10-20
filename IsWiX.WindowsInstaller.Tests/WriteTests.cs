using MsiReader;
using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>Tests of INSERT, UPDATE, DELETE, view modification and commit. Each test works on its own copy of basic.msi.</summary>
public class WriteTests : IDisposable
{
    private readonly string folder = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));

    public WriteTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    private string CopyOfFixture()
    {
        string copy = Path.Combine(folder, "basic.msi");
        File.Copy(TestFixtures.Path("basic.msi"), copy);
        return copy;
    }

    private static List<string> PropertyNames(Database db)
    {
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");
        view.Execute();
        var names = new List<string>();
        for (Record? row = view.Fetch(); row != null; row = view.Fetch())
        {
            names.Add(row.GetString(1) + "=" + (row.IsNull(2) ? "NULL" : row.GetString(2)));
        }
        return names;
    }

    [Fact]
    public void InsertAddsRowVisibleToSelect()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View insert = db.OpenView("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)");
        insert.Execute(new Record("Added", "new value"));

        Assert.Contains("Added=new value", PropertyNames(db));
    }

    [Fact]
    public void InsertWithLiteralValues()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View insert = db.OpenView("INSERT INTO `Property` (`Property`, `Value`) VALUES ('Literal', 'text')");
        insert.Execute();

        Assert.Contains("Literal=text", PropertyNames(db));
    }

    [Fact]
    public void DuplicatePrimaryKeyIsRejected()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View insert = db.OpenView("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)");

        Assert.ThrowsAny<InstallerException>(() => insert.Execute(new Record("ProductName", "again")));
        Assert.Contains("ProductName=Contoso Widget", PropertyNames(db));
    }

    [Fact]
    public void UpdateChangesMatchingRows()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View update = db.OpenView("UPDATE `Property` SET `Value` = ? WHERE `Property` = ?");
        update.Execute(new Record("Renamed", "ProductName"));

        var names = PropertyNames(db);
        Assert.Contains("ProductName=Renamed", names);
        Assert.DoesNotContain("ProductName=Contoso Widget", names);
    }

    [Fact]
    public void DeleteRemovesMatchingRows()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View delete = db.OpenView("DELETE FROM `Property` WHERE `Property` = ?");
        delete.Execute(new Record("ProductName"));

        Assert.DoesNotContain(PropertyNames(db), n => n.StartsWith("ProductName="));
    }

    [Fact]
    public void WritesOnReadOnlyDatabaseFail()
    {
        using var db = new Database(CopyOfFixture());
        using View insert = db.OpenView("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)");

        Assert.ThrowsAny<InstallerException>(() => insert.Execute(new Record("X", "Y")));
    }

    [Fact]
    public void UncommittedChangesAreDiscarded()
    {
        string path = CopyOfFixture();
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        using (View insert = db.OpenView("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)"))
        {
            insert.Execute(new Record("Temp", "temp"));
        }

        using var reopened = new Database(path);
        Assert.DoesNotContain(PropertyNames(reopened), n => n.StartsWith("Temp="));
    }

    [Fact]
    public void CommitPersistsChangesAndKeepsBinaryAndPlainStreams()
    {
        string path = CopyOfFixture();
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            using View insert = db.OpenView("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)");
            insert.Execute(new Record("Saved", "yes"));
            db.Commit();
        }

        using var reopened = new Database(path);
        Assert.Contains("Saved=yes", PropertyNames(reopened));

        using var reader = MsiDatabase.Open(path);
        Assert.NotNull(reader.ReadStream("Binary.hello"));
        Assert.Equal(4, reader.GetTable("Property").Rows.Count);
    }

    [Fact]
    public void ImportReplacesTableAndTakesBinaryFromFilesBesideIdt()
    {
        string path = CopyOfFixture();
        string idt = Path.Combine(folder, "Binary.idt");
        File.WriteAllText(idt, "Name\tData\r\ns72\tv0\r\nBinary\tName\r\nhello\tBinary.hello\r\ncopy\tBinary.copy\r\n");
        File.Copy(TestFixtures.Path("hello.bin"), Path.Combine(folder, "Binary.hello"));
        File.WriteAllBytes(Path.Combine(folder, "Binary.copy"), new byte[] { 1, 2, 3 });

        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            db.Import(idt);
            db.Commit();
        }

        using var reader = MsiDatabase.Open(path);
        Assert.Equal(File.ReadAllBytes(TestFixtures.Path("hello.bin")), reader.ReadStream("Binary.hello"));
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.ReadStream("Binary.copy"));
        Assert.Equal(2, reader.GetTable("Binary").Rows.Count);
    }

    [Fact]
    public void CodePageSetterIsWrittenOnCommit()
    {
        string path = CopyOfFixture();
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            db.CodePage = 1251;
            Assert.Equal(1251, db.CodePage);
            db.Commit();
        }

        using var reopened = new Database(path);
        Assert.Equal(1251, reopened.CodePage);
    }

    [Fact]
    public void DirectModeWritesWithoutCommit()
    {
        string path = CopyOfFixture();
        using (var db = new Database(path, DatabaseOpenMode.Direct))
        using (View insert = db.OpenView("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)"))
        {
            insert.Execute(new Record("Direct", "written"));
        }

        using var reopened = new Database(path);
        Assert.Contains("Direct=written", PropertyNames(reopened));
    }

    [Fact]
    public void ViewInsertAndDeleteUseTheRecord()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");

        view.Insert(new Record("ViaView", "record"));
        Assert.Contains("ViaView=record", PropertyNames(db));

        view.Delete(new Record("ViaView", "record"));
        Assert.DoesNotContain(PropertyNames(db), n => n.StartsWith("ViaView="));
    }

    [Fact]
    public void ViewAssignInsertsThenUpdates()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");

        view.Assign(new Record("Assigned", "first"));
        view.Assign(new Record("Assigned", "second"));

        var names = PropertyNames(db);
        Assert.Contains("Assigned=second", names);
        Assert.DoesNotContain("Assigned=first", names);
    }

    [Fact]
    public void ViewRefreshFillsRecordFromRowWithSameKey()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");
        view.Assign(new Record("Refreshed", "value"));

        using var record = new Record(2);
        record.SetString(1, "Refreshed");
        view.Refresh(record);

        Assert.Equal("value", record.GetString(2));
    }

    [Fact]
    public void ViewSeekFillsRecordFromRowWithSameKey()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");
        view.Assign(new Record("Sought", "found"));

        using var record = new Record(2);
        record.SetString(1, "Sought");
        view.Modify(ViewModifyMode.Seek, record);

        Assert.Equal("found", record.GetString(2));
    }

    [Fact]
    public void ViewRefreshOfMissingKeyFails()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");

        using var record = new Record(2);
        record.SetString(1, "NoSuchProperty");
        Assert.ThrowsAny<InstallerException>(() => view.Refresh(record));
    }

    [Fact]
    public void ViewValidateNewRejectsExistingKeyAndWritesNothing()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");

        view.Modify(ViewModifyMode.ValidateNew, new Record("Brand", "new"));
        Assert.DoesNotContain("Brand=new", PropertyNames(db));
        Assert.ThrowsAny<InstallerException>(() => view.Modify(ViewModifyMode.ValidateNew, new Record("ProductName", "x")));
    }

    [Fact]
    public void ViewValidateDeleteRequiresExistingKey()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");

        view.Modify(ViewModifyMode.ValidateDelete, new Record("ProductName", "x"));
        Assert.ThrowsAny<InstallerException>(() => view.Modify(ViewModifyMode.ValidateDelete, new Record("Nope", "x")));
    }

    [Fact]
    public void ViewMergeInsertsNewRowAndAcceptsIdenticalRow()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");
        view.Assign(new Record("Merged", "same"));

        view.Modify(ViewModifyMode.Merge, new Record("Merged", "same"));
        view.Modify(ViewModifyMode.Merge, new Record("Added", "new"));

        var names = PropertyNames(db);
        Assert.Contains("Merged=same", names);
        Assert.Contains("Added=new", names);
    }

    [Fact]
    public void ViewMergeOfConflictingRowFails()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");
        view.Assign(new Record("Merged", "same"));

        Assert.ThrowsAny<InstallerException>(() => view.Modify(ViewModifyMode.Merge, new Record("Merged", "different")));
        Assert.Contains("Merged=same", PropertyNames(db));
    }

    [Fact]
    public void ViewOverFilteredQueryCannotBeModified()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property` WHERE `Property` = 'ProductName'");

        Assert.ThrowsAny<InstallerException>(() => view.Insert(new Record("Nope", "x")));
    }

    [Fact]
    public void KeyLongerThanColumnIsRejected()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View insert = db.OpenView("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)");

        Assert.ThrowsAny<InstallerException>(() => insert.Execute(new Record(new string('k', 73), "x")));
    }

    [Fact]
    public void BinaryValueIsWrittenAsStreamOnCommit()
    {
        string path = CopyOfFixture();
        byte[] payload = { 1, 2, 3, 4, 5 };
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        using (View insert = db.OpenView("INSERT INTO `Binary` (`Name`, `Data`) VALUES (?, ?)"))
        using (var record = new Record("world", null))
        {
            record.SetStream(2, new MemoryStream(payload));
            insert.Execute(record);
            db.Commit();
        }

        using var reader = MsiDatabase.Open(path);
        Assert.True(reader.ReadStream("Binary.world")!.AsSpan().SequenceEqual(payload));
        Assert.Equal(2, reader.GetTable("Binary").Rows.Count);
    }

    [Fact]
    public void BinaryValueReadsBackThroughView()
    {
        string path = CopyOfFixture();
        byte[] payload = { 9, 8, 7 };
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        using (View insert = db.OpenView("INSERT INTO `Binary` (`Name`, `Data`) VALUES (?, ?)"))
        using (var record = new Record("world", null))
        {
            record.SetStream(2, new MemoryStream(payload));
            insert.Execute(record);
            db.Commit();
        }

        using var reopened = new Database(path);
        using View select = reopened.OpenView("SELECT `Data` FROM `Binary` WHERE `Name` = 'world'");
        select.Execute();
        using Record? row = select.Fetch();
        Assert.NotNull(row);
        using var buffer = new MemoryStream();
        row!.GetStream(1).CopyTo(buffer);
        Assert.True(buffer.ToArray().AsSpan().SequenceEqual(payload));
    }

    [Fact]
    public void DeletingBinaryRowRemovesItsStreamOnCommit()
    {
        string path = CopyOfFixture();
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            using View delete = db.OpenView("DELETE FROM `Binary` WHERE `Name` = 'hello'");
            delete.Execute();
            db.Commit();
        }

        using var reader = MsiDatabase.Open(path);
        Assert.Null(reader.ReadStream("Binary.hello"));
        Assert.Equal(0, reader.GetTable("Binary").Rows.Count);
    }

    [Fact]
    public void BinaryReadsStillWorkAfterASecondCommit()
    {
        string path = CopyOfFixture();
        byte[] first = { 1, 1, 2, 3 };
        byte[] second = { 5, 8, 13 };
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            using (View insert = db.OpenView("INSERT INTO `Binary` (`Name`, `Data`) VALUES (?, ?)"))
            using (var record = new Record("first", null))
            {
                record.SetStream(2, new MemoryStream(first));
                insert.Execute(record);
            }
            db.Commit();

            using (View insert = db.OpenView("INSERT INTO `Binary` (`Name`, `Data`) VALUES (?, ?)"))
            using (var record = new Record("second", null))
            {
                record.SetStream(2, new MemoryStream(second));
                insert.Execute(record);
            }
            db.Commit();

            using View select = db.OpenView("SELECT `Data` FROM `Binary` WHERE `Name` = 'first'");
            select.Execute();
            using Record? row = select.Fetch();
            Assert.NotNull(row);
            using var buffer = new MemoryStream();
            row!.GetStream(1).CopyTo(buffer);
            Assert.True(buffer.ToArray().AsSpan().SequenceEqual(first));
        }

        using var reader = MsiDatabase.Open(path);
        Assert.True(reader.ReadStream("Binary.first")!.AsSpan().SequenceEqual(first));
        Assert.True(reader.ReadStream("Binary.second")!.AsSpan().SequenceEqual(second));
    }

    [Fact]
    public void BinaryReadsFollowAPrimaryKeyChangeAcrossCommits()
    {
        string path = CopyOfFixture();
        byte[] payload = { 4, 2, 4, 2 };
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            using (View insert = db.OpenView("INSERT INTO `Binary` (`Name`, `Data`) VALUES (?, ?)"))
            using (var record = new Record("first", null))
            {
                record.SetStream(2, new MemoryStream(payload));
                insert.Execute(record);
            }
            db.Commit();

            using (View update = db.OpenView("UPDATE `Binary` SET `Name` = 'renamed' WHERE `Name` = 'first'"))
            {
                update.Execute();
            }
            db.Commit();

            using View select = db.OpenView("SELECT `Data` FROM `Binary` WHERE `Name` = 'renamed'");
            select.Execute();
            using Record? row = select.Fetch();
            Assert.NotNull(row);
            using var buffer = new MemoryStream();
            row!.GetStream(1).CopyTo(buffer);
            Assert.True(buffer.ToArray().AsSpan().SequenceEqual(payload));
        }

        using var reader = MsiDatabase.Open(path);
        Assert.True(reader.ReadStream("Binary.renamed")!.AsSpan().SequenceEqual(payload));
        Assert.Null(reader.ReadStream("Binary.first"));
    }
}
