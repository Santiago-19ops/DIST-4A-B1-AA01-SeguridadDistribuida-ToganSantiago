
using System.ComponentModel.DataAnnotations;

namespace Notificaciones.Api.Models
{
    public class EnvioCorreo
    {
        [Key]
        public int IdEnvio { get; set; }

        [Required]
        [EmailAddress]
        public string CorreoDestino { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Asunto { get; set; } = string.Empty;

        [Required]
        public string Estado { get; set; } = "Pendiente";

        public DateTime FechaEnvio { get; set; } = DateTime.UtcNow;
    }
}
