using CodeWF.NetWeaver.AOTTest.Dto;
using System.Collections;
using CodeWF.NetWeaver.AOTTest.Models;
using CodeWF.Tools.Extensions;

namespace CodeWF.NetWeaver.AOTTest;

public static class Test
{
    public static void TestSerialize()
    {
        var netObject = new ResponseProcessList
        {
            TaskId = 3,
            TotalSize = 200,
            PageSize = 3,
            PageCount = 67,
            PageIndex = 1,
            Processes = new List<ProcessItem>()
        };

        var buffer1 = netObject.Serialize(32);
        var serilizeTime = DateTimeOffset.UtcNow;
        var buffer2 = netObject.Serialize(32, serilizeTime);

        var readIndex = 0;
        buffer1.ReadHead(ref readIndex, out var header1);
        
        readIndex = 0;
        buffer2.ReadHead(ref readIndex, out var header2);
        var dt = header2.UnixTimeMilliseconds.FromUnixTimeMilliseconds();
        Console.WriteLine($"Old: {serilizeTime.LocalDateTime:yyyy-MM-dd HH:mm:ss fff}, New: {dt:yyyy-MM-dd HH:mm:ss fff}");
    }
    public static void TestAOT()
    {
        Console.WriteLine("===1、AOT Array====");
        AOTArray();

        Console.WriteLine("\r\n\r\n===2、AOT List===");
        AOTList();

        Console.WriteLine("\r\n\r\n===3、AOT Dictionary<int,int>===");
        AOTDictionary();

        Console.WriteLine("\r\n\r\n===3、AOT Dictionary<stirng,double>===");
        AOTDictionary2();

        Console.WriteLine("\r\n\r\n===4、AOT Custom Object");
        AOTObject();
    }


    private static void AOTArray()
    {
        try
        {
            int[] arr = [1, 3, 4];
            var type = arr.GetType();

            Console.WriteLine($"直接获取数组长度：{arr.Length}");

            var arrLen1 = type.GetProperty(nameof(Array.Length))?.GetValue(arr);
            Console.WriteLine($"反射获取数组长度：{arrLen1}");

            Array arrObj = arr;
            var arrLen2 = arrObj.Length;
            Console.WriteLine($"转Array获取数组长度：{arrLen2}");

            var elementType = type.GetElementType();
            var newObj = Array.CreateInstance(elementType, arrLen2);
            Console.WriteLine($"{newObj}");

            Console.WriteLine("=========================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{nameof(AOTArray)}:\r\n{ex}");
        }
    }

    private static void AOTList()
    {
        try
        {
            var lst = new List<int> { 1, 2, 3, 4 };
            var type = lst.GetType();

            Console.WriteLine($"直接获取List长度：{lst.Count}");

            var listLen1 = type.GetProperty(nameof(IList.Count))?.GetValue(lst);
            Console.WriteLine($"反射获取List长度：{listLen1}");

            var listObj = lst as IList;
            var listLen2 = listObj.Count;
            Console.WriteLine($"转IList获取List长度：{listLen2}");

            var newObj1 = CreateInstance(lst);
            Console.WriteLine($"ins1: {newObj1}");

            var newObj2 = Activator.CreateInstance(type);
            Console.WriteLine($"ins2:{newObj2}");

            Console.WriteLine("=========================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{nameof(AOTList)}:\r\n{ex}");
        }
    }

    private static void AOTDictionary()
    {
        try
        {
            var dict = new Dictionary<int, int>() { { 1, 2 }, { 3, 4 }, { 5, 6 }, { 7, 8 } };
            var type = dict.GetType();

            Console.WriteLine($"直接获取Dictionary长度：{dict.Count}");

            var dictLen1 = type.GetProperty(nameof(IDictionary.Count))?.GetValue(dict);
            Console.WriteLine($"反射获取Dictionary长度：{dictLen1}");

            var dictObj = dict as IDictionary;
            var dictLen2 = dictObj.Count;
            Console.WriteLine($"反射获取Dictionary长度：{dictLen2}");

            var newObj1 = CreateInstance(dict);
            Console.WriteLine($"ins1: {newObj1}");

            var newObj = Activator.CreateInstance(type);
            Console.WriteLine($"ins2: {newObj}");

            Console.WriteLine("=========================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{nameof(AOTDictionary)}:\r\n{ex}");
        }
    }
    private static void AOTDictionary2()
    {
        try
        {
            var dict = new Dictionary<string, double>() { { "1", 2 }, { "3", 4 }, { "5", 6 }, { "7", 8 } };
            var type = dict.GetType();

            Console.WriteLine($"直接获取Dictionary长度：{dict.Count}");

            var dictLen1 = type.GetProperty(nameof(IDictionary.Count))?.GetValue(dict);
            Console.WriteLine($"反射获取Dictionary长度：{dictLen1}");

            var dictObj = dict as IDictionary;
            var dictLen2 = dictObj.Count;
            Console.WriteLine($"反射获取Dictionary长度：{dictLen2}");

            var newObj1 = CreateInstance(dict);
            Console.WriteLine($"ins1: {newObj1}");

            var newObj = Activator.CreateInstance(type);
            Console.WriteLine($"ins2: {newObj}");

            Console.WriteLine("=========================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{nameof(AOTDictionary)}:\r\n{ex}");
        }
    }

    private static void AOTObject()
    {
        try
        {
            var person = new PersonDto()
            {
                Name = "NetWeaver",
                Tags = [1, 2, 3],
                Addresses = ["四川", "成都"],
                Projects = [new Project() { Id = 1, Name = "Math" }, new Project() { Id = 2, Name = "Chinese" }],
                Records = new() { { 1, 98 }, { 2, 99 } },
                Course = new() { { "Math", 98.5 }, { "Chinese", 99.5 } }
            };
            var buffer = person.SerializeObject();
            var newPerson = buffer.DeserializeObject(typeof(PersonDto)) as PersonDto;
            Console.WriteLine($"名【{person.Name}】=》【{newPerson?.Name}】");
            Console.WriteLine($"Tags【{person.Tags.Length}】=》【{newPerson.Tags?.Length}】");
            Console.WriteLine($"Addresses【{person.Addresses.Length}】=》【{newPerson.Addresses?.Length}】");
            Console.WriteLine($"Projects【{person.Projects.Count}】=》【{newPerson.Projects?.Count}】");
            Console.WriteLine($"Records【{person.Records.Count}】=》【{newPerson.Records?.Count}】");
            Console.WriteLine($"Course【{person.Course.Count}】=》【{newPerson.Course?.Count}】");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{nameof(AOTObject)}:\r\n{ex}");
        }
    }

    private static object CreateInstance(object val)
    {
        var type = val.GetType();
        var itemTypes = type.GetGenericArguments();
        if (val is IList)
        {
            var lstType = typeof(List<>);
            var genericType = lstType.MakeGenericType(itemTypes.First());
            return Activator.CreateInstance(genericType)!;
        }
        else
        {
            var dictType = typeof(Dictionary<,>);
            var genericType = dictType.MakeGenericType(itemTypes.First(), itemTypes[1]);
            return Activator.CreateInstance(genericType)!;
        }
    }
}