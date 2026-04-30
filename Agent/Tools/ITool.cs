namespace Agent.Tools;

public interface ITool
{
    string Name { get; }
    string Execute(string input);
}
