using CodeWF.NetWeaver.AOTTest.Dto;
using System.Collections;

namespace CodeWF.NetWeaver.AOTTest;

public static class Test
{
    public static void TestAOT()
    {
        Console.WriteLine("===1、测试AOT获取数组长度====");

        int[] arr = [1, 3, 4];

        Console.WriteLine($"直接获取数组长度：{arr.Length}");

        var arrLen1 = arr.GetType()?.GetProperty(nameof(Array.Length))?.GetValue(arr);
        Console.WriteLine($"反射获取数组长度：{arrLen1}");

        var arrObj = arr as Array;
        var arrLen2 = arrObj.Length;
        Console.WriteLine($"转Array获取数组长度：{arrLen2}");
        Console.WriteLine("=========================");


        Console.WriteLine("===2、测试AOT获取List长度===");

        var lst = new List<int> { 1, 2, 3, 4 };
        Console.WriteLine($"直接获取List长度：{lst.Count}");

        var listLen1 = lst.GetType()?.GetProperty(nameof(IList.Count))?.GetValue(lst);
        Console.WriteLine($"反射获取List长度：{listLen1}");

        var listObj = arr as IList;
        var listLen2 = listObj.Count;
        Console.WriteLine($"转IList获取List长度：{listLen2}");
        Console.WriteLine("=========================");


        Console.WriteLine("===3、测试AOT获取Dictionary长度===");

        var dict = new Dictionary<int, int>() { { 1, 2 }, { 3, 4 }, { 5, 6 }, { 7, 8 } };
        Console.WriteLine($"直接获取Dictionary长度：{dict.Count}");

        var dictLen1 = dict.GetType()?.GetProperty(nameof(IDictionary.Count))?.GetValue(dict);
        Console.WriteLine($"反射获取Dictionary长度：{dictLen1}");

        var dictObj = dict as IDictionary;
        var dictLen2 = dictObj.Count;
        Console.WriteLine($"反射获取Dictionary长度：{dictLen2}");
        Console.WriteLine("=========================");

        Console.WriteLine("===4、测试复杂对象序列化");
        try
        {
            var person = new PersonDto()
            {
                Name = "NetWeaver",
                Tags = [1, 2, 3],
                Addresses = ["四川", "成都"],
                Projects = [new Project() { Id = 1, Name = "Math" }, new Project() { Id = 2, Name = "Chinese" }],
                Records = new() { { 1, 98 }, { 2, 99 } }
            };
            var buffer = person.SerializeObject();
            var newPerson = buffer.DeserializeObject(typeof(PersonDto)) as PersonDto;
            Console.WriteLine($"名【{person.Name}】=》【{newPerson?.Name}】");
            Console.WriteLine($"Tags【{person.Tags.Length}】=》【{newPerson.Tags?.Length}】");
            Console.WriteLine($"Addresses【{person.Addresses.Length}】=》【{newPerson.Addresses?.Length}】");
            Console.WriteLine($"Projects【{person.Projects.Count}】=》【{newPerson.Projects?.Count}】");
            Console.WriteLine($"Records【{person.Records.Count}】=》【{newPerson.Records?.Count}】");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"序列化异常：{ex}");
        }
    }
}