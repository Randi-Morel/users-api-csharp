using Microsoft.AspNetCore.Mvc;
using UsersApi.Models;
using UsersApi.Services;

namespace UsersApi.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(UserStore store) : ControllerBase
{
    [HttpGet]
    public ActionResult<User[]> GetAll() => Ok(store.All());

    [HttpGet("{id:guid}")]
    public ActionResult<User> GetById(Guid id) => store.Find(id) is { } user
        ? Ok(user) : Problem(statusCode: 404, title: "Usuario no encontrado.");

    [HttpPost]
    public ActionResult<User> Create(UserRequest request)
    {
        if (!NormalizeAndValidate(request)) return ValidationProblem(ModelState);
        var result = store.Create(request);
        return result.Status == WriteStatus.DuplicateEmail
            ? Problem(statusCode: 409, title: "Ya existe un usuario con ese correo.")
            : CreatedAtAction(nameof(GetById), new { id = result.User!.Id }, result.User);
    }

    [HttpPut("{id:guid}")]
    public ActionResult<User> Update(Guid id, UserRequest request)
    {
        if (!NormalizeAndValidate(request)) return ValidationProblem(ModelState);
        var result = store.Update(id, request);
        return result.Status switch
        {
            WriteStatus.NotFound => Problem(statusCode: 404, title: "Usuario no encontrado."),
            WriteStatus.DuplicateEmail => Problem(statusCode: 409, title: "Ya existe un usuario con ese correo."),
            _ => Ok(result.User)
        };
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id) => store.Delete(id)
        ? NoContent() : Problem(statusCode: 404, title: "Usuario no encontrado.");

    private bool NormalizeAndValidate(UserRequest request)
    {
        request.Name = request.Name.Trim();
        request.Email = request.Email.Trim().ToLowerInvariant();
        // Volver a validar evita aceptar nombres como " a " tras retirar espacios.
        ModelState.Clear();
        return TryValidateModel(request);
    }
}
