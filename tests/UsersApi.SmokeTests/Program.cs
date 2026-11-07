using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// Pruebas HTTP reales, sin paquetes de test externos ni datos personales.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var dll = Path.Combine(root, "src/UsersApi/bin/Release/net8.0/UsersApi.dll");
if (!File.Exists(dll)) throw new FileNotFoundException("Ejecuta primero dotnet build -c Release.", dll);
var temporary = Path.Combine(Path.GetTempPath(), "usersapi-tests-" + Guid.NewGuid());
Directory.CreateDirectory(temporary);
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
Process? server = null;
var output = new StringBuilder();
var checks = 0;
try
{
    server = Start();
    await Ready();
    await Check(await client.GetAsync("/api/users"), 200);
    var creationResponse = await client.PostAsJsonAsync("/api/users", new { name = " Randi Morel ", email = "Randi@Example.com", age = 25 });
    var location = creationResponse.Headers.Location;
    var created = await Check(creationResponse, 201);
    var id = created.GetProperty("id").GetGuid();
    Require(location?.AbsolutePath == $"/api/users/{id}", "Location apunta al usuario creado");
    Require(created.GetProperty("name").GetString() == "Randi Morel", "Nombre normalizado");
    Require(created.GetProperty("email").GetString() == "randi@example.com", "Correo normalizado");
    var originalDate = created.GetProperty("createdAt").GetString();
    await Check(await client.GetAsync($"/api/users/{id}"), 200);
    await Check(await client.PostAsJsonAsync("/api/users", new { name = "Otra persona", email = "RANDI@example.com", age = 20 }), 409);
    foreach (var invalid in new object[] {
        new { name = " ", email = "ok@example.com", age = 20 },
        new { name = " a ", email = "ok@example.com", age = 20 },
        new { name = "Randi", email = "incorrecto", age = 20 },
        new { name = "Randi", email = "ok@example.com", age = 0 },
        new { name = "Randi", email = "ok@example.com", age = 121 },
        new { name = "Randi", email = "ok@example.com" },
        new { name = (string?)null, email = "ok@example.com", age = 20 }
    }) await Check(await client.PostAsJsonAsync("/api/users", invalid), 400);
    await Check(await client.PostAsync("/api/users", new StringContent("{bad", Encoding.UTF8, "application/json")), 400);
    await Check(await client.PostAsync("/api/users", new StringContent("null", Encoding.UTF8, "application/json")), 400);
    var updated = await Check(await client.PutAsJsonAsync($"/api/users/{id}", new { name = "Randi Actualizado", email = "randi@example.com", age = 26 }), 200);
    Require(updated.GetProperty("createdAt").GetString() == originalDate, "Conservar fecha de creación");
    await Check(await client.PutAsJsonAsync($"/api/users/{id}", new { name = "Randi", email = "invalid", age = 26 }), 400);
    var second = await Check(await client.PostAsJsonAsync("/api/users", new { name = "Segundo usuario", email = "second@example.com", age = 30 }), 201);
    await Check(await client.PutAsJsonAsync($"/api/users/{second.GetProperty("id").GetGuid()}", new { name = "Segundo usuario", email = "randi@example.com", age = 30 }), 409);
    // Varias altas simultáneas con el mismo correo deben producir una sola creación.
    var parallel = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync("/api/users", new { name = "Concurrente", email = "parallel@example.com", age = 22 })));
    Require(parallel.Count(r => r.StatusCode == HttpStatusCode.Created) == 1 && parallel.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 7, "Unicidad concurrente del correo");
    foreach (var response in parallel) response.Dispose();
    Stop();
    server = Start();
    await Ready();
    var persisted = await Check(await client.GetAsync($"/api/users/{id}"), 200);
    Require(persisted.GetProperty("age").GetInt32() == 26, "Persistencia tras reinicio");
    await Check(await client.DeleteAsync($"/api/users/{id}"), 204);
    await Check(await client.GetAsync($"/api/users/{id}"), 404);
    await Check(await client.DeleteAsync($"/api/users/{id}"), 404);
    await Check(await client.PutAsJsonAsync($"/api/users/{Guid.NewGuid()}", new { name = "Inexistente", email = "nobody@example.com", age = 22 }), 404);
    await Check(await client.GetAsync("/api/users/no-es-guid"), 404);
    Stop();
    server = Start();
    await Ready();
    await Check(await client.GetAsync($"/api/users/{id}"), 404);
    Stop();
    Require(output.ToString().Contains("RequestId="), "Middleware emite logs");
    Console.WriteLine($"PASS: {checks} comprobaciones HTTP, validación, concurrencia, persistencia y middleware.");
}
catch
{
    Console.Error.WriteLine(output.ToString());
    throw;
}
finally
{
    Stop();
    Directory.Delete(temporary, recursive: true);
}

Process Start()
{
    var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
    {
        WorkingDirectory = temporary,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    start.ArgumentList.Add(dll);
    start.Environment["ASPNETCORE_URLS"] = client.BaseAddress!.ToString().TrimEnd('/');
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
    start.Environment["Users__FilePath"] = Path.Combine(temporary, "users.json");
    var process = Process.Start(start)!;
    process.OutputDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

void Stop()
{
    if (server is null) return;
    if (!server.HasExited) server.Kill(entireProcessTree: true);
    server.WaitForExit();
    server.Dispose();
    server = null;
}

async Task Ready()
{
    for (var attempt = 0; attempt < 100; attempt++)
    {
        if (server!.HasExited) throw new Exception("La API terminó antes de iniciar.");
        try { using var response = await client.GetAsync("/health"); if (response.IsSuccessStatusCode) return; }
        catch (HttpRequestException) { }
        await Task.Delay(100);
    }
    throw new TimeoutException("La API no inició.");
}

async Task<JsonElement> Check(HttpResponseMessage response, int expected)
{
    using (response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Require((int)response.StatusCode == expected, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: esperado {expected}, recibido {(int)response.StatusCode}: {body}");
        Require(response.Headers.Contains("X-Request-Id"), "Cabecera del middleware");
        return body.Length == 0 ? default : JsonSerializer.Deserialize<JsonElement>(body);
    }
}

void Require(bool success, string message)
{
    if (!success) throw new Exception(message);
    checks++;
}
