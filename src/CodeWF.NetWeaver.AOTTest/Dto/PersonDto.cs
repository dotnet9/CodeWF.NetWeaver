namespace CodeWF.NetWeaver.AOTTest.Dto;

public class PersonDto
{
    public string? Name { get; set; }

    public int[]? Tags { get; set; }

    public string[]? Addresses { get; set; }

    public List<Project>? Projects { get; set; }

    public Dictionary<int, int>? Records { get; set; }

    public Dictionary<string, double>? Course { get; set; }
}

public class Project
{
    public Project()
    {
    }

    public int Id { get; set; }
    public string? Name { get; set; }
}