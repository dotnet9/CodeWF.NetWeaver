using System.Diagnostics.CodeAnalysis;
using CodeWF.NetWeaver.AOTTest.Dto;

namespace CodeWF.NetWeaver.AOTTest;

internal class Program
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Project))]
    static void Main(string[] args)
    {
        Test.TestAOT();

        Console.ReadLine();
    }
}