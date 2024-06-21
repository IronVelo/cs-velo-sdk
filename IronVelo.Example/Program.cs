using System.Net.Mime;
using IronVelo;
using IronVelo.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSingleton<VeloSdk>(_ => new VeloSdk("127.0.0.1", port: 3069));
builder.Services.AddLogging();

var app = builder.Build();

// For errors pertaining to the connection / critical precondition violations the SDK will throw
app.UseExceptionHandler(exceptionHandlerApp => exceptionHandlerApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = MediaTypeNames.Text.Plain;
    var maybeErr = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;

    if (maybeErr is RequestError err)
    {
        await context.Response.WriteAsync(err.ToString());
    }
    else
    {
        // Only possibilities of this are perhaps deserialization but unlikely as RequestError generally handles.
        await context.Response.WriteAsync("Exception!");
    }
}));

if (app.Environment.IsDevelopment())
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning("HTTPS redirection is not enabled. This is not safe!");
}
else
{
    app.UseHttpsRedirection();
}

app.UseRouting();

app.MapControllers();
app.Run();