using System.ComponentModel;

namespace CodeWF.NetWeaver.Tests.Models
{
    /// <summary>
    ///     GPU引擎
    /// </summary>
    public enum GpuEngine
    {
        [Description("无")] None,
        [Description("GPU 0 - 3D")] Gpu03D
    }
}