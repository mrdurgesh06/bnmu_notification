using Microsoft.OpenApi.Models;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Notification.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();

// Firebase Admin SDK (for sending push notifications from the server).
// Optional at startup: if firebase-service-account.json isn't present
// yet, the API still runs fine -- FcmService just logs a warning and
// skips sending until the key file is added. See README notes for how
// to generate this file from the Firebase console.
//
// Checked in two places since Visual Studio (F5/Debug) and `dotnet run`
// don't always agree on ContentRootPath vs the actual build output
// folder -- this way it's found either way, regardless of whether the
// file's "Copy to Output Directory" property is set.
var firebaseKeyCandidates = new[]
{
    Path.Combine(builder.Environment.ContentRootPath, "firebase-service-account.json"),
    Path.Combine(AppContext.BaseDirectory, "firebase-service-account.json"),
};
var firebaseKeyPath = firebaseKeyCandidates.FirstOrDefault(File.Exists);

if (firebaseKeyPath != null && FirebaseApp.DefaultInstance == null)
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromFile(firebaseKeyPath),
    });
    Console.WriteLine($"[Firebase] Loaded service account key from: {firebaseKeyPath}");
}
else if (firebaseKeyPath == null)
{
    Console.WriteLine(
        "[Firebase] firebase-service-account.json not found in either the content root " +
        $"({firebaseKeyCandidates[0]}) or the build output ({firebaseKeyCandidates[1]}) -- " +
        "push notifications are disabled until it's added. In Visual Studio, right-click the " +
        "file -> Properties -> set 'Copy to Output Directory' to 'Copy if newer'.");
}

builder.Services.AddSingleton<FcmService>();
builder.Services.AddHostedService<NoticePushWorker>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Notification API",
        Version = "v1"
    });
});

var app = builder.Build();

// Serves wwwroot/bnmu_logo.png (and anything else dropped in wwwroot) as
// static files -- this is what lets FcmService build a public URL for the
// BNMU logo to show as a big-picture image in push notifications when the
// app is closed (see PublicBaseUrl in appsettings.json).
app.UseStaticFiles();

// Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Notification API V1");
    c.RoutePrefix = "swagger";
});

// Test Home Page
app.MapGet("/", () =>
{
    return Results.Ok("Notification API Running Successfully");
});

// CORS
app.UseCors("AllowAll");

// Authorization
app.UseAuthorization();

// Controllers
app.MapControllers();

app.Run();