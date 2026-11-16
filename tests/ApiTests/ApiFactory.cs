namespace ApiTests;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.IdentityManagement.Services;
using Domain.IdentityManagement;
using Domain.IdentityManagement.Repositories;
using Domain.IdentityManagement.ValueObjects;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

/// <summary>
/// The real API (<see cref="Api.Program"/>) on ephemeral PostgreSQL and Redis containers, migrated
/// like production. Shared by every test of the collection; each test builds its own users and groups.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Api.Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Program reads some settings eagerly (Redis, environment) before the factory's overrides apply:
        // environment variables are seen from the start. Each test project runs in its own process.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        foreach (var (cle, valeur) in Reglages())
            Environment.SetEnvironmentVariable(cle.Replace(":", "__"), valeur);

        // Migrate before the host starts, so that the API's own startup migration is a no-op.
        var options = new DbContextOptionsBuilder<TontineDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new TontineDbContext(options, Mock.Of<IMediator>());
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development: fake SMS gateway (no real message leaves the tests).
        builder.UseEnvironment("Development");
        foreach (var (cle, valeur) in Reglages())
            builder.UseSetting(cle, valeur);
        // No background job server: the tests only exercise the HTTP API.
        builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
    }

    private IEnumerable<(string, string)> Reglages() =>
    [
        ("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString()),
        ("ConnectionStrings:HangfireConnection", _postgres.GetConnectionString()),
        ("ConnectionStrings:Redis", _redis.GetConnectionString() + ",abortConnect=false"),
        ("Jwt:Secret", "api-tests-secret-key-that-is-long-enough-0123456789"),
        ("Serilog:MinimumLevel:Default", "Warning"),
        ("Serilog:SeqUrl", "http://127.0.0.1:1"),
    ];

    /// <summary>Creates an account and returns a client authenticated as that account.</summary>
    public async Task<Acteur> CompteAsync(string nom, RoleUtilisateur role = RoleUtilisateur.Membre)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUtilisateurRepository>();
        var telephone = "+2376" + Random.Shared.Next(10_000_000, 99_999_999);
        var u = Utilisateur.Create(telephone, nom, "hash-non-utilise", role);
        await users.AddAsync(u);
        await scope.ServiceProvider.GetRequiredService<TontineDbContext>().SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateAccessToken(u);

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Acteur(u.Id.Value, nom, client);
    }
}

[CollectionDefinition(Nom)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Nom = "Api";
}

/// <summary>An account and its authenticated HTTP client.</summary>
public sealed record Acteur(Guid Id, string Nom, HttpClient Http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<HttpResponseMessage> Get(string url) => Http.GetAsync(url);
    public Task<HttpResponseMessage> Post(string url, object? body = null) => Http.PostAsJsonAsync(url, body ?? new { }, Json);
    public Task<HttpResponseMessage> Put(string url, object body) => Http.PutAsJsonAsync(url, body, Json);

    /// <summary>GET that must succeed; returns the JSON body.</summary>
    public async Task<JsonNode> Lire(string url)
    {
        var r = await Get(url);
        return await Corps(r, url);
    }

    /// <summary>POST that must succeed; returns the JSON body (or an empty object).</summary>
    public async Task<JsonNode> Envoyer(string url, object? body = null)
    {
        var r = await Post(url, body);
        return await Corps(r, url);
    }

    public async Task<JsonNode> Remplacer(string url, object body)
    {
        var r = await Put(url, body);
        return await Corps(r, url);
    }

    private static async Task<JsonNode> Corps(HttpResponseMessage r, string url)
    {
        var texte = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)r.StatusCode} on {url}: {texte}");
        return string.IsNullOrWhiteSpace(texte) ? new JsonObject() : JsonNode.Parse(texte)!;
    }
}
