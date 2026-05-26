using System.Runtime.InteropServices;

namespace JointBench.TwinCat;

public sealed class AutomationProbe
{
    public AutomationSmokeResult Smoke(string progId = "TcXaeShell.DTE.15.0", string? solutionPath = null)
    {
        object? dte = null;
        try
        {
            var type = Type.GetTypeFromProgID(progId)
                ?? throw new InvalidOperationException($"COM ProgID is not registered: {progId}");
            dte = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Failed to instantiate COM ProgID: {progId}");

            TrySet(dte, "SuppressUI", true);
            TrySet(dte, "UserControl", false);

            var name = Convert.ToString(Get(dte, "Name")) ?? string.Empty;
            var version = Convert.ToString(Get(dte, "Version")) ?? string.Empty;
            string? openedSolution = null;

            if (!string.IsNullOrWhiteSpace(solutionPath))
            {
                if (!File.Exists(solutionPath))
                {
                    throw new FileNotFoundException("Solution file not found.", solutionPath);
                }

                var solution = Get(dte, "Solution");
                Invoke(solution, "Open", solutionPath);
                openedSolution = Convert.ToString(Get(solution, "FullName"));
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
                    var solution = Get(dte, "Solution");
                    Invoke(solution, "Close", false);
                }
                catch
                {
                    // Some hosts do not have an open solution or reject Close during startup.
                }

                try
                {
                    Invoke(dte, "Quit");
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

    private static object? Get(object? target, string propertyName)
    {
        if (target is null)
        {
            return null;
        }

        return target.GetType().InvokeMember(
            propertyName,
            System.Reflection.BindingFlags.GetProperty,
            binder: null,
            target,
            args: null);
    }

    private static void TrySet(object target, string propertyName, object value)
    {
        try
        {
            target.GetType().InvokeMember(
                propertyName,
                System.Reflection.BindingFlags.SetProperty,
                binder: null,
                target,
                args: [value]);
        }
        catch
        {
            // Some DTE hosts expose these properties as read-only in particular startup states.
        }
    }

    private static object? Invoke(object? target, string methodName, params object[] args)
    {
        if (target is null)
        {
            return null;
        }

        return target.GetType().InvokeMember(
            methodName,
            System.Reflection.BindingFlags.InvokeMethod,
            binder: null,
            target,
            args);
    }
}
