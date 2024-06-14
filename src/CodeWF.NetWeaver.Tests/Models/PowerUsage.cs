using System.ComponentModel;

namespace CodeWF.NetWeaver.Tests.Models
{
    public enum PowerUsage
    {
        [Description("Very low")] VeryLow,
        [Description("Low")] Low,
        [Description("Moderate")] Moderate,
        [Description("High")] High,
        [Description("Very high")] VeryHigh
    }
}
