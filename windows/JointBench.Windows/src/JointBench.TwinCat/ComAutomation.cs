using System.Runtime.InteropServices;
using System.Reflection;

namespace JointBench.TwinCat;

internal static class ComAutomation
{
    private const int RpcCallRejected = unchecked((int)0x80010001);
    private const int RpcServerCallRetryLater = unchecked((int)0x8001010A);

    public static object Create(string progId)
    {
        var type = Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException($"COM ProgID is not registered: {progId}");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Failed to instantiate COM ProgID: {progId}");
    }

    public static T Retry<T>(Func<T> action, int attempts = 20, int delayMilliseconds = 750)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception exc) when (IsRetryableComBusy(exc) && attempt < attempts)
            {
                Thread.Sleep(delayMilliseconds);
            }
        }

        return action();
    }

    public static void Retry(Action action, int attempts = 20, int delayMilliseconds = 750) =>
        Retry(
            () =>
            {
                action();
                return true;
            },
            attempts,
            delayMilliseconds);

    public static object? Get(object? target, string propertyName)
    {
        if (target is null)
        {
            return null;
        }

        return Retry(() => target.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null));
    }

    public static object? GetIndexed(object? target, string propertyName, params object?[] args)
    {
        if (target is null)
        {
            return null;
        }

        return Retry(() => target.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args));
    }

    public static void TrySet(object target, string propertyName, object value)
    {
        try
        {
            Retry(() => target.GetType().InvokeMember(
                propertyName,
                BindingFlags.SetProperty,
                binder: null,
                target,
                args: [value]));
        }
        catch
        {
            // Some DTE hosts expose these properties as read-only during startup.
        }
    }

    public static object? Invoke(object? target, string methodName, params object?[] args)
    {
        if (target is null)
        {
            return null;
        }

        return Retry(() => target.GetType().InvokeMember(
            methodName,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args));
    }

    private static bool IsRetryableComBusy(Exception exc)
    {
        if (exc is COMException comException)
        {
            return comException.HResult is RpcCallRejected or RpcServerCallRetryLater;
        }

        return exc is TargetInvocationException { InnerException: COMException inner } &&
            inner.HResult is RpcCallRejected or RpcServerCallRetryLater;
    }
}
