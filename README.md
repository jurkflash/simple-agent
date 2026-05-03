# simple-agent

`simple-agent` is a small .NET 8 AI agent example that shows how to run an LLM-driven decision loop against a constrained tool surface and process jobs from RabbitMQ asynchronously.

## What this currently covers

- **Agent loop orchestration**: the agent asks the LLM for the next action, validates it, executes the allowed tool, and finishes with a final answer.
- **Constrained actions**: the current tool surface is intentionally small and only supports `get_complaints` and `finish`.
- **Structured tool execution**: the agent executes tools through a CQRS adapter instead of calling handlers directly.
- **Permission checks**: every tool action is checked before execution.
- **RabbitMQ background processing**:
  - durable `agent-jobs` queue
  - hosted background worker
  - manual ack/nack with requeue on failure
  - prefetch count of 1 for simple sequential processing
- **Observability and tracing**:
  - correlation ID per agent run
  - structured `ILogger` logging
  - step-level tracing for LLM decisions, validation, CQRS dispatch, and tool results
  - timing for total run duration, step duration, LLM calls, and CQRS execution
  - run summary output with success/failure metadata
- **Layered solution structure**:
  - `Agent`: hosted worker entrypoint / composition root
  - `SimpleAgent.Domain`: core models
  - `SimpleAgent.Application`: agent orchestration, use-case logic, abstractions
  - `SimpleAgent.Infrastructure`: OpenAI client, CQRS adapter, correlation context, permission service

## Current flow

1. The worker consumes a JSON message from the `agent-jobs` queue.
2. The message is deserialized into an `AgentJobMessage` with `goal` and `correlationId`.
3. The worker creates a scoped agent execution and starts processing.
4. The agent sends the current goal/history to the LLM.
5. The LLM returns a JSON decision.
6. The decision is parsed and validated against allowed actions.
7. If the action is `get_complaints`, the request is mapped to a CQRS query and dispatched.
8. The tool result is added to history.
9. The agent asks the LLM for the next step until it returns `finish`.
10. The worker logs the result and acknowledges the message, or nacks and requeues on failure.

## Message contract

Messages in `agent-jobs` are JSON serialized using this shape:

```json
{
  "goal": "Generate complaint summary for April",
  "correlationId": "9f1f0ffb-6f34-45f8-8e95-1a4dc9f9d1b3"
}
```

- `goal`: the natural-language task for the agent to execute
- `correlationId`: trace identifier propagated through the worker, agent, LLM, CQRS, and logs

## Queue behavior

- Queue name: `agent-jobs`
- Durable queue declaration
- Manual acknowledgment on success
- Negative acknowledgment with `requeue: true` on failure
- `BasicQos` prefetch count set to `1`

## Project structure

```text
Agent/                      Hosted worker entrypoint, queue consumer, and composition root
SimpleAgent.Domain/         Agent state, decisions, errors, run summary
SimpleAgent.Application/    Agent loop, action definitions, abstractions, queries
SimpleAgent.Infrastructure/ OpenAI client, CQRS tool adapter, permissions, correlation context
```

## Run locally

Set `OPENAI_API_KEY`, configure RabbitMQ settings, then run:

```powershell
cd Agent
dotnet run
```

RabbitMQ configuration is read from the `RabbitMQ` configuration section, for example via environment variables:

```text
RabbitMQ__HostName=localhost
RabbitMQ__Port=5672
RabbitMQ__UserName=guest
RabbitMQ__Password=guest
```

## Build

```powershell
dotnet build simple-agent.sln
```

## Notes

- Prompt and raw LLM response logging is truncated to reduce the risk of leaking sensitive data.
- The project is intentionally simple: it demonstrates the mechanics of an AI agent without introducing external observability tooling or a large action catalog.
