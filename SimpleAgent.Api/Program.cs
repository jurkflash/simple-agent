using Pokok.BuildingBlocks.Cqrs.Dispatching;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Messaging.RabbitMQ;
using SimpleAgent.Application;
using SimpleAgent.Application.Abstractions;
using SimpleAgent.Api.Services;
using SimpleAgent.Infrastructure;
using SimpleAgent.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.IncludeScopes = true;
    options.TimestampFormat = "HH:mm:ss.fff ";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddControllers();
builder.Services.Configure<RabbitMQOptions>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<RabbitMQConnection>();
builder.Services.AddSingleton<IRabbitMQConnection>(serviceProvider => serviceProvider.GetRequiredService<RabbitMQConnection>());
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<PendingAgentRequests>();
builder.Services.AddSingleton<AgentJobPublisher>();
builder.Services.AddSingleton<IAgentJobPublisher>(serviceProvider => serviceProvider.GetRequiredService<AgentJobPublisher>());
builder.Services.AddSingleton<AgentRequestClient>();
builder.Services.AddHostedService<AgentResponseListener>();

var app = builder.Build();

await EnsureDatabaseCreatedAsync(app);

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

static async Task EnsureDatabaseCreatedAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}
