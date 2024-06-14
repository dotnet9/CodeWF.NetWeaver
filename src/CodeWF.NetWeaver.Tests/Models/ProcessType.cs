using System.ComponentModel;

namespace CodeWF.NetWeaver.Tests.Models
{
    public enum ProcessType
    {
        [Description("Application")] Application,
        [Description("Background Process")] BackgroundProcess
    }
}