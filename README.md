# simple-agent

`simple-agent` is a small .NET 8 AI agent example that shows how to run an LLM-driven decision loop against a constrained tool surface.

## What this currently covers

- **Agent loop orchestration**: the agent asks the LLM for the next action, validates it, executes the allowed tool, and finishes with a final answer.
- **Constrained actions**: the current tool surface is intentionally small and only supports `get_complaints` and `finish`.
- **Structured tool execution**: the agent executes tools through a CQRS adapter instead of calling handlers directly.
- **Permission checks**: every tool action is checked before execution.
- **Observability and tracing**:
  - correlation ID per agent run
  - structured `ILogger` logging
  - step-level tracing for LLM decisions, validation, CQRS dispatch, and tool results
  - timing for total run duration, step duration, LLM calls, and CQRS execution
  - run summary output with success/failure metadata
- **Layered solution structure**:
  - `Agent`: console host / composition root
  - `SimpleAgent.Domain`: core models
  - `SimpleAgent.Application`: agent orchestration, use-case logic, abstractions
  - `SimpleAgent.Infrastructure`: OpenAI client, CQRS adapter, correlation context, permission service

## Current flow

1. The host creates an agent run with a goal and correlation ID.
2. The agent sends the current goal/history to the LLM.
3. The LLM returns a JSON decision.
4. The decision is parsed and validated against allowed actions.
5. If the action is `get_complaints`, the request is mapped to a CQRS query and dispatched.
6. The tool result is added to history.
7. The agent asks the LLM for the next step until it returns `finish`.

## Project structure

```text
Agent/                      Console entrypoint and DI composition root
SimpleAgent.Domain/         Agent state, decisions, errors, run summary
SimpleAgent.Application/    Agent loop, action definitions, abstractions, queries
SimpleAgent.Infrastructure/ OpenAI client, CQRS tool adapter, permissions, correlation context
```

## Run locally

Set `OPENAI_API_KEY`, then run:

```powershell
cd Agent
dotnet run
```

## Build

```powershell
dotnet build simple-agent.sln
```

## Notes

- Prompt and raw LLM response logging is truncated to reduce the risk of leaking sensitive data.
- The project is intentionally simple: it demonstrates the mechanics of an AI agent without introducing external observability tooling or a large action catalog.
