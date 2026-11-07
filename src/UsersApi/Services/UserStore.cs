using System.Text.Json;
using UsersApi.Models;

namespace UsersApi.Services;

public enum WriteStatus { Success, NotFound, DuplicateEmail }
public sealed record WriteResult(WriteStatus Status, User? User = null);

// Una instancia por proceso. El bloqueo serializa lecturas, comprobaciones y escrituras.
public sealed class UserStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private List<User> _users;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public UserStore(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _path = Path.GetFullPath(configuration["Users:FilePath"] ?? "App_Data/users.json", environment.ContentRootPath);
        _users = File.Exists(_path)
            ? JsonSerializer.Deserialize<List<User>>(File.ReadAllText(_path), JsonOptions)
                ?? throw new InvalidDataException("El archivo de usuarios no contiene una lista válida.")
            : new List<User>();
    }

    public User[] All() { lock (_gate) return _users.OrderBy(u => u.CreatedAt).ToArray(); }
    public User? Find(Guid id) { lock (_gate) return _users.Find(u => u.Id == id); }

    public WriteResult Create(UserRequest request)
    {
        lock (_gate)
        {
            if (EmailExists(request.Email)) return new(WriteStatus.DuplicateEmail);
            var user = new User(Guid.NewGuid(), request.Name, request.Email, request.Age!.Value, DateTimeOffset.UtcNow);
            Save([.. _users, user]);
            return new(WriteStatus.Success, user);
        }
    }

    public WriteResult Update(Guid id, UserRequest request)
    {
        lock (_gate)
        {
            var index = _users.FindIndex(u => u.Id == id);
            if (index < 0) return new(WriteStatus.NotFound);
            if (EmailExists(request.Email, id)) return new(WriteStatus.DuplicateEmail);
            var user = _users[index] with { Name = request.Name, Email = request.Email, Age = request.Age!.Value };
            var next = new List<User>(_users);
            next[index] = user;
            Save(next);
            return new(WriteStatus.Success, user);
        }
    }

    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            var next = _users.Where(u => u.Id != id).ToList();
            if (next.Count == _users.Count) return false;
            Save(next);
            return true;
        }
    }

    private bool EmailExists(string email, Guid? except = null) =>
        _users.Any(u => u.Id != except && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

    private void Save(List<User> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
            // Publicar el nuevo estado solo después de que el archivo se guarde.
            _users = next;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
