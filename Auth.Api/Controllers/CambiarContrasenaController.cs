
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Auth.Api.Controllers
{
    [ApiController]
    [Route("api/Auth")]
    [Authorize]
    public class CambiarContrasenaController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _usuarios;

        public CambiarContrasenaController(UserManager<IdentityUser> usuarios)
        {
            _usuarios = usuarios;
        }

        // POST: api/Auth/cambiar-contrasena
        [HttpPost("cambiar-contrasena")]
        public async Task<IActionResult> CambiarContrasena(
            [FromBody] CambiarContrasenaRequest datos)
        {
            if (datos.NuevaContrasena != datos.ConfirmarContrasena)
                return BadRequest("Las contraseñas nuevas no coinciden.");

            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(id))
                return Unauthorized();

            var usuario = await _usuarios.FindByIdAsync(id);

            if (usuario == null)
                return Unauthorized();

            // No permitir cambios en cuentas bloqueadas
            if (await _usuarios.IsLockedOutAsync(usuario))
                return Forbid();

            // Identity comprueba la contraseña actual
            // antes de actualizar la contraseña guardada.
            var resultado = await _usuarios.ChangePasswordAsync(
                usuario, datos.ContrasenaActual, datos.NuevaContrasena);

            if (!resultado.Succeeded)
                return BadRequest(new
                {
                    Mensaje = "No se pudo cambiar la contraseña.",
                    Errores = resultado.Errors.Select(e => e.Description)
                });

            return Ok(new
            {
                Mensaje = "Contraseña cambiada correctamente."
            });
        }
    }

    public class CambiarContrasenaRequest
    {
        [Required]
        public string ContrasenaActual { get; set; } = string.Empty;

        [Required]
        public string NuevaContrasena { get; set; } = string.Empty;

        [Required]
        public string ConfirmarContrasena { get; set; } = string.Empty;
    }
}
