using System.Net;
using IronVelo.Flows;
using IronVelo.Types;
using Newtonsoft.Json;

namespace IronVelo;
using System.Net.Http;

internal enum RouteType
{
    Signup = 0,
    Login = 1,
    Refresh = 2,
    Revoke = 3,
    Health = 4,
    Delete = 5,
    MigrateLogin = 6,
    UpdateMfa = 7,
}

internal record H2Client
{
    private string[] Routes { get; }
    private HttpClient HttpConnection { get; }
    internal H2Client(string host, int port = 443, HttpClient? httpClient = null)
    {
        HttpConnection = httpClient ?? new HttpClient();
        Routes = new[]
        {
            "https://" + host + ":" + port + "/signup",  // (int)RouteType.Signup
            "https://" + host + ":" + port + "/login",   // (int)RouteType.Login
            "https://" + host + ":" + port + "/refresh", // (int)RouteType.Refresh 
            "https://" + host + ":" + port + "/revoke",  // (int)RouteType.Revoke
            "https://" + host + ":" + port + "/health",  // (int)RouteType.Health
            "https://" + host + ":" + port + "/delete",  // (int)RouteType.Delete
            "https://" + host + ":" + port + "/mLogin",  // (int)RouteType.MigrateLogin
            "https://" + host + ":" + port + "/upMfa"    // (int)RouteType.UpdateMfa
        };
    }
    
    internal Task<HttpResponseMessage> MakeDefaultRequest<TC>(
        TC content, RouteType route
    ) where TC : HttpContent
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            Routes[(int)route]
        ){
            Version = new Version(2, 0),
            Content = content,
        };

        return HttpConnection.SendAsync(request);
    }

    internal Task<HttpResponseMessage> MakeRequestWithTimeout<TC>(
        TC? content, RouteType route, TimeSpan timeout, HttpMethod method
    ) where TC : HttpContent
    {
        var request = new HttpRequestMessage(
            method,
            Routes[(int)route]
        )
        {
            Version = new Version(2, 0),
            Content = content,
        };

        var cts = new CancellationTokenSource(timeout);
        return HttpConnection.SendAsync(request, cts.Token);
    }
}

