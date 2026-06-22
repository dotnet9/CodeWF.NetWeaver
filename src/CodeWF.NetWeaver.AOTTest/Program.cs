namespace CodeWF.NetWeaver.AOTTest;

internal class Program
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Project))]
    private static void Main(string[] args)
    {
        Test.TestSerialize();
        Test.TestAOT();

        Console.ReadLine();
    }
}
