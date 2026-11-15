using System;
using System.Linq;

static class ModernTestRunner
{
    [STAThread] static int Main(string[] args)
    {
        if (args[0] == "--chart-ocr-check")
        {
            foreach (string path in args.Skip(1))
            {
                string[] rows = AirportSignStudio.StandChartOcr.Read(path, out int review);
                Console.WriteLine($"{System.IO.Path.GetFileName(path)}: {rows.Length} rows, {review} to review");
                foreach (string row in rows) Console.WriteLine(row);
            }
            return 0;
        }
        if (args[0] == "--chart-check")
        {
            var result = AirportSignStudio.BatchSigns.FromPdfs(args.Skip(1));
            string[] ids = result.Rows;
            bool table = ids.Length > 0;
            Console.WriteLine((table ? "Coordinate table" : "No confirmed stands") + ": " + ids.Length);
            foreach (string id in ids) Console.WriteLine(id);
            return 0;
        }
        return args[0] == "--ui" ? VerifyDpi.Main(args.Skip(1).ToArray()) : Verify.Main(args);
    }
}
