namespace UsersApi.Models;

public sealed record User(Guid Id, string Name, string Email, int Age, DateTimeOffset CreatedAt);
