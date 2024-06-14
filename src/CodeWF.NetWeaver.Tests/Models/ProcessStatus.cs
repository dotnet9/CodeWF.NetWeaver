using System.ComponentModel;

namespace CodeWF.NetWeaver.Tests.Models
{
    public enum ProcessStatus
    {
        [Description("New")] New,
        [Description("Ready")] Ready,
        [Description("Running")] Running,
        [Description("Blocked")] Blocked,
        [Description("Terminated")] Terminated
    }
}