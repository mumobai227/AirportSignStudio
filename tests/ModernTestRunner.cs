using System;
using System.Linq;

static class ModernTestRunner
{
    [STAThread] static int Main(string[] args)
    {
        if (args[0] == "--chart-check")
        {
            string[] ids = AirportSignStudio.BatchSigns.FromPdfs(args.Skip(1), out bool table);
            Console.WriteLine((table ? "Coordinate table" : "Chart candidates") + ": " + ids.Length);
            foreach (string id in ids) Console.WriteLine(id);
            return 0;
        }
        return args[0] == "--ui" ? VerifyDpi.Main(args.Skip(1).ToArray()) : Verify.Main(args);
    }
}
