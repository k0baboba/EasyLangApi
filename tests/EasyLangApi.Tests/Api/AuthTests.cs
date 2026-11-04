using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EasyLangApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EasyLangApi.Tests.Api;

public class AuthTests
{
    private static Task<HttpResponseMessage> Register(HttpClient client, string email, string password = ApiFactory.Password) =>
        client.PostAsJsonAsync("/auth/register", new { email, password });

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password = ApiFactory.Password) =>
        client.PostAsJsonAsync("/auth/login", new { email, password });

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/auth/refresh", new { refreshToken });

    private static HttpRequestMessage Me(string accessToken) => new(HttpMethod.Get, "/auth/me")
    {
        Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
    };

    [Fact]
    public async Task Register_then_call_protected_endpoint()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var registered = await Register(client, "Maya@Example.com");
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var tokens = await registered.ReadJsonAsync();

        var me = await client.SendAsync(Me(tokens["accessToken"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("maya@example.com", (await me.ReadJsonAsync())["email"]!.GetValue<string>());
    }

    [Fact]
    public async Task Password_is_stored_only_as_hash()
    {
        await using var factory = new ApiFactory();
        await Register(factory.CreateClient(), "leo@example.com");

        var user = await factory.WithDbAsync(db => db.Users.SingleAsync());

        Assert.DoesNotContain(ApiFactory.Password, user.PasswordHash);
        Assert.True(user.PasswordHash.Length > 40);
    }

    [Theory]
    [InlineData("not-an-email", "long-enough-password")]
    [InlineData("a@b.c", "short")]
    [InlineData("", "long-enough-password")]
    public async Task Invalid_registration_is_bad_request(string email, string password)
    {
        await using var factory = new ApiFactory();

        var response = await Register(factory.CreateClient(), email, password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Email_can_be_registered_once_regardless_of_case()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await Register(client, "leo@example.com");

        var again = await Register(client, "LEO@example.com");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Login_checks_password_and_does_not_reveal_which_part_is_wrong()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await Register(client, "leo@example.com");

        var ok = await Login(client, "leo@example.com");
        var wrongPassword = await Login(client, "leo@example.com", "wrong-password-123");
        var unknownUser = await Login(client, "nobody@example.com");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(
            (await wrongPassword.ReadJsonAsync())["detail"]!.GetValue<string>(),
            (await unknownUser.ReadJsonAsync())["detail"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("/episodes")]
    [InlineData("/vocabulary")]
    [InlineData("/review/next")]
    [InlineData("/lines/1/explain?phrase=beat")]
    [InlineData("/auth/me")]
    public async Task Protected_endpoints_require_token(string url)
    {
        await using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Forged_token_is_rejected()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var tokens = await (await Register(client, "leo@example.com")).ReadJsonAsync();
        var token = tokens["accessToken"]!.GetValue<string>();
        var tampered = token[..^4] + (token[^4] == 'A' ? "BBBB" : "AAAA");

        var response = await client.SendAsync(Me(tampered));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Access_token_expires_and_refresh_gives_a_new_pair()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var tokens = await (await Register(client, "leo@example.com")).ReadJsonAsync();

        factory.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Me(tokens["accessToken"]!.GetValue<string>()))).StatusCode);

        var refreshed = await Refresh(client, tokens["refreshToken"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var fresh = await refreshed.ReadJsonAsync();
        Assert.NotEqual(tokens["refreshToken"]!.GetValue<string>(), fresh["refreshToken"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Me(fresh["accessToken"]!.GetValue<string>()))).StatusCode);
    }

    [Fact]
    public async Task Reusing_old_refresh_token_revokes_the_whole_session()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var first = (await (await Register(client, "leo@example.com")).ReadJsonAsync())["refreshToken"]!.GetValue<string>();
        var second = (await (await Refresh(client, first)).ReadJsonAsync())["refreshToken"]!.GetValue<string>();

        var reuse = await Refresh(client, first);
        var afterReuse = await Refresh(client, second);

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_expires()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var refresh = (await (await Register(client, "leo@example.com")).ReadJsonAsync())["refreshToken"]!.GetValue<string>();

        factory.Time.Advance(TimeSpan.FromDays(31));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, refresh)).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var refresh = (await (await Register(client, "leo@example.com")).ReadJsonAsync())["refreshToken"]!.GetValue<string>();

        var logout = await client.PostAsJsonAsync("/auth/logout", new { refreshToken = refresh });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, refresh)).StatusCode);
    }

    [Fact]
    public async Task Configured_signing_key_works()
    {
        await using var factory = new ApiFactory().WithSetting("Jwt:SigningKey", new string('k', 40));

        var client = await factory.CreateUserClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Configured_signing_key_must_be_long_enough()
    {
        await using var factory = new ApiFactory().WithSetting("Jwt:SigningKey", "too-short");

        Assert.ThrowsAny<Exception>(() => factory.CreateClient()); // приложение не стартует
    }
}