/// <summary>
/// The main entry point for interacting with IronVelo's Identity Provider (IdP).
/// </summary>
public class VeloSdk
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VeloSdk"/> class with the specified host and port.
    /// </summary>
    /// <param name="host">The host address of the IronVelo IdP.</param>
    /// <param name="port">The port to connect to the IronVelo IdP. Defaults to 443.</param>
    /// <param name="httpClient">
    /// An optional <see cref="HttpClient"/> to be used for making requests. If null, a default client will be created.
    /// </param>
    public VeloSdk(string host, int port = 443, HttpClient? httpClient = null)
    {
        _client = new H2Client(host, port, httpClient);
    }
    
    private readonly H2Client _client;
    
    /// <summary>
    /// Initiate the login flow for a fully migrated user. 
    /// </summary>
    /// <returns>The ingress state to the login flow.</returns>
    public Flows.Login.HelloLogin Login()
    {
        var client = new FlowClient(RouteType.Login, _client);
        return new Flows.Login.HelloLogin(client);
    } 
    
    /// <summary>
    /// Initiate the signup for a new user
    /// </summary>
    /// <returns>The ingress state to the signup flow.</returns>
    public Flows.Signup.HelloSignup Signup()
    {
        var client = new FlowClient(RouteType.Signup, _client);
        return new Flows.Signup.HelloSignup(client);
    }
    
    /// <summary>
    /// Initiate a login for a user who hasn't migrated to IronVelo's identity provider fully (they have not setup MFA)
    /// </summary>
    /// <returns>The ingress state to the login migration flow.</returns>
    public Flows.MigrateLogin.HelloLogin MigrateLogin()
    {
        var client = new FlowClient(RouteType.MigrateLogin, _client);
        return new Flows.MigrateLogin.HelloLogin(client);
    }
    
    /// <summary>
    /// Schedule the deletion of a user
    /// </summary>
    /// <remarks>
    /// While it is not recommended, you are able to configure your IdP to immediately delete the users account. The
    /// default is schedule deletion in one week, if the user logs in within that period the account will persist.
    ///
    /// We strongly recommend leveraging scheduled deletion, as if the user's token and password are compromised
    /// (despite many measures in place to prevent this) a hacker could theoretically delete the user's account. We
    /// use immediate deletion just to simplify integration testing. 
    /// </remarks>
    public Flows.Delete.AskDelete DeleteUser()
    {
        var client = new FlowClient(RouteType.Delete, _client);
        return new Flows.Delete.AskDelete(client);
    }

    /// <inheritdoc cref="Flows.UpdateMfa.HelloUpdateMfa"/>
    public Flows.UpdateMfa.HelloUpdateMfa UpdateMfa()
    {
        var client = new FlowClient(RouteType.UpdateMfa, _client);
        return new Flows.UpdateMfa.HelloUpdateMfa(client);
    }
    
    /// <summary>
    /// Checks the validity of the provided token using the <c>peek</c> operation. This ensures that the token both has
    /// not been revoked or stolen. This should be invoked on practically <b>all authenticated requests</b>. Token
    /// validation is highly optimized in IronVelo's IdP, providing superior performance and security compared to
    /// traditional identity providers.
    /// <br/>
    /// <para>
    /// IronVelo's IdP offers a novel, and refreshingly simple, token management protocol. In this protocol a token is
    /// strictly one time use (though this feature can be disabled). This allows for some incredible security properties
    /// but also is something that developers should be aware of.
    /// <br/><br/>
    /// Tokens are a common attack vector for users, with our protocol the amount of damage possible is significantly
    /// reduced. The beauty of this is that detecting a stolen token does not require anomaly detection, or anything of
    /// complexity, but is detected and remedied intrinsically by the protocol.
    /// <br/><br/>
    /// This automatic remedying of token theft comes at only benefit to the user. A user can have multiple tokens at
    /// once (them being logged in on multiple devices), and if a token theft is remedied only that specific login is
    /// invalidated, leaving other sessions untouched. If a user wishes to log out of all devices you can use the
    /// <see cref="VeloSdk.RevokeTokens"/>.
    /// </para>
    /// </summary>
    /// <param name="token">The token to be checked and rotated.</param>
    /// <returns>A result containing the new <see cref="PeekedToken"/> if successful, or an error.</returns>
    /// <remarks>
    /// <b>Usage Considerations:</b>
    /// <list type="bullet"><item>
    /// <description>
    /// <b>Token Invalidation:</b> The provided token will no longer be valid after this operation.
    /// </description>
    /// </item><item>
    /// <description>
    /// <b>New Token:</b> The returned <see cref="PeekedToken"/> includes a new token which must be used for any further
    /// interactions.
    /// </description>
    /// </item><item>
    /// <description>
    /// <b>Security:</b> This method enhances security by preventing the reuse of tokens, mitigating the risk of token
    /// theft and replay attacks.
    /// </description>
    /// </item></list>
    /// </remarks>
    public FutResult<PeekedToken, Opaque> CheckToken(Token token)
    {
        return new FutResult<PeekedToken, Opaque>(CheckTokenImp(token));
    }

    private async Task<Result<PeekedToken, Opaque>> CheckTokenImp(Token token)
    {
        var response = await _client.MakeDefaultRequest(token.AsRawContent(), RouteType.Refresh);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return Opaque.AsResult<PeekedToken>();
        }

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<PeekedToken>(content, new TokenDeserializer());

        return result == null ? Opaque.AsResult<PeekedToken>() : Result<PeekedToken, Opaque>.Success(result);
    }

    /// <summary>
    /// Log out of all sessions for a user
    /// </summary>
    /// <param name="token">The representation of the user's login state</param>
    public FutResult<None, Option<Token>> RevokeTokens(Token token)
    {
        return new FutResult<None, Option<Token>>(RevokeTokensImp(token));
    }

    private async Task<Result<None, Option<Token>>> RevokeTokensImp(Token token)
    {
        var response = await _client.MakeDefaultRequest(token.AsRawContent(), RouteType.Revoke);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return Result<None, Option<Token>>.Failure(Option<Token>.None());
        }
    
        var result = JsonConvert.DeserializeObject<RevokeResponse>(
            await response.Content.ReadAsStringAsync()
        );

        if (result?.Failure is { } newToken)
        {
            return Result<None, Option<Token>>.Failure(Option<Token>.Some(new Token(newToken)));
        }

        return None.AsOk<Option<Token>>();
    }

    /// <summary>
    /// Quickly check if the IdP is currently online / accessible
    /// </summary>
    /// <param name="timeoutSeconds">Give up on the request after some number of seconds</param>
    public async Task<bool> IsHealthy(uint timeoutSeconds = 5)
    {
        var response = await _client.MakeRequestWithTimeout<ByteArrayContent>(
            null, RouteType.Health, TimeSpan.FromSeconds(timeoutSeconds), HttpMethod.Get
        );

        return response.StatusCode == HttpStatusCode.OK;
    }
}

internal record RevokeResponse(
    [property: JsonProperty("failure", NullValueHandling = NullValueHandling.Ignore)] string? Failure
);
