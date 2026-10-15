using System;
using System.Linq;
using System.Reflection;
using WhiskeyRealism.Telemetry;

static class Program
{
    internal static int Main(string[] args)
    {
        if (args != null && args.Length > 0)
        {
            if (args[0] != "--validate-telemetry" || args.Length != 2)
            {
                Console.Error.WriteLine("usage: --validate-telemetry <session-dir>");
                return 1;
            }

            var result = TelemetrySessionValidator.ValidateDirectory(args[1]);
            Console.WriteLine(result.Summary);
            return result.Success ? 0 : 1;
        }

        foreach (MethodInfo run in DiscoverTestClasses())
            ((Action)Delegate.CreateDelegate(typeof(Action), run))();

        return 0;
    }

    // Every top-level static class named *Tests with `public static void Run()`,
    // in ordinal name order. A class that matches the name but not the Run
    // signature is an error, not a silent skip.
    internal static MethodInfo[] DiscoverTestClasses()
    {
        Type[] types;
        try
        {
            types = typeof(Program).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            string loaderErrors = string.Join("; ", ex.LoaderExceptions.Where(e => e != null).Select(e => e.Message));
            throw new InvalidOperationException("test discovery could not load types: " + loaderErrors, ex);
        }

        return types
            .Where(t => !t.IsNested && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Tests", StringComparison.Ordinal))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t =>
            {
                MethodInfo run = t.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (run == null || run.ReturnType != typeof(void))
                    throw new InvalidOperationException(t.FullName + " is named *Tests but has no public static void Run()");
                return run;
            })
            .ToArray();
    }
}
