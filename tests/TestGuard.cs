/// <summary>Catch setup, assertions and cleanup errors before they can reach Windows Error Reporting.</summary>
internal static class TestGuard
{
    internal static int Run(string[] arguments, System.Func<int> checks)
    {
        try
        {
            if (System.Array.IndexOf(arguments, "--qa-verify-failure") >= 0)
                throw new System.InvalidOperationException("Intentional QA failure-boundary verification.");
            return checks();
        }
        catch (System.Exception error)
        {
            System.Console.Error.WriteLine("QA FAILED (caught): " + error);
            return 1;
        }
    }
}
