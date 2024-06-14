using System.ComponentModel;

namespace CodeWF.NetWeaver.Tests.Models
{
    [Flags]
    public enum AlarmStatus
    {
        [Description("Normal")] Normal = 0,
        [Description("Overtime")] Overtime = 1,
        [Description("OverLimit")] OverLimit = 2,
        [Description("User Changed")] UserChanged = 4
    }
}