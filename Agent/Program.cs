using SimpleAgent;
using SimpleAgent.Models;
using SimpleAgent.Tools;

namespace SimpleAgent;

internal static class Program
{
    private static async Task Main()
    {
        try
        {
            const string goal = "Generate complaint summary for April";

            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine(ErrorResult.Create("OPENAI_API_KEY is required"));
                return;
            }

            using var httpClient = new HttpClient();

            var agent = new Agent(
                new LlmClient(httpClient, apiKey),
                new ITool[]
                {
                    new GetComplaintsTool(),
                    new FormatReportTool()
                });

            await agent.RunAsync(goal);
        }
        catch (Exception exception)
        {
            Console.WriteLine(ErrorResult.Create(exception.Message));
        }
    }
}
