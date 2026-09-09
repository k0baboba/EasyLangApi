var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/translate", () => "Привет");

app.Run();
