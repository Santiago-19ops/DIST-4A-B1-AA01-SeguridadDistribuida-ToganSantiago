
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Usuarios.Api.Data;
using Usuarios.Api.Models;

namespace Usuarios.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PerfilesController : ControllerBase
    {
        private readonly UsuariosDbContext _context;
        private readonly IConfiguration _configuration;

        public PerfilesController(
            UsuariosDbContext context,
            IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // POST: api/Perfiles/interno
        // Crear perfiles desde Auth.Api
        [HttpPost("interno")]
        public async Task<IActionResult> CrearPerfil(
            [FromBody] CrearPerfilRequest datos,
            [FromHeader(Name = "X-Internal-Key")] string? clave)
        {
            var claveEsperada =
                _configuration["Security:InternalApiKey"];

            if (string.IsNullOrWhiteSpace(claveEsperada))
                return StatusCode(503, "Servicio no configurado.");

            var recibida = Encoding.UTF8.GetBytes(clave ?? "");
            var esperada = Encoding.UTF8.GetBytes(claveEsperada);

            if (recibida.Length != esperada.Length ||
                !CryptographicOperations.FixedTimeEquals(
                    recibida, esperada))
            {
                return Unauthorized();
            }

            bool existe = await _context.Perfiles
                .AnyAsync(p => p.UsuarioId == datos.UsuarioId);

            if (existe)
                return Conflict("El usuario ya tiene un perfil.");

            var perfil = new Perfil
            {
                UsuarioId = datos.UsuarioId,
                Nombre = datos.Nombre.Trim(),
                Correo = datos.Correo.Trim()
            };

            _context.Perfiles.Add(perfil);
            await _context.SaveChangesAsync();

            return StatusCode(201, perfil);
        }

        // GET: api/Perfiles/mi-perfil
        // Cada usuario consulta su propio perfil
        [Authorize]
        [HttpGet("mi-perfil")]
        public async Task<IActionResult> MiPerfil()
        {
            var usuarioId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(usuarioId))
                return Unauthorized();

            var perfil = await _context.Perfiles
                .FirstOrDefaultAsync(
                    p => p.UsuarioId == usuarioId);

            if (perfil == null)
                return NotFound("No se encontro el perfil.");

            return Ok(perfil);
        }

        // PUT: api/Perfiles/mi-perfil
        // Cada usuario actualiza su propio perfil
        [Authorize]
        [HttpPut("mi-perfil")]
        public async Task<IActionResult> ActualizarMiPerfil(
            [FromBody] ActualizarPerfilRequest datos)
        {
            var usuarioId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(usuarioId))
                return Unauthorized();

            var perfil = await _context.Perfiles
                .FirstOrDefaultAsync(
                    p => p.UsuarioId == usuarioId);

            if (perfil == null)
                return NotFound("No se encontro el perfil.");

            perfil.Nombre = datos.Nombre.Trim();

            await _context.SaveChangesAsync();

            return Ok(perfil);
        }

        // GET: api/Perfiles/todos
        // Solo los administradores pueden consultar todos
        [Authorize(Roles = "Administrador")]
        [HttpGet("todos")]
        public async Task<IActionResult> ConsultarTodos()
        {
            var perfiles = await _context.Perfiles
                .ToListAsync();

            return Ok(perfiles);
        }
    }

    public class CrearPerfilRequest
    {
        [Required]
        public string UsuarioId { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Correo { get; set; } = string.Empty;
    }

    public class ActualizarPerfilRequest
    {
        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;
    }
}
