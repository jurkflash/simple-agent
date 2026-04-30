namespace Agent.Tools;

public class GetComplaintsTool : ITool
{
    public string Name => "get_complaints";

    public string Execute(string input)
    {
        return """
            [
              { "category": "Noise", "count": 12 },
              { "category": "Parking", "count": 8 }
            ]
            """;
    }
}
