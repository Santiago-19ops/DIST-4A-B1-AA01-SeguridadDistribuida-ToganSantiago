
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace Auth.Api.Controllers
{
    [ApiController]
    [Route("api/interno/permisos")]
    public class PermisosInternosController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _usuarios;
        private readonly IConfiguration _configuration;

        public PermisosInternosController(
            UserManager<IdentityUser> usuarios,
            IConfiguration configuration)
        {
            _usuarios = usuarios;
            _configuration = configuration;
        }

        // GET: api/interno/permisos/{usuarioId}
        // Usuarios.Api consulta los permisos actuales en AuthDB.
        // No se permite llamar sin la clave interna.
        [HttpGet("{usuarioId}")]
        public async Task<IActionResult> ConsultarPermisos(
            string usuarioId,
            [FromHeader(Name = "X-Internal-Key")] string? clave)
        {
            // Leer la clave privada desde User Secrets.
            var claveEsperada =
                _configuration["Security:InternalApiKey"];

            if (string.IsNullOrWhiteSpace(claveEsperada))
                return StatusCode(503, "Servicio no configurado.");

            // Comprobar la clave de comunicacion interna.
            var recibida = Encoding.UTF8.GetBytes(clave ?? "");
            var esperada = Encoding.UTF8.GetBytes(claveEsperada);

            if (recibida.Length != esperada.Length ||
                !CryptographicOperations.FixedTimeEquals(
                    recibida, esperada))
            {
                return Unauthorized();
            }

            // Consultar el usuario REAL en AuthDB.
            var usuario = await _usuarios.FindByIdAsync(usuarioId);

            if (usuario == null)
                return NotFound("Usuario no encontrado.");

            // Una cuenta pendiente, deshabilitada o bloqueada
            // no debe seguir accediendo con un JWT antiguo.
            bool tieneBloqueo =
                usuario.LockoutEnd.HasValue &&
                usuario.LockoutEnd.Value > DateTimeOffset.UtcNow;

            bool estaBloqueado =
                await _usuarios.IsLockedOutAsync(usuario);

            bool habilitado = !tieneBloqueo && !estaBloqueado;

            // Obtener los roles actuales, no los del JWT.
            var rolesActuales =
                await _usuarios.GetRolesAsync(usuario);

            return Ok(new
            {
                Habilitado = habilitado,
                Roles = rolesActuales
            });
        }
    }
}
