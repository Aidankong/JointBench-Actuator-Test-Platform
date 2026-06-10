namespace JointBench.TwinCat;

public static class CiA402StateDiagnosis
{
    public static string Describe(ActuatorState? state)
    {
        if (state is null)
        {
            return "State unavailable.";
        }

        return Describe(
            state.Statusword,
            state.Controlword,
            state.FaultCode,
            state.Enabled,
            state.ModeOfOperationCommand,
            state.ModeOfOperationDisplay);
    }

    public static string Describe(
        int? statusword,
        int? controlword,
        int faultCode = 0,
        bool enabled = false,
        int? modeCommand = null,
        int? modeDisplay = null)
    {
        if (statusword is null)
        {
            return "Statusword unavailable.";
        }

        var status = statusword.Value;
        var control = controlword ?? 0;
        var notes = new List<string>();

        if ((status & 0x0008) != 0 || faultCode != 0)
        {
            notes.Add("Drive fault is active; reset fault only after the hardware cause is removed.");
        }
        else if (enabled || (status & 0x006F) == 0x0027)
        {
            notes.Add("Operation Enabled.");
        }
        else if ((status & 0x006F) == 0x0023 && (control & 0x000F) == 0x000F)
        {
            notes.Add("Switched On but not Operation Enabled; check S-ON, STO, drive-enable DI, servo power, and the Ti5 safety interlock.");
        }
        else if ((status & 0x006F) == 0x0021)
        {
            notes.Add("Ready to switch on; waiting for switch-on command.");
        }
        else if ((status & 0x006F) == 0x0023)
        {
            notes.Add("Switched On; waiting for enable operation command.");
        }
        else if ((status & 0x004F) == 0x0040)
        {
            notes.Add("Switch On Disabled; verify quick-stop, voltage enable, and reset sequence.");
        }
        else
        {
            notes.Add("Unclassified CiA402 state; inspect statusword bits and drive diagnostics.");
        }

        if (modeCommand.HasValue && modeDisplay.HasValue && modeCommand.Value != modeDisplay.Value)
        {
            notes.Add($"Mode command/display mismatch: command={modeCommand.Value}, display={modeDisplay.Value}.");
        }

        return string.Join(" ", notes);
    }
}
