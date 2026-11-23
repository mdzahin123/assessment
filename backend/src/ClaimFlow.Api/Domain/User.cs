namespace ClaimFlow.Api.Domain;

public class User
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public UserRole Role { get; private set; }

    // EF Core needs a parameterless constructor to rebuild objects from database rows.
    private User() { }

    public User(Guid id, string name, string email, UserRole role)
    {
        Id = id;
        Name = name;
        Email = email;
        Role = role;
    }
}
