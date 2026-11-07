# API de gestión de usuarios en C# con ASP.NET Core

Proyecto académico de API REST con CRUD, validación, persistencia JSON y middleware de registro. Está escrito en C# sobre .NET 8 y no requiere paquetes NuGet adicionales ni un servidor de base de datos.

## Ejecutar en Visual Studio o terminal

Requisito: SDK de .NET 8 (Visual Studio 2022 con soporte para .NET 8 y desarrollo ASP.NET si usas Visual Studio).

1. Abre `UsersApi.sln` y establece `UsersApi` como proyecto de inicio.
2. Ejecuta con el perfil `UsersApi` (F5), o desde la carpeta raíz:

```sh
dotnet build UsersApi.sln --configuration Release
dotnet run --project src/UsersApi
```

La API escucha en `http://localhost:5080`. Visita `/health` para comprobar el inicio. Usa `UsersApi.http` desde Visual Studio o VS Code con un cliente de archivos HTTP; también puedes usar Postman. La raíz devuelve información básica de la API. No se incluye una interfaz gráfica ni Swagger.

## Endpoints

| Método | Ruta | Resultado correcto |
| --- | --- | --- |
| GET | `/api/users` | 200 y lista de usuarios |
| GET | `/api/users/{id}` | 200 y usuario |
| POST | `/api/users` | 201, usuario y cabecera Location |
| PUT | `/api/users/{id}` | 200 y usuario actualizado |
| DELETE | `/api/users/{id}` | 204 sin cuerpo |

POST y PUT reciben el mismo cuerpo; PUT reemplaza todos los campos editables:

```json
{
  "name": "Randi Morel",
  "email": "randi@example.com",
  "age": 25
}
```

La respuesta incorpora `id` (GUID) y `createdAt` (UTC). Son campos administrados por el servidor; la actualización conserva ambos. Las propiedades JSON se escriben en camelCase.

## Validación y errores

- Nombre obligatorio de 2 a 100 caracteres, también después de retirar espacios exteriores.
- Correo obligatorio con formato válido y hasta 254 caracteres; se guarda en minúsculas.
- Edad obligatoria, entera y entre 1 y 120.
- Correo único sin distinguir mayúsculas; se devuelve 409 si está duplicado.
- Datos inválidos o JSON mal formado: 400. Usuario inexistente o ID con formato no GUID: 404.
- Los errores de validación contienen `errors` y usan Problem Details. Los errores internos se gestionan con el manejador de excepciones de ASP.NET Core; no se incluyen detalles internos en Production.

`[ApiController]` y DataAnnotations validan las peticiones antes de ejecutar el controlador. Después de normalizar los campos se vuelve a validar. Los espacios en correos pueden ser rechazados por la validación inicial del formato; usa un correo sin espacios.

## Middleware

`RequestLoggingMiddleware` registra método, ruta, código HTTP, duración e identificador de solicitud. Añade la cabecera `X-Request-Id`. No registra cuerpos ni correos. Se ejecuta antes del manejador de excepciones para observar el estado final de la respuesta.

## Datos y alcance

El primer POST crea `src/UsersApi/App_Data/users.json` al ejecutar con el perfil del proyecto. Está excluido de Git para evitar publicar datos locales. La ruta se configura con `Users:FilePath` o la variable `Users__FilePath`.

Las operaciones usan exclusión mutua dentro de un proceso. Se escribe primero un archivo temporal y después se reemplaza el archivo de datos; el estado en memoria solo cambia tras guardar correctamente. Si el archivo existente está corrupto, se produce un error y no se sobrescribe silenciosamente.

Esta persistencia está pensada para la tarea y una única instancia. No compartas el archivo entre varios procesos. La API no incluye autenticación; el requisito de middleware se cubre con logging. Para un despliegue real con datos personales se requerirían autenticación, autorización, HTTPS y una base de datos apropiada. Publicar el código en GitHub no exige desplegar el servidor.

## Pruebas automatizadas

Desde la carpeta que contiene `UsersApi.sln`:

```sh
dotnet build UsersApi.sln --configuration Release
dotnet run --project tests/UsersApi.SmokeTests --configuration Release --no-build -- .
```

Es un ejecutable de pruebas HTTP, no un proyecto para `dotnet test`. Arranca la API en un puerto local y usa un directorio temporal. Comprueba CRUD, cabecera Location, normalización, validaciones, JSON inválido, correos duplicados, altas simultáneas, códigos HTTP, cabecera y logs del middleware, y persistencia de actualizaciones y eliminaciones al reiniciar. Termina con código distinto de cero si falla. El workflow de GitHub Actions ejecuta estos mismos comandos al subir el proyecto.
