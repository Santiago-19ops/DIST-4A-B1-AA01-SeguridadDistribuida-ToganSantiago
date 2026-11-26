
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

namespace Auth.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _usuarios;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserManager<IdentityUser> usuarios,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _usuarios = usuarios;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        // POST: api/Auth/registro
        [HttpPost("registro")]
        public async Task<IActionResult> Registro(
            [FromBody] RegistroRequest datos)
        {
            var clave = _configuration["Security:InternalApiKey"];

            if (string.IsNullOrWhiteSpace(clave))
                return StatusCode(503,
                    "Falta configurar la clave interna.");

            if (datos.Contrasena != datos.ConfirmarContrasena)
                return BadRequest("Las contraseñas no coinciden.");

            string correo = datos.Correo.Trim();

            var usuario = new IdentityUser
            {
                UserName = correo,
                Email = correo,
                LockoutEnabled = true,
                LockoutEnd = DateTimeOffset.MaxValue
            };

            var resultado = await _usuarios.CreateAsync(
                usuario, datos.Contrasena);

            if (!resultado.Succeeded)
            {
                return BadRequest(resultado.Errors
                    .Select(e => e.Description));
            }

            var rol = await _usuarios.AddToRoleAsync(
                usuario, "Cliente");

            if (!rol.Succeeded)
            {
                await _usuarios.DeleteAsync(usuario);

                return StatusCode(500,
                    "No se pudo asignar el rol.");
            }

            // Crear perfil en Usuarios.Api
            try
            {
                var cliente = _httpClientFactory
                    .CreateClient("UsuariosApi");

                using var solicitud = new HttpRequestMessage(
                    HttpMethod.Post, "api/Perfiles/interno");

                solicitud.Headers.Add("X-Internal-Key", clave);

                solicitud.Content = JsonContent.Create(new
                {
                    UsuarioId = usuario.Id,
                    Nombre = datos.Nombre.Trim(),
                    Correo = correo
                });

                using var respuesta =
                    await cliente.SendAsync(solicitud);

                if (!respuesta.IsSuccessStatusCode)
                {
                    return StatusCode(503,
                        "No se pudo confirmar el perfil. Cuenta pendiente.");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "Usuarios.Api no disponible.");

                return StatusCode(503,
                    "Usuarios.Api no esta disponible. Cuenta pendiente.");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex,
                    "Tiempo de espera agotado en Usuarios.Api.");

                return StatusCode(503,
                    "Tiempo de espera agotado. Cuenta pendiente.");
            }

            var activacion = await _usuarios.SetLockoutEndDateAsync(
                usuario, null);

            if (!activacion.Succeeded)
                return StatusCode(503,
                    "La cuenta sigue pendiente de activacion.");

            // Enviar correo de bienvenida
            bool correoEnviado = await EnviarCorreoBienvenida(
                datos.Nombre.Trim(), correo);

            return StatusCode(201, new
            {
                Mensaje = "Usuario y perfil creados correctamente.",
                UsuarioId = usuario.Id,
                Rol = "Cliente",
                CorreoBienvenida = correoEnviado
                    ? "Enviado"
                    : "No enviado"
            });
        }

        // Enviar bienvenida desde Notificaciones.Api
        private async Task<bool> EnviarCorreoBienvenida(
            string nombre, string correo)
        {
            var clave = _configuration[
                "Security:NotificationsApiKey"];

            if (string.IsNullOrWhiteSpace(clave))
            {
                _logger.LogWarning(
                    "Falta la clave de Notificaciones.Api.");

                return false;
            }

            try
            {
                var cliente = _httpClientFactory
                    .CreateClient("NotificacionesApi");

                using var solicitud = new HttpRequestMessage(
                    HttpMethod.Post,
                    "api/Correos/interno/bienvenida");

                solicitud.Headers.Add("X-Internal-Key", clave);

                solicitud.Content = JsonContent.Create(new
                {
                    Nombre = nombre,
                    CorreoDestino = correo
                });

                using var respuesta =
                    await cliente.SendAsync(solicitud);

                return respuesta.IsSuccessStatusCode;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "No se pudo enviar la bienvenida.");

                return false;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex,
                    "Tiempo de espera agotado en Notificaciones.Api.");

                return false;
            }
        }

        // POST: api/Auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login(
            [FromBody] LoginRequest datos)
        {
            const string error =
                "Credenciales incorrectas o cuenta no disponible.";

            var usuario = await _usuarios.FindByEmailAsync(
                datos.Correo.Trim());

            if (usuario == null)
                return Unauthorized(error);

            if (await _usuarios.IsLockedOutAsync(usuario))
                return Unauthorized(error);

            bool contrasenaCorrecta =
                await _usuarios.CheckPasswordAsync(
                    usuario, datos.Contrasena);

            if (!contrasenaCorrecta)
            {
                await _usuarios.AccessFailedAsync(usuario);
                return Unauthorized(error);
            }

            await _usuarios.ResetAccessFailedCountAsync(usuario);

            var roles = await _usuarios.GetRolesAsync(usuario);

            var claims = new List<Claim>
            {
                new Claim(
                    JwtRegisteredClaimNames.Sub, usuario.Id),

                new Claim(
                    JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString()),

                new Claim(
                    ClaimTypes.NameIdentifier, usuario.Id),

                new Claim(
                    ClaimTypes.Email, usuario.Email ?? ""),

                new Claim(
                    ClaimTypes.Name, usuario.UserName ?? "")
            };

            foreach (var rol in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, rol));
            }

            var clave = _configuration["Jwt:Key"]
                ?? throw new Exception("Falta la clave JWT.");

            var llave = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(clave));

            var firma = new SigningCredentials(
                llave, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(60),
                signingCredentials: firma
            );

            return Ok(new
            {
                Mensaje = "Inicio de sesion correcto.",
                Token = new JwtSecurityTokenHandler()
                    .WriteToken(token),
                Tipo = "Bearer",
                Expira = token.ValidTo,
                Roles = roles
            });
        }

        // GET: api/Auth/mi-sesion
        [Authorize]
        [HttpGet("mi-sesion")]
        public IActionResult MiSesion()
        {
            return Ok(new
            {
                Mensaje = "Token valido.",
                UsuarioId = User.FindFirstValue(
                    ClaimTypes.NameIdentifier),
                Correo = User.FindFirstValue(
                    ClaimTypes.Email),
                Roles = User.FindAll(ClaimTypes.Role)
                    .Select(r => r.Value)
            });
        }

        // POST: api/Auth/olvide-contrasena
        [HttpPost("olvide-contrasena")]
        public async Task<IActionResult> OlvideContrasena(
            [FromBody] OlvideContrasenaRequest datos)
        {
            // Siempre devolver el mismo mensaje
            // para no revelar si el correo existe
            const string mensaje =
                "Si el correo esta registrado, recibiras " +
                "instrucciones para recuperar tu contraseña.";

            var usuario = await _usuarios.FindByEmailAsync(
                datos.Correo.Trim());

            if (usuario == null)
                return Ok(new { Mensaje = mensaje });

            // Generar token mediante ASP.NET Core Identity
            string codigo = await _usuarios
                .GeneratePasswordResetTokenAsync(usuario);

            var clave = _configuration[
                "Security:NotificationsApiKey"];

            if (string.IsNullOrWhiteSpace(clave))
            {
                _logger.LogWarning(
                    "Falta configurar la clave de notificaciones.");

                return Ok(new { Mensaje = mensaje });
            }

            try
            {
                var cliente = _httpClientFactory
                    .CreateClient("NotificacionesApi");

                using var solicitud = new HttpRequestMessage(
                    HttpMethod.Post,
                    "api/Correos/interno/recuperacion");

                solicitud.Headers.Add(
                    "X-Internal-Key", clave);

                solicitud.Content = JsonContent.Create(new
                {
                    CorreoDestino = datos.Correo.Trim(),
                    Codigo = codigo
                });

                using var respuesta =
                    await cliente.SendAsync(solicitud);

                if (!respuesta.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "No se pudo enviar el correo de recuperacion.");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "Notificaciones.Api no disponible.");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex,
                    "Tiempo de espera agotado al enviar recuperacion.");
            }

            return Ok(new { Mensaje = mensaje });
        }

        // POST: api/Auth/restablecer-contrasena
        [HttpPost("restablecer-contrasena")]
        public async Task<IActionResult> RestablecerContrasena(
            [FromBody] RestablecerContrasenaRequest datos)
        {
            if (datos.NuevaContrasena != datos.ConfirmarContrasena)
                return BadRequest(
                    "Las contraseñas no coinciden.");

            var usuario = await _usuarios.FindByEmailAsync(
                datos.Correo.Trim());

            if (usuario == null)
                return BadRequest(
                    "El codigo no es valido o ha expirado.");

            var resultado = await _usuarios.ResetPasswordAsync(
                usuario,
                datos.Codigo.Trim(),
                datos.NuevaContrasena);

            if (!resultado.Succeeded)
            {
                return BadRequest(new
                {
                    Mensaje = "No se pudo restablecer la contraseña.",
                    Errores = resultado.Errors
                        .Select(e => e.Description)
                });
            }

            return Ok(new
            {
                Mensaje = "Contraseña restablecida correctamente."
            });
        }
    }

    public class RegistroRequest
    {
        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Correo { get; set; } = string.Empty;

        [Required]
        public string Contrasena { get; set; } = string.Empty;

        [Required]
        public string ConfirmarContrasena { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        [Required]
        [EmailAddress]
        public string Correo { get; set; } = string.Empty;

        [Required]
        public string Contrasena { get; set; } = string.Empty;
    }

    public class OlvideContrasenaRequest
    {
        [Required]
        [EmailAddress]
        public string Correo { get; set; } = string.Empty;
    }

    public class RestablecerContrasenaRequest
    {
        [Required]
        [EmailAddress]
        public string Correo { get; set; } = string.Empty;

        [Required]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        public string NuevaContrasena { get; set; } = string.Empty;

        [Required]
        public string ConfirmarContrasena { get; set; } = string.Empty;
    }
}
