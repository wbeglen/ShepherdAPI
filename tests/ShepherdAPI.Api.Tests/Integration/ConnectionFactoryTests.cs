using Dapper;
using Microsoft.Extensions.Configuration;
using ShepherdAPI.Api.Data;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

[Collection("Database")]
public class ConnectionFactoryTests(DatabaseFixture fixture)
{
    private static async Task<int> ReadLockTimeoutAsync(ISqlConnectionFactory factory)
    {
        using var connection = await factory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<int>("SELECT @@LOCK_TIMEOUT;");
    }

    [Fact]
    public async Task NewConnection_ReportsTheConfiguredLockTimeout()
    {
        Assert.Equal(1234, await ReadLockTimeoutAsync(fixture.CreateConnectionFactory(1234)));
    }

    [Fact]
    public async Task NewConnection_UsesTheDefaultLockTimeout_WhenNotConfigured()
    {
        Assert.Equal(SqlConnectionFactory.DefaultLockTimeoutMs, await ReadLockTimeoutAsync(fixture.CreateConnectionFactory(null)));
        Assert.Equal(10000, SqlConnectionFactory.DefaultLockTimeoutMs);
    }

    [Fact]
    public async Task EveryConnectionFromTheFactory_GetsTheTimeout_EvenWhenPooled()
    {
        var factory = fixture.CreateConnectionFactory(2222);

        Assert.Equal(2222, await ReadLockTimeoutAsync(factory));
        Assert.Equal(2222, await ReadLockTimeoutAsync(factory));
    }

    [Fact]
    public async Task FixtureFactory_UsesTheShortFixtureTimeout()
    {
        Assert.Equal(DatabaseFixture.FixtureLockTimeoutMs, await ReadLockTimeoutAsync(fixture.ConnectionFactory));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("soon")]
    public void InvalidLockTimeout_FailsFast(string configured)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Experiments"] = fixture.ConnectionString,
            [SqlConnectionFactory.LockTimeoutKey] = configured,
        }).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new SqlConnectionFactory(configuration));
        Assert.Contains(SqlConnectionFactory.LockTimeoutKey, ex.Message);
    }
}
