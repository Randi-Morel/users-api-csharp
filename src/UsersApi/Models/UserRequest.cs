using System.ComponentModel.DataAnnotations;

namespace UsersApi.Models;

public sealed class UserRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(254)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "La edad es obligatoria.")]
    [Range(1, 120, ErrorMessage = "La edad debe estar entre 1 y 120.")]
    public int? Age { get; set; }
}
