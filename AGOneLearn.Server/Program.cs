using AgOne.Sso;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAgOneSso(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAgOneSso();
app.UseAuthorization();

app.MapControllers();
app.MapAgOneSsoEndpoints();
app.MapFallbackToFile("index.html");

app.Run();
