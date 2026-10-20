using IsWiX.WindowsInstaller;
using MsiReader;

namespace ConsoleApp1
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //using(Database database = new Database(@"D:\fusion-1.0.5.msi", DatabaseOpenMode.ReadOnly))
            //{
            //    using (View view = database.OpenView("SELECT * FROM Property"))
            //    {
            //        view.Execute();
            //        Record record;
            //        while ((record = view.Fetch()) != null)
            //        {
            //            string name = record.GetString("Property");
            //            string data = record.GetString("Value");
            //            Console.WriteLine($"{name} : {data}");
            //        }
            //    }
            //}
            //using var db = MsiDatabase.Open(@"D:\fusion-1.0.5.msi");
            //var file = db.GetTable("Property");
            //foreach (var row in file.Rows)
            //{
            //    string? name = row.GetString("Property");
            //    string? data = row.GetString("Value");
            //    Console.WriteLine($"{name} : {data}");
            //}
            //db.Dispose();
        }
    }
}
