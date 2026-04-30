using Agent.Models;
using Agent.Tools;

namespace Agent;

public class Agent
{
    private readonly LlmClient _llmClient;
    private readonly Dictionary<string, ITool> _tools;
    private readonly AgentState _state;

    public Agent(string goal)
    {
        _llmClient = new LlmClient();
        _state = new AgentState { Goal = goal };
        _tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase)
        {
            { "get_complaints", new GetComplaintsTool() }
        };
    }

    public void Run()
    {
        Console.WriteLine($"Agent starting with goal: {_state.Goal}");
        Console.WriteLine(new string('-', 50));

        while (!_state.IsFinished)
        {
            Decision decision = _llmClient.GetNextDecision(_state);

            Console.WriteLine($"[Decision] Action: {decision.Action}");
            if (decision.Input is not null)
                Console.WriteLine($"           Input:  {decision.Input}");

            _state.History.Add($"Decision: action={decision.Action}, input={decision.Input}");

            if (decision.Action == "finish")
            {
                Console.WriteLine(new string('-', 50));
                Console.WriteLine("[Result] " + decision.Output);
                _state.IsFinished = true;
            }
            else if (_tools.TryGetValue(decision.Action, out ITool? tool))
            {
                Console.WriteLine($"[Tool] Executing '{tool.Name}' with input: {decision.Input}");

                string result = tool.Execute(decision.Input ?? string.Empty);

                Console.WriteLine($"[Tool Result]\n{result}");
                _state.History.Add($"Tool result: {result}");
            }
            else
            {
                Console.WriteLine($"[Warning] Unknown action '{decision.Action}'. Stopping.");
                _state.IsFinished = true;
            }

            Console.WriteLine(new string('-', 50));
        }
    }
}
