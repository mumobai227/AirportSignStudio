using System;
using System.Linq;

static class ModernTestRunner
{
    [STAThread] static int Main(string[] args)
    {
        return args[0] == "--ui" ? VerifyDpi.Main(args.Skip(1).ToArray()) : Verify.Main(args);
    }
}
