using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Poker.Api.Auth;
using Poker.Api.Endpoints;
using Poker.Api.Hubs;
using Poker.Api.Iam;
using Poker.Application.Abstractions;
using Poker.Application.Tables;
using Poker.Application.Wallet;
using Poker.Infrastructure.DailyGift;
using Poker.Infrastructure.Persistence;
using Poker.Infrastructure.Redis;
using Poker.Infrastructure.Tables;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// --- Redis ---
string redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnectionString));

// --- Postgres / EF Core ---
string postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Database=poker;Username=poker;Password=poker";
builder.Services.AddDbContext<PokerDbContext>(o => o.UseNpgsql(postgresConnectionString));

// --- Application/Infrastructure services ---
builder.Services.AddSingleton<IClock, Poker.Application.Abstractions.SystemClock>();
builder.Services.AddSingleton<ITableRepository, InMemoryTableRepository>();
builder.Services.AddSingleton<IActiveTableTracker, RedisActiveTableTracker>();
builder.Services.AddSingleton<IDistributedLock, RedisDistributedLock>();
builder.Services.AddScoped<IWalletRepository, EfWalletRepository>();
builder.Services.AddScoped<IUserRepository, EfUserRepository>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<TableService>();
builder.Services.AddSingleton<EmailDomainAllowList>();
builder.Services.AddHostedService<DailyGiftHostedService>();

// --- Auth: trust identity headers injected by Ory Oathkeeper at the edge ---
builder.Services
    .AddAuthentication(OathkeeperAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, OathkeeperAuthenticationHandler>(
        OathkeeperAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

// --- SignalR, scaled out via the Redis backplane ---
builder.Services.AddSignalR().AddStackExchangeRedis(redisConnectionString);
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();

builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PokerDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapPokerEndpoints();
app.MapHub<LobbyHub>("/hubs/lobby");
app.MapHub<TableHub>("/hubs/table");

app.Run();
