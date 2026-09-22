namespace CodeWF.NetWeaver.AOTTest;

internal class Program
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Project))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<double>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<ProcessItem>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<Project>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<int, int>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<string, double>))]
    private static void Main(string[] args)
    {
        Test.TestSerialize();
        Test.TestAOT();

        Console.ReadLine();
    }
}
