using SimpleAgent;
using SimpleAgent.Tools;

namespace SimpleAgent;

internal static class Program
{
    private static async Task Main()
    {
        const string goal = "Generate complaint summary for April";

        var agent = new Agent(
            new LlmClient(),
            new ITool[]
            {
                new GetComplaintsTool()
            });

        await agent.RunAsync(goal);
    }
}
