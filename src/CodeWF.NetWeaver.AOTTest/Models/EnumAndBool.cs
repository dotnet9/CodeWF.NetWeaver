namespace CodeWF.NetWeaver.AOTTest.Models;

public class EnumAndBool
{
    public string Name { get; set; }
    public bool Flag { get; set; }
    public SampleEnum Kind { get; set; }
}

public enum SampleEnum
{
    FirstValue = 1,
    SecondValue = 2,
    ThirdValue = 3
}
