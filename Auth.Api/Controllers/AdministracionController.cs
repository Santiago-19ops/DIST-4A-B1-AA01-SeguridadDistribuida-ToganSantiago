
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Auth.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Administrador")]
    public class AdministracionController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _usuarios;

        // El registro usa MaxValue para cuentas pendientes.
        // El dia anterior identifica las cuentas deshabilitadas.
        private static readonly DateTimeOffset BloqueoAdministrativo =
            DateTimeOffset.MaxValue.AddDays(-1);

        public AdministracionController(
            UserManager<IdentityUser> usuarios)
        {
            _usuarios = usuarios;
        }

        private static bool EstaPendiente(IdentityUser usuario)
        {
            return usuario.LockoutEnd == DateTimeOffset.MaxValue;
        }

        private static bool EstaDeshabilitado(IdentityUser usuario)
        {
            return usuario.LockoutEnd == BloqueoAdministrativo;
        }

        // Verificar el rol y estado actuales en AuthDB
        private async Task<bool> AdministradorAutorizado()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(id))
                return false;

            var actual = await _usuarios.FindByIdAsync(id);

            if (actual == null ||
                EstaPendiente(actual) ||
                EstaDeshabilitado(actual) ||
                await _usuarios.IsLockedOutAsync(actual))
                return false;

            return await _usuarios.IsInRoleAsync(
                actual, "Administrador");
        }

        private async Task<bool> EsUltimoAdministradorActivo()
        {
            var administradores =
                await _usuarios.GetUsersInRoleAsync("Administrador");

            int activos = administradores.Count(u =>
                !EstaPendiente(u) && !EstaDeshabilitado(u));

            return activos <= 1;
        }

        // GET: api/Administracion/usuarios
        [HttpGet("usuarios")]
        public async Task<IActionResult> ConsultarUsuarios()
        {
            if (!await AdministradorAutorizado())
                return Forbid();

            var usuarios = await _usuarios.Users
                .OrderBy(u => u.Email)
                .ToListAsync();

            var resultado = new List<object>();

            foreach (var usuario in usuarios)
            {
                var roles = await _usuarios.GetRolesAsync(usuario);

                string estado = EstaPendiente(usuario) ?
                    "Pendiente" :
                    EstaDeshabilitado(usuario) ?
                    "Deshabilitada" :
                    await _usuarios.IsLockedOutAsync(usuario) ?
                    "Bloqueada temporalmente" :
                    "Habilitada";

                resultado.Add(new
                {
                    UsuarioId = usuario.Id,
                    Correo = usuario.Email,
                    Roles = roles,
                    Estado = estado
                });
            }

            return Ok(resultado);
        }

        // PUT: api/Administracion/usuarios/{id}/rol
        [HttpPut("usuarios/{id}/rol")]
        public async Task<IActionResult> CambiarRol(
            string id,
            [FromBody] CambiarRolRequest datos)
        {
            if (!await AdministradorAutorizado())
                return Forbid();

            if (datos.Rol != "Cliente" &&
                datos.Rol != "Administrador")
                return BadRequest(
                    "El rol debe ser Cliente o Administrador.");

            var usuario = await _usuarios.FindByIdAsync(id);

            if (usuario == null)
                return NotFound("Usuario no encontrado.");

            if (EstaPendiente(usuario))
                return BadRequest(
                    "No se pueden cambiar roles de cuentas pendientes.");

            var rolesActuales =
                await _usuarios.GetRolesAsync(usuario);

            if (rolesActuales.Contains(datos.Rol))
                return Ok(new
                {
                    Mensaje = "El usuario ya tiene ese rol."
                });

            if (rolesActuales.Contains("Administrador") &&
                datos.Rol == "Cliente" &&
                await EsUltimoAdministradorActivo())
                return BadRequest(
                    "No se puede quitar el ultimo administrador activo.");

            var agregado = await _usuarios.AddToRoleAsync(
                usuario, datos.Rol);

            if (!agregado.Succeeded)
                return BadRequest(
                    agregado.Errors.Select(e => e.Description));

            foreach (var rolAnterior in rolesActuales)
            {
                var quitado = await _usuarios.RemoveFromRoleAsync(
                    usuario, rolAnterior);

                if (!quitado.Succeeded)
                {
                    await _usuarios.RemoveFromRoleAsync(
                        usuario, datos.Rol);

                    return StatusCode(500,
                        "No se pudo completar el cambio de rol.");
                }
            }

            return Ok(new
            {
                Mensaje = "Rol actualizado.",
                UsuarioId = id,
                Rol = datos.Rol
            });
        }

        // PUT: api/Administracion/usuarios/{id}/estado
        [HttpPut("usuarios/{id}/estado")]
        public async Task<IActionResult> CambiarEstado(
            string id,
            [FromBody] CambiarEstadoRequest datos)
        {
            if (!await AdministradorAutorizado())
                return Forbid();

            if (!datos.Habilitado.HasValue)
                return BadRequest(
                    "Debes indicar Habilitado: true o false.");

            bool habilitar = datos.Habilitado.Value;

            var usuario = await _usuarios.FindByIdAsync(id);

            if (usuario == null)
                return NotFound("Usuario no encontrado.");

            // No activar cuentas con perfil pendiente
            if (EstaPendiente(usuario))
                return BadRequest(
                    "Cuenta pendiente: primero debe confirmarse su perfil.");

            if (!habilitar &&
                !EstaDeshabilitado(usuario) &&
                await _usuarios.IsInRoleAsync(
                    usuario, "Administrador") &&
                await EsUltimoAdministradorActivo())
                return BadRequest(
                    "No se puede deshabilitar al ultimo administrador activo.");

            if (habilitar && !EstaDeshabilitado(usuario))
                return Ok(new
                {
                    Mensaje = "La cuenta ya esta habilitada."
                });

            if (!habilitar && EstaDeshabilitado(usuario))
                return Ok(new
                {
                    Mensaje = "La cuenta ya esta deshabilitada."
                });

            var fecha = habilitar ?
                (DateTimeOffset?)null :
                BloqueoAdministrativo;

            var cambio = await _usuarios.SetLockoutEndDateAsync(
                usuario, fecha);

            if (!cambio.Succeeded)
                return BadRequest(
                    cambio.Errors.Select(e => e.Description));

            return Ok(new
            {
                Mensaje = habilitar ?
                    "Cuenta habilitada." :
                    "Cuenta deshabilitada.",
                UsuarioId = id
            });
        }
    }

    public class CambiarRolRequest
    {
        [Required]
        public string Rol { get; set; } = string.Empty;
    }

    public class CambiarEstadoRequest
    {
        [Required]
        public bool? Habilitado { get; set; }
    }
}
