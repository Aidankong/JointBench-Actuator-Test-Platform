using System.Runtime.InteropServices;

namespace JointBench.TwinCat;

public sealed class AutomationProbe
{
    public AutomationSmokeResult Smoke(string progId = "TcXaeShell.DTE.15.0", string? solutionPath = null)
    {
        object? dte = null;
        try
        {
            dte = ComAutomation.Create(progId);

            ComAutomation.TrySet(dte, "SuppressUI", true);
            ComAutomation.TrySet(dte, "UserControl", false);

            var name = Convert.ToString(ComAutomation.Get(dte, "Name")) ?? string.Empty;
            var version = Convert.ToString(ComAutomation.Get(dte, "Version")) ?? string.Empty;
            string? openedSolution = null;

            if (!string.IsNullOrWhiteSpace(solutionPath))
            {
                if (!File.Exists(solutionPath))
                {
                    throw new FileNotFoundException("Solution file not found.", solutionPath);
                }

                var solution = ComAutomation.Get(dte, "Solution");
                ComAutomation.Invoke(solution, "Open", solutionPath);
                openedSolution = Convert.ToString(ComAutomation.Get(solution, "FullName"));
            }

            return new AutomationSmokeResult(progId, true, name, version, openedSolution, string.Empty);
        }
        catch (Exception exc)
        {
            return new AutomationSmokeResult(progId, false, string.Empty, string.Empty, null, exc.Message);
        }
        finally
        {
            if (dte is not null)
            {
                try
                {
                    var solution = ComAutomation.Get(dte, "Solution");
                    ComAutomation.Invoke(solution, "Close", false);
                }
                catch
                {
                    // Some hosts do not have an open solution or reject Close during startup.
                }

                try
                {
                    ComAutomation.Invoke(dte, "Quit");
                }
                catch
                {
                    // The smoke result is about automation availability, not shutdown cleanup.
                }

                try
                {
                    Marshal.FinalReleaseComObject(dte);
                }
                catch
                {
                    // Ignore COM cleanup errors from already-released instances.
                }
            }
        }
    }

}
