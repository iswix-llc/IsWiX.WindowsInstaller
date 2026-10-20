using IsWiX.WindowsInstaller;

namespace ConsoleApp1
{
    internal class Program
    {
        static void Main(string[] args)
        {
            using (Database database = new Database(@"D:\fusion-1.0.5.msi", DatabaseOpenMode.ReadOnly))
            {
                using (View view = database.OpenView("SELECT * FROM Property"))
                {
                    string author = database.SummaryInfo.Author;

                    view.Execute();
                    Record record;
                    while ((record = view.Fetch()) != null)
                    {
                        string name = record.GetString("Property");
                        string data = record.GetString("Value");
                        Console.WriteLine($"{name} : {data}");
                    }
                }
            }
        }
    }
}
