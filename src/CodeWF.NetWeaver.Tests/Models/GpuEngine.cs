using System.ComponentModel;

namespace CodeWF.NetWeaver.Tests.Models
{
    public enum GpuEngine
    {
        [Description("None")] None,
        [Description("GPU 0 - 3D")] Gpu03D
    }
}